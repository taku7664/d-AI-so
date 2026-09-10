using System.Runtime.CompilerServices;
using System.Text;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Codex;

/// <summary>Codex CLI 어댑터. (ARCHITECTURE §4.2)</summary>
public sealed class CodexProvider : IProvider, IUsageReader
{
    private const string RulesFile = "AGENTS.md";
    private const string RulesPresetFile = "PROJECT_RULES.daiso";
    private const int FirstPromptLength = 200;
    private const int MetaScanLines = 20;

    private readonly ProviderHome _home;

    public CodexProvider()
        : this(ProviderHome.FromUserProfile())
    {
    }

    public CodexProvider(ProviderHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    /// <inheritdoc />
    public ToolKind Kind => ToolKind.Codex;

    /// <inheritdoc />
    public string ExecutableName => "codex";

    /// <inheritdoc />
    public string InstallCommand => "npm install -g @openai/codex";

    /// <inheritdoc />
    public bool AppendOnlySessions => true;

    /// <inheritdoc />
    public string RulesFileName => RulesFile;

    /// <summary>`~/.codex`.</summary>
    public string ConfigDirectory => _home.Combine(".codex");

    /// <inheritdoc />
    /// <remarks>CLI 가 받아 둔 <c>models_cache.json</c> 을 읽는다 (<see cref="CodexModelCache"/>). CLI 를 한 번도 안 돌렸으면 빈 목록.</remarks>
    public async Task<IReadOnlyList<ModelOption>> ListModelsAsync(CancellationToken ct)
    {
        var path = Path.Combine(ConfigDirectory, CodexModelCache.FileName);

        try
        {
            return File.Exists(path)
                ? CodexModelCache.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false))
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // CLI 가 캐시를 새로 쓰는 중이다
            return [];
        }
    }

    /// <summary>날짜별 폴더가 있는 세션 루트.</summary>
    public string SessionsRoot => Path.Combine(ConfigDirectory, "sessions");

    /// <summary>보관된 세션 루트.</summary>
    public string ArchivedSessionsRoot => Path.Combine(ConfigDirectory, "archived_sessions");

    /// <inheritdoc />
    public IReadOnlyList<string> ContextFilePatterns(string projectDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var dir = ProjectPathNormalizer.Normalize(projectDir) ?? projectDir;
        var patterns = new List<string> { Path.Combine(ConfigDirectory, RulesFile) };

        // git 루트 → cwd 방향으로 AGENTS.md를 쌓는다.
        foreach (var directory in GitRootChain(dir))
        {
            patterns.Add(Path.Combine(directory, RulesFile));
        }

        patterns.Add(Path.Combine(dir, RulesPresetFile));

        // 홈 폴더가 상위 폴더에도 들어 있으면 같은 경로가 두 번 나온다. 로드 순서를 지키며 한 번만 남긴다.
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
        [new AuthFile(Path.Combine(ConfigDirectory, "auth.json"), Required: true)];

    /// <inheritdoc />
    public async Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct)
    {
        var authJson = await ReadTextOrNullAsync(Path.Combine(ConfigDirectory, "auth.json"), ct)
            .ConfigureAwait(false);

        return CodexAuthReader.Read(authJson, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var (root, isArchived) in new[] { (SessionsRoot, false), (ArchivedSessionsRoot, true) })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                yield return await ReadMetaAsync(file, isArchived, ct).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var info = new FileInfo(filePath);
        var parser = new CodexRecordParser();

        string? sessionId = null;
        string? cwd = null;
        string? version = null;
        string? model = null;
        DateTimeOffset? startedAt = null;
        string? firstPrompt = null;
        var users = 0;
        var assistants = 0;

        // token_count는 누적값이라 마지막 non-null 하나만 쓴다.
        TokenUsage? cumulative = null;

        await foreach (var line in JsonlReader.ReadLinesAsync(filePath, 0, ct).ConfigureAwait(false))
        {
            var record = parser.Parse(line);
            if (record is null)
            {
                continue;
            }

            sessionId ??= record.SessionId;
            cwd ??= record.Cwd;
            version ??= record.CliVersion;
            model ??= record.Model;
            startedAt ??= record.Timestamp;

            if (record.CumulativeUsage is { } usage)
            {
                cumulative = usage;
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
            ToolKind.Codex,
            sessionId ?? SessionIdFromFileName(filePath),
            filePath,
            ProjectPathNormalizer.Normalize(cwd),
            startedAt ?? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            info.Exists ? info.Length : 0,
            users,
            assistants,
            firstPrompt,
            (cumulative ?? TokenUsage.Zero) with { Model = model },
            version,
            IsArchived: IsUnderArchive(filePath),
            IsActive: false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionMessage> ReadMessagesAsync(
        string filePath,
        long fromByteOffset,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var parser = new CodexRecordParser();

        await foreach (var line in JsonlReader.ReadLinesAsync(filePath, fromByteOffset, ct).ConfigureAwait(false))
        {
            var record = parser.Parse(line);
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
    /// <summary>Ctrl+V. Codex CLI 는 이 키로 클립보드 그림을 첨부한다.</summary>
    public string ImagePasteKeys => "\x16";

    public string BuildResumeArguments(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"resume {session.Id}";
    }

    /// <inheritdoc />
    /// <remarks>Codex의 token_count는 세션 누적값이라 날짜별로 더하면 중복된다.</remarks>
    public bool UsageIsAdditive => false;

    /// <inheritdoc />
    public async IAsyncEnumerable<UsageDay> ReadUsageAsync(
        string filePath,
        long fromByteOffset,
        DateOnly sessionDate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var parser = new CodexRecordParser();
        TokenUsage? cumulative = null;
        string? model = null;

        await foreach (var line in JsonlReader.ReadLinesAsync(filePath, fromByteOffset, ct).ConfigureAwait(false))
        {
            var record = parser.Parse(line);
            if (record is null)
            {
                continue;
            }

            model ??= record.Model;

            if (record.CumulativeUsage is { } usage)
            {
                cumulative = usage;
            }
        }

        if (cumulative is { } total)
        {
            yield return new UsageDay(sessionDate, total with { Model = model });
        }
    }

    private async Task<SessionInfo> ReadMetaAsync(string filePath, bool isArchived, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        var parser = new CodexRecordParser();

        string? sessionId = null;
        string? cwd = null;
        string? version = null;
        DateTimeOffset? startedAt = null;

        foreach (var line in await JsonlReader.ReadHeadLinesAsync(filePath, MetaScanLines, ct).ConfigureAwait(false))
        {
            var record = parser.Parse(line);
            if (record is null)
            {
                continue;
            }

            sessionId ??= record.SessionId;
            cwd ??= record.Cwd;
            version ??= record.CliVersion;
            startedAt ??= record.Timestamp;

            if (sessionId is not null && cwd is not null && version is not null && startedAt is not null)
            {
                break;
            }
        }

        return new SessionInfo(
            ToolKind.Codex,
            sessionId ?? SessionIdFromFileName(filePath),
            filePath,
            ProjectPathNormalizer.Normalize(cwd),
            startedAt ?? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            info.Length,
            UserMessageCount: 0,
            AssistantMessageCount: 0,
            FirstPrompt: null,
            TokenUsage.Zero,
            version,
            isArchived,
            IsActive: false);
    }

    private bool IsUnderArchive(string filePath) =>
        ProjectPathNormalizer.Normalize(filePath)?.StartsWith(
            ProjectPathNormalizer.Normalize(ArchivedSessionsRoot) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>`rollout-2026-09-07T21-02-50-&lt;uuid&gt;.jsonl` 에서 uuid 부분.</summary>
    private static string SessionIdFromFileName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var parts = name.Split('-');

        return parts.Length >= 5
            ? string.Join('-', parts[^5..])
            : name;
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
