using System.Runtime.CompilerServices;
using System.Text;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Gemini;

/// <summary>Gemini CLI 어댑터. (ARCHITECTURE §4.5)</summary>
public sealed class GeminiProvider : IProvider, IUsageReader
{
    private const string RulesFile = "GEMINI.md";
    private const string RulesPresetFile = "PROJECT_RULES.daiso";
    private const int FirstPromptLength = 200;
    private const int MetaScanLines = 3;

    /// <summary>Antigravity가 남기는 서버 세션. 대화가 없어 목록에서 뺀다.</summary>
    private const string ServerSessionId = "a2a-server";

    private readonly ProviderHome _home;

    public GeminiProvider()
        : this(ProviderHome.FromUserProfile())
    {
    }

    public GeminiProvider(ProviderHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    /// <inheritdoc />
    public ToolKind Kind => ToolKind.Gemini;

    /// <inheritdoc />
    public string ExecutableName => "gemini";

    /// <inheritdoc />
    public string InstallCommand => "npm install -g @google/gemini-cli";

    /// <inheritdoc />
    public string RulesFileName => RulesFile;

    /// <summary>`~/.gemini`.</summary>
    public string ConfigDirectory => _home.Combine(".gemini");

    /// <summary>프로젝트별 임시 폴더 루트. 그 아래 `{name|hash}/chats/session-*.jsonl`.</summary>
    public string SessionsRoot => Path.Combine(ConfigDirectory, "tmp");

    /// <inheritdoc />
    public IReadOnlyList<string> ContextFilePatterns(string projectDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var dir = ProjectPathNormalizer.Normalize(projectDir) ?? projectDir;
        var patterns = new List<string> { Path.Combine(ConfigDirectory, RulesFile) };

        // 프로젝트 루트(git) → cwd 방향으로 GEMINI.md를 쌓는다.
        foreach (var directory in GitRootChain(dir))
        {
            patterns.Add(Path.Combine(directory, RulesFile));
        }

        patterns.Add(Path.Combine(dir, RulesPresetFile));

        return patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public Task<bool> IsInstalledAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ExecutableLocator.ExistsOnPath(ExecutableName));
    }

    /// <inheritdoc />
    public IReadOnlyList<AuthFile> AuthFiles =>
    [
        new AuthFile(Path.Combine(ConfigDirectory, "oauth_creds.json"), Required: true),
        new AuthFile(Path.Combine(ConfigDirectory, "google_accounts.json"), Required: false),
    ];

    /// <inheritdoc />
    public async Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct)
    {
        var oauth = await ReadTextOrNullAsync(Path.Combine(ConfigDirectory, "oauth_creds.json"), ct).ConfigureAwait(false);
        var accounts = await ReadTextOrNullAsync(Path.Combine(ConfigDirectory, "google_accounts.json"), ct).ConfigureAwait(false);

        return GeminiAuthReader.Read(oauth, accounts, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!Directory.Exists(SessionsRoot))
        {
            yield break;
        }

        var projects = await LoadProjectMapAsync(ct).ConfigureAwait(false);

        foreach (var projectDir in Directory.EnumerateDirectories(SessionsRoot))
        {
            var chats = Path.Combine(projectDir, "chats");

            if (!Directory.Exists(chats))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(chats, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();

                var info = await ReadMetaAsync(file, Path.GetFileName(projectDir), projects, ct).ConfigureAwait(false);

                if (info is not null)
                {
                    yield return info;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var info = new FileInfo(filePath);
        var projects = await LoadProjectMapAsync(ct).ConfigureAwait(false);

        string? sessionId = null;
        string? projectHash = null;
        string? model = null;
        DateTimeOffset? startedAt = null;
        string? firstPrompt = null;
        var users = 0;
        var assistants = 0;
        var usage = TokenUsage.Zero;

        await foreach (var line in JsonlReader.ReadLinesAsync(filePath, 0, ct).ConfigureAwait(false))
        {
            var record = GeminiRecordParser.Parse(line);
            if (record is null)
            {
                continue;
            }

            sessionId ??= record.SessionId;
            projectHash ??= record.ProjectHash;
            startedAt ??= record.StartTime;
            model ??= record.Model;

            if (record.Usage is { } u)
            {
                usage = usage.Add(u);
            }

            foreach (var message in record.Messages)
            {
                switch (message.Role)
                {
                    case MessageRole.User:
                        users++;
                        firstPrompt ??= Shorten(message.Text);
                        break;
                    case MessageRole.Assistant:
                        assistants++;
                        break;
                    default:
                        break;
                }
            }
        }

        return new SessionInfo(
            ToolKind.Gemini,
            sessionId ?? SessionIdFromFileName(filePath),
            filePath,
            ProjectPathNormalizer.Normalize(ResolveProject(filePath, projectHash, projects)),
            startedAt ?? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            info.Exists ? info.Length : 0,
            users,
            assistants,
            firstPrompt,
            usage with { Model = model },
            ToolVersion: null,
            IsArchived: false,
            IsActive: false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionMessage> ReadMessagesAsync(
        string filePath,
        long fromByteOffset,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await foreach (var line in JsonlReader.ReadLinesAsync(filePath, fromByteOffset, ct).ConfigureAwait(false))
        {
            var record = GeminiRecordParser.Parse(line);
            if (record is null)
            {
                continue;
            }

            foreach (var message in record.Messages)
            {
                yield return message;
            }
        }
    }

    /// <inheritdoc />
    public string BuildResumeArguments(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"--resume {session.Id}";
    }

    /// <inheritdoc />
    /// <remarks>Gemini는 응답마다 그 응답의 토큰이 붙으므로 날짜별로 더한다.</remarks>
    public bool UsageIsAdditive => true;

    /// <inheritdoc />
    public async IAsyncEnumerable<UsageDay> ReadUsageAsync(
        string filePath,
        long fromByteOffset,
        DateOnly sessionDate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var byDay = new Dictionary<DateOnly, TokenUsage>();

        await foreach (var line in JsonlReader.ReadLinesAsync(filePath, fromByteOffset, ct).ConfigureAwait(false))
        {
            var record = GeminiRecordParser.Parse(line);
            if (record?.Usage is not { } usage)
            {
                continue;
            }

            var date = DateOnly.FromDateTime((record.Timestamp ?? default).UtcDateTime);
            if (date == default)
            {
                date = sessionDate;
            }

            byDay[date] = byDay.TryGetValue(date, out var existing) ? existing.Add(usage) : usage;
        }

        foreach (var (date, usage) in byDay.OrderBy(pair => pair.Key))
        {
            yield return new UsageDay(date, usage);
        }
    }

    /// <summary>헤더 줄만 읽어 메타를 만든다. 서버 세션은 null을 돌려 목록에서 뺀다.</summary>
    private async Task<SessionInfo?> ReadMetaAsync(
        string filePath,
        string projectDirectoryName,
        GeminiProjectMap projects,
        CancellationToken ct)
    {
        var info = new FileInfo(filePath);

        string? sessionId = null;
        string? projectHash = null;
        DateTimeOffset? startedAt = null;

        foreach (var line in await JsonlReader.ReadHeadLinesAsync(filePath, MetaScanLines, ct).ConfigureAwait(false))
        {
            var record = GeminiRecordParser.Parse(line);
            if (record is null)
            {
                continue;
            }

            sessionId ??= record.SessionId;
            projectHash ??= record.ProjectHash;
            startedAt ??= record.StartTime;

            if (sessionId is not null)
            {
                break;
            }
        }

        if (string.Equals(sessionId, ServerSessionId, StringComparison.Ordinal))
        {
            return null;
        }

        return new SessionInfo(
            ToolKind.Gemini,
            sessionId ?? SessionIdFromFileName(filePath),
            filePath,
            ProjectPathNormalizer.Normalize(projects.Resolve(projectDirectoryName) ?? projects.Resolve(projectHash)),
            startedAt ?? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            info.Length,
            UserMessageCount: 0,
            AssistantMessageCount: 0,
            FirstPrompt: null,
            TokenUsage.Zero,
            ToolVersion: null,
            IsArchived: false,
            IsActive: false);
    }

    private static string? ResolveProject(string filePath, string? projectHash, GeminiProjectMap projects)
    {
        // .../tmp/{name|hash}/chats/session.jsonl → 폴더 이름으로 먼저, 안 되면 헤더의 projectHash로
        var chats = Path.GetDirectoryName(filePath);
        var projectDir = chats is null ? null : Path.GetFileName(Path.GetDirectoryName(chats));

        return projects.Resolve(projectDir) ?? projects.Resolve(projectHash);
    }

    private async Task<GeminiProjectMap> LoadProjectMapAsync(CancellationToken ct) =>
        GeminiProjectMap.From(await ReadTextOrNullAsync(Path.Combine(ConfigDirectory, "projects.json"), ct).ConfigureAwait(false));

    /// <summary>`session-2026-05-25T14-57-a1b2c3d4.jsonl` 의 마지막 조각.</summary>
    private static string SessionIdFromFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var dash = name.LastIndexOf('-');

        return dash >= 0 && dash + 1 < name.Length ? name[(dash + 1)..] : name;
    }

    /// <summary>git 루트에서 프로젝트 폴더까지 위→아래 순서로.</summary>
    private static IEnumerable<string> GitRootChain(string dir)
    {
        var chain = new List<string>();

        for (var current = new DirectoryInfo(dir); current is not null; current = current.Parent)
        {
            chain.Add(current.FullName);

            if (Directory.Exists(Path.Combine(current.FullName, ".git"))
                || File.Exists(Path.Combine(current.FullName, ".git")))
            {
                break;
            }
        }

        chain.Reverse();
        return chain;
    }

    private static string Shorten(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= FirstPromptLength ? trimmed : trimmed[..FirstPromptLength];
    }

    private static async Task<string?> ReadTextOrNullAsync(string path, CancellationToken ct)
    {
        try
        {
            return File.Exists(path)
                ? await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
