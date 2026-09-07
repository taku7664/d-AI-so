using System.Globalization;
using Daiso.Core;
using Microsoft.Data.Sqlite;

namespace Daiso.Infrastructure;

/// <summary>
/// 세션 메타·메시지·사용량을 SQLite에 담는 인덱스. (ARCHITECTURE §5.1)
/// 검색은 FTS5 trigram, 2글자 미만 질의는 LIKE 폴백.
/// </summary>
public sealed class SqliteSessionIndex : ISessionIndex, IDisposable
{
    /// <summary>trigram 인덱스로 찾을 수 있는 최소 길이.</summary>
    private const int TrigramMinimumLength = 3;

    private const int SnippetRadius = 40;

    private readonly IReadOnlyList<IProvider> _providers;

    /// <summary>쓰기 전용 연결. 갱신·재구축만 쓴다.</summary>
    private readonly SqliteConnection _connection;

    /// <summary>쓰기는 한 번에 하나만. 목록·검색은 각자 연결을 열어 기다리지 않는다.</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>읽기용 연결 문자열. WAL이라 쓰기 중에도 읽을 수 있다.</summary>
    private readonly string _connectionString;

    public SqliteSessionIndex(IEnumerable<IProvider> providers, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _providers = providers.ToList();

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();

        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        CreateSchema();
    }

    /// <summary>인덱스 위치를 바꾸는 환경 변수. 디스크가 빠듯한 머신에서 다른 드라이브로 옮길 때 쓴다.</summary>
    public const string DatabasePathVariable = "DAISO_INDEX_DB";

    /// <summary>기본 인덱스 파일 위치. <see cref="DatabasePathVariable"/>이 있으면 그 값을 쓴다.</summary>
    public static string DefaultDatabasePath =>
        Environment.GetEnvironmentVariable(DatabasePathVariable) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "d-AI-so",
                "index.db");

    public void Dispose()
    {
        _connection.Dispose();
        _writeGate.Dispose();
    }

    /// <summary>
    /// 읽기용 연결을 새로 연다.
    /// 하나의 연결을 화면과 배경 갱신이 같이 쓰면 리더가 겹쳐 깨진다 (IndexOutOfRange).
    /// </summary>
    private SqliteConnection OpenRead()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        return connection;
    }

    /// <inheritdoc />
    public async Task RebuildAsync(IProgress<IndexProgress> progress, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            Execute("DELETE FROM messages_fts; DELETE FROM sessions; DELETE FROM usage_daily;");

            var sessions = await CollectAsync(ct).ConfigureAwait(false);
            var done = 0;

            foreach (var (provider, session) in sessions)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new IndexProgress(done, sessions.Count, session.FilePath));

                await IndexAsync(provider, session, fromOffset: 0, previous: null, ct).ConfigureAwait(false);
                done++;
            }

            progress?.Report(new IndexProgress(done, sessions.Count, string.Empty));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in _providers)
            {
                await foreach (var session in provider.EnumerateSessionsAsync(ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    seen.Add(session.FilePath);

                    var previous = ReadRow(session.FilePath);

                    // 크기·수정 시각이 같으면 파일을 열지 않는다.
                    if (previous is not null
                        && previous.SizeBytes == session.SizeBytes
                        && previous.ModifiedAt == session.ModifiedAt)
                    {
                        UpdateLiveFlags(session);
                        continue;
                    }

                    // 커진 파일은 이전 오프셋부터, 줄어들었거나 새 파일은 처음부터 읽는다.
                    var append = previous is not null && session.SizeBytes > previous.SizeBytes;
                    var offset = append ? previous!.LastOffset : 0;

                    await IndexAsync(provider, session, offset, append ? previous : null, ct).ConfigureAwait(false);
                }
            }

            RemoveMissing(seen);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionInfo>> ListAsync(SessionFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ct.ThrowIfCancellationRequested();

        using var connection = OpenRead();
        using var command = connection.CreateCommand();
        var where = new List<string>();

        if (filter.Tool is { } tool)
        {
            where.Add("tool = $tool");
            command.Parameters.AddWithValue("$tool", tool.ToString());
        }

        if (filter.ProjectPath is { } project)
        {
            where.Add("project_path = $project COLLATE NOCASE");
            command.Parameters.AddWithValue("$project", project);
        }

        if (filter.From is { } from)
        {
            where.Add("substr(started_at, 1, 10) >= $from");
            command.Parameters.AddWithValue("$from", Text(from));
        }

        if (filter.To is { } to)
        {
            where.Add("substr(started_at, 1, 10) <= $to");
            command.Parameters.AddWithValue("$to", Text(to));
        }

        if (filter.OrphansOnly)
        {
            where.Add("(project_path IS NULL OR project_exists = 0)");
        }

        if (filter.MinSizeBytes is { } minSize)
        {
            where.Add("size_bytes >= $minSize");
            command.Parameters.AddWithValue("$minSize", minSize);
        }

        if (!filter.IncludeArchived)
        {
            where.Add("is_archived = 0");
        }

        command.CommandText =
            $"SELECT {SessionColumns} FROM sessions"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : string.Empty)
            + " ORDER BY modified_at DESC";

        var sessions = new List<SessionInfo>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            sessions.Add(ReadSession(reader));
        }

        return Task.FromResult<IReadOnlyList<SessionInfo>>(sessions);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);
        }

        using var connection = OpenRead();

        return Task.FromResult(trimmed.Length >= TrigramMinimumLength
            ? SearchWithFts(connection, trimmed)
            : SearchWithLike(connection, trimmed));
    }

    /// <inheritdoc />
    public Task<UsageSummary> GetUsageAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var connection = OpenRead();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT date, project, model, input, output, cache_create, cache_read
            FROM usage_daily
            WHERE date >= $from AND date <= $to
            """;
        command.Parameters.AddWithValue("$from", Text(from));
        command.Parameters.AddWithValue("$to", Text(to));

        var days = new Dictionary<DateOnly, TokenUsage>();
        var byProject = new Dictionary<string, TokenUsage>(StringComparer.OrdinalIgnoreCase);
        var byModel = new Dictionary<string, TokenUsage>(StringComparer.Ordinal);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var date = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var project = reader.IsDBNull(1) ? "(알 수 없음)" : reader.GetString(1);
            var model = reader.IsDBNull(2) ? "(알 수 없음)" : reader.GetString(2);
            var usage = new TokenUsage(
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), model);

            Accumulate(days, date, usage);
            Accumulate(byProject, project, usage);
            Accumulate(byModel, model, usage);
        }

        var orderedDays = days
            .OrderBy(pair => pair.Key)
            .Select(pair => new UsageDay(pair.Key, pair.Value))
            .ToList();

        return Task.FromResult(new UsageSummary(orderedDays, byProject, byModel));
    }

    // ── 인덱싱 ───────────────────────────────────────────────────────────

    private async Task<List<(IProvider Provider, SessionInfo Session)>> CollectAsync(CancellationToken ct)
    {
        var sessions = new List<(IProvider, SessionInfo)>();

        foreach (var provider in _providers)
        {
            await foreach (var session in provider.EnumerateSessionsAsync(ct).ConfigureAwait(false))
            {
                sessions.Add((provider, session));
            }
        }

        return sessions;
    }

    /// <summary>
    /// 세션 한 건을 인덱스에 반영한다.
    /// <paramref name="previous"/>가 있으면 이어 읽기이므로 기존 카운트에 더한다.
    /// </summary>
    private async Task IndexAsync(
        IProvider provider,
        SessionInfo session,
        long fromOffset,
        SessionRow? previous,
        CancellationToken ct)
    {
        if (previous is null)
        {
            DeleteRows(session.FilePath);
        }

        var users = previous?.UserMessageCount ?? 0;
        var assistants = previous?.AssistantMessageCount ?? 0;
        var firstPrompt = previous?.FirstPrompt;

        using (var transaction = _connection.BeginTransaction())
        {
            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO messages_fts (file_path, at, role, text)
                VALUES ($path, $at, $role, $text)
                """;
            var pathParameter = insert.Parameters.Add("$path", SqliteType.Text);
            var atParameter = insert.Parameters.Add("$at", SqliteType.Text);
            var roleParameter = insert.Parameters.Add("$role", SqliteType.Text);
            var textParameter = insert.Parameters.Add("$text", SqliteType.Text);
            pathParameter.Value = session.FilePath;

            await foreach (var message in provider
                .ReadMessagesAsync(session.FilePath, fromOffset, ct)
                .ConfigureAwait(false))
            {
                // 도구 호출과 시스템 메시지는 검색 인덱스에 넣지 않는다. (ARCHITECTURE §2.2)
                if (message.Role is not (MessageRole.User or MessageRole.Assistant))
                {
                    continue;
                }

                atParameter.Value = Text(message.At);
                roleParameter.Value = message.Role.ToString();
                textParameter.Value = message.Text;
                insert.ExecuteNonQuery();

                if (message.IsSidechain)
                {
                    continue;
                }

                if (message.Role == MessageRole.User)
                {
                    users++;
                    firstPrompt ??= message.Text.Length <= 200 ? message.Text : message.Text[..200];
                }
                else
                {
                    assistants++;
                }
            }

            transaction.Commit();
        }

        var stored = session with
        {
            UserMessageCount = users,
            AssistantMessageCount = assistants,
            FirstPrompt = firstPrompt,
        };

        UpsertSession(stored, session.SizeBytes);
        await UpdateUsageAsync(provider, stored, fromOffset, ct).ConfigureAwait(false);
    }

    private async Task UpdateUsageAsync(
        IProvider provider,
        SessionInfo session,
        long fromOffset,
        CancellationToken ct)
    {
        if (provider is not IUsageReader usageReader)
        {
            return;
        }

        var sessionDate = DateOnly.FromDateTime(session.StartedAt.UtcDateTime);
        var project = session.ProjectPath;

        await foreach (var day in usageReader
            .ReadUsageAsync(session.FilePath, fromOffset, sessionDate, ct)
            .ConfigureAwait(false))
        {
            WriteUsage(session, project, day, usageReader.UsageIsAdditive);
        }
    }

    /// <summary>
    /// 날짜별 합산(Claude) 또는 세션 단위 덮어쓰기(Codex).
    /// 덮어쓰기는 세션 파일 단위로 행을 하나만 유지한다.
    /// </summary>
    private void WriteUsage(SessionInfo session, string? project, UsageDay day, bool additive)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = additive
            ? """
              INSERT INTO usage_daily
                  (date, tool, project, model, file_path, input, output, cache_create, cache_read)
              VALUES ($date, $tool, $project, $model, $path, $input, $output, $cacheCreate, $cacheRead)
              ON CONFLICT(date, tool, project, model, file_path) DO UPDATE SET
                  input = input + excluded.input,
                  output = output + excluded.output,
                  cache_create = cache_create + excluded.cache_create,
                  cache_read = cache_read + excluded.cache_read
              """
            : """
              INSERT INTO usage_daily
                  (date, tool, project, model, file_path, input, output, cache_create, cache_read)
              VALUES ($date, $tool, $project, $model, $path, $input, $output, $cacheCreate, $cacheRead)
              ON CONFLICT(date, tool, project, model, file_path) DO UPDATE SET
                  input = excluded.input,
                  output = excluded.output,
                  cache_create = excluded.cache_create,
                  cache_read = excluded.cache_read
              """;

        command.Parameters.AddWithValue("$date", Text(day.Date));
        command.Parameters.AddWithValue("$tool", session.Tool.ToString());
        command.Parameters.AddWithValue("$project", (object?)project ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)day.Usage.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$path", session.FilePath);
        command.Parameters.AddWithValue("$input", day.Usage.Input);
        command.Parameters.AddWithValue("$output", day.Usage.Output);
        command.Parameters.AddWithValue("$cacheCreate", day.Usage.CacheCreate);
        command.Parameters.AddWithValue("$cacheRead", day.Usage.CacheRead);
        command.ExecuteNonQuery();
    }

    private void UpsertSession(SessionInfo session, long lastOffset)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (
                file_path, tool, session_id, project_path, project_exists, started_at, modified_at,
                size_bytes, user_messages, assistant_messages, first_prompt,
                input, output, cache_create, cache_read, model,
                tool_version, is_archived, is_active, last_offset)
            VALUES (
                $path, $tool, $id, $project, $projectExists, $startedAt, $modifiedAt,
                $size, $users, $assistants, $firstPrompt,
                $input, $output, $cacheCreate, $cacheRead, $model,
                $version, $archived, $active, $offset)
            ON CONFLICT(file_path) DO UPDATE SET
                tool = excluded.tool,
                session_id = excluded.session_id,
                project_path = excluded.project_path,
                project_exists = excluded.project_exists,
                started_at = excluded.started_at,
                modified_at = excluded.modified_at,
                size_bytes = excluded.size_bytes,
                user_messages = excluded.user_messages,
                assistant_messages = excluded.assistant_messages,
                first_prompt = excluded.first_prompt,
                input = excluded.input,
                output = excluded.output,
                cache_create = excluded.cache_create,
                cache_read = excluded.cache_read,
                model = excluded.model,
                tool_version = excluded.tool_version,
                is_archived = excluded.is_archived,
                is_active = excluded.is_active,
                last_offset = excluded.last_offset
            """;

        command.Parameters.AddWithValue("$path", session.FilePath);
        command.Parameters.AddWithValue("$tool", session.Tool.ToString());
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$project", (object?)session.ProjectPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$projectExists", ProjectExists(session.ProjectPath) ? 1 : 0);
        command.Parameters.AddWithValue("$startedAt", Text(session.StartedAt));
        command.Parameters.AddWithValue("$modifiedAt", Text(session.ModifiedAt));
        command.Parameters.AddWithValue("$size", session.SizeBytes);
        command.Parameters.AddWithValue("$users", session.UserMessageCount);
        command.Parameters.AddWithValue("$assistants", session.AssistantMessageCount);
        command.Parameters.AddWithValue("$firstPrompt", (object?)session.FirstPrompt ?? DBNull.Value);
        command.Parameters.AddWithValue("$input", session.Usage.Input);
        command.Parameters.AddWithValue("$output", session.Usage.Output);
        command.Parameters.AddWithValue("$cacheCreate", session.Usage.CacheCreate);
        command.Parameters.AddWithValue("$cacheRead", session.Usage.CacheRead);
        command.Parameters.AddWithValue("$model", (object?)session.Usage.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", (object?)session.ToolVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$archived", session.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$active", session.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$offset", lastOffset);
        command.ExecuteNonQuery();
    }

    /// <summary>파일을 다시 열지 않아도 되는 항목(실행 중, 아카이브 여부)만 갱신한다.</summary>
    private void UpdateLiveFlags(SessionInfo session)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            UPDATE sessions SET is_active = $active, is_archived = $archived, project_exists = $exists
            WHERE file_path = $path
            """;
        command.Parameters.AddWithValue("$active", session.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$archived", session.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$exists", ProjectExists(session.ProjectPath) ? 1 : 0);
        command.Parameters.AddWithValue("$path", session.FilePath);
        command.ExecuteNonQuery();
    }

    private void DeleteRows(string filePath)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            DELETE FROM messages_fts WHERE file_path = $path;
            DELETE FROM usage_daily WHERE file_path = $path;
            """;
        command.Parameters.AddWithValue("$path", filePath);
        command.ExecuteNonQuery();
    }

    private void RemoveMissing(IReadOnlySet<string> seen)
    {
        var stale = new List<string>();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT file_path FROM sessions";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (!seen.Contains(path))
                {
                    stale.Add(path);
                }
            }
        }

        foreach (var path in stale)
        {
            DeleteRows(path);

            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM sessions WHERE file_path = $path";
            command.Parameters.AddWithValue("$path", path);
            command.ExecuteNonQuery();
        }
    }

    // ── 검색 ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<SearchHit> SearchWithFts(SqliteConnection connection, string query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SessionColumns.Replace("sessions.", "s.", StringComparison.Ordinal)},
                   m.at, m.role, m.text
            FROM messages_fts m
            JOIN sessions s ON s.file_path = m.file_path
            WHERE messages_fts MATCH $query
            ORDER BY s.modified_at DESC
            """;
        command.Parameters.AddWithValue("$query", $"\"{query.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");

        return ReadHits(command, query);
    }

    private static IReadOnlyList<SearchHit> SearchWithLike(SqliteConnection connection, string query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SessionColumns.Replace("sessions.", "s.", StringComparison.Ordinal)},
                   m.at, m.role, m.text
            FROM messages_fts m
            JOIN sessions s ON s.file_path = m.file_path
            WHERE m.text LIKE $like
            ORDER BY s.modified_at DESC
            """;
        command.Parameters.AddWithValue("$like", $"%{query}%");

        return ReadHits(command, query);
    }

    private static IReadOnlyList<SearchHit> ReadHits(SqliteCommand command, string query)
    {
        var hits = new List<SearchHit>();
        using var reader = command.ExecuteReader();
        var messageStart = SessionColumnCount;

        while (reader.Read())
        {
            var session = ReadSession(reader);
            var at = ParseTimestamp(reader.GetString(messageStart));
            var role = Enum.Parse<MessageRole>(reader.GetString(messageStart + 1));
            var text = reader.GetString(messageStart + 2);

            hits.Add(new SearchHit(session, new SessionMessage(at, role, text, false), Snippet(text, query)));
        }

        return hits;
    }

    /// <summary>일치 지점 주변만 잘라 보여준다.</summary>
    private static string Snippet(string text, string query)
    {
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return text.Length <= SnippetRadius * 2 ? text : text[..(SnippetRadius * 2)] + "…";
        }

        var start = Math.Max(0, index - SnippetRadius);
        var end = Math.Min(text.Length, index + query.Length + SnippetRadius);

        return (start > 0 ? "…" : string.Empty)
            + text[start..end]
            + (end < text.Length ? "…" : string.Empty);
    }

    // ── 스키마·매핑 ──────────────────────────────────────────────────────

    private const string SessionColumns = """
        sessions.tool, sessions.session_id, sessions.file_path, sessions.project_path,
        sessions.started_at, sessions.modified_at, sessions.size_bytes,
        sessions.user_messages, sessions.assistant_messages, sessions.first_prompt,
        sessions.input, sessions.output, sessions.cache_create, sessions.cache_read, sessions.model,
        sessions.tool_version, sessions.is_archived, sessions.is_active
        """;

    private const int SessionColumnCount = 18;

    private void CreateSchema() => Execute("""
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS sessions (
            file_path          TEXT PRIMARY KEY,
            tool               TEXT NOT NULL,
            session_id         TEXT NOT NULL,
            project_path       TEXT,
            project_exists     INTEGER NOT NULL DEFAULT 0,
            started_at         TEXT NOT NULL,
            modified_at        TEXT NOT NULL,
            size_bytes         INTEGER NOT NULL,
            user_messages      INTEGER NOT NULL,
            assistant_messages INTEGER NOT NULL,
            first_prompt       TEXT,
            input              INTEGER NOT NULL DEFAULT 0,
            output             INTEGER NOT NULL DEFAULT 0,
            cache_create       INTEGER NOT NULL DEFAULT 0,
            cache_read         INTEGER NOT NULL DEFAULT 0,
            model              TEXT,
            tool_version       TEXT,
            is_archived        INTEGER NOT NULL DEFAULT 0,
            is_active          INTEGER NOT NULL DEFAULT 0,
            last_offset        INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS ix_sessions_project ON sessions (project_path);
        CREATE INDEX IF NOT EXISTS ix_sessions_modified ON sessions (modified_at);

        CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5 (
            file_path UNINDEXED,
            at        UNINDEXED,
            role      UNINDEXED,
            text,
            tokenize = 'trigram'
        );

        CREATE TABLE IF NOT EXISTS usage_daily (
            date         TEXT NOT NULL,
            tool         TEXT NOT NULL,
            project      TEXT,
            model        TEXT,
            file_path    TEXT NOT NULL,
            input        INTEGER NOT NULL DEFAULT 0,
            output       INTEGER NOT NULL DEFAULT 0,
            cache_create INTEGER NOT NULL DEFAULT 0,
            cache_read   INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (date, tool, project, model, file_path)
        );
        """);

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private SessionRow? ReadRow(string filePath)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT size_bytes, modified_at, last_offset, user_messages, assistant_messages, first_prompt
            FROM sessions WHERE file_path = $path
            """;
        command.Parameters.AddWithValue("$path", filePath);

        using var reader = command.ExecuteReader();

        return reader.Read()
            ? new SessionRow(
                reader.GetInt64(0),
                ParseTimestamp(reader.GetString(1)),
                reader.GetInt64(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5))
            : null;
    }

    private static SessionInfo ReadSession(SqliteDataReader reader) => new(
        Enum.Parse<ToolKind>(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        ParseTimestamp(reader.GetString(4)),
        ParseTimestamp(reader.GetString(5)),
        reader.GetInt64(6),
        reader.GetInt32(7),
        reader.GetInt32(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        new TokenUsage(
            reader.GetInt64(10),
            reader.GetInt64(11),
            reader.GetInt64(12),
            reader.GetInt64(13),
            reader.IsDBNull(14) ? null : reader.GetString(14)),
        reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.GetInt32(16) != 0,
        reader.GetInt32(17) != 0);

    private static bool ProjectExists(string? projectPath) =>
        projectPath is not null && Directory.Exists(projectPath);

    private static void Accumulate<TKey>(Dictionary<TKey, TokenUsage> target, TKey key, TokenUsage usage)
        where TKey : notnull =>
        target[key] = target.TryGetValue(key, out var existing) ? existing.Add(usage) : usage;

    private static string Text(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static string Text(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>증분 판단에 쓰는 기존 행.</summary>
    private sealed record SessionRow(
        long SizeBytes,
        DateTimeOffset ModifiedAt,
        long LastOffset,
        int UserMessageCount,
        int AssistantMessageCount,
        string? FirstPrompt);
}
