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
    /// <remarks>로고는 simple-icons(CC0). 색은 OpenAI 초록.</remarks>
    public ToolDisplay Display { get; } = new(
        Title: "Codex CLI",
        Vendor: "OpenAI",
        Short: "Codex",
        Initial: "X",
        ColorStops: ["#10A37F"],
        LogoPath: "M22.2819 9.8211a5.9847 5.9847 0 0 0-.5157-4.9108 6.0462 6.0462 0 0 0-6.5098-2.9A6.0651 6.0651 0 0 0 4.9807 4.1818a5.9847 5.9847 0 0 0-3.9977 2.9 6.0462 6.0462 0 0 0 .7427 7.0966 5.98 5.98 0 0 0 .511 4.9107 6.051 6.051 0 0 0 6.5146 2.9001A5.9847 5.9847 0 0 0 13.2599 24a6.0557 6.0557 0 0 0 5.7718-4.2058 5.9894 5.9894 0 0 0 3.9977-2.9001 6.0557 6.0557 0 0 0-.7475-7.0729zm-9.022 12.6081a4.4755 4.4755 0 0 1-2.8764-1.0408l.1419-.0804 4.7783-2.7582a.7948.7948 0 0 0 .3927-.6813v-6.7369l2.02 1.1686a.071.071 0 0 1 .038.052v5.5826a4.504 4.504 0 0 1-4.4945 4.4944zm-9.6607-4.1254a4.4708 4.4708 0 0 1-.5346-3.0137l.142.0852 4.783 2.7582a.7712.7712 0 0 0 .7806 0l5.8428-3.3685v2.3324a.0804.0804 0 0 1-.0332.0615L9.74 19.9502a4.4992 4.4992 0 0 1-6.1408-1.6464zM2.3408 7.8956a4.485 4.485 0 0 1 2.3655-1.9728V11.6a.7664.7664 0 0 0 .3879.6765l5.8144 3.3543-2.0201 1.1685a.0757.0757 0 0 1-.071 0l-4.8303-2.7865A4.504 4.504 0 0 1 2.3408 7.872zm16.5963 3.8558L13.1038 8.364 15.1192 7.2a.0757.0757 0 0 1 .071 0l4.8303 2.7913a4.4944 4.4944 0 0 1-.6765 8.1042v-5.6772a.79.79 0 0 0-.407-.667zm2.0107-3.0231l-.142-.0852-4.7735-2.7818a.7759.7759 0 0 0-.7854 0L9.409 9.2297V6.8974a.0662.0662 0 0 1 .0284-.0615l4.8303-2.7866a4.4992 4.4992 0 0 1 6.6802 4.66zM8.3065 12.863l-2.02-1.1638a.0804.0804 0 0 1-.038-.0567V6.0742a4.4992 4.4992 0 0 1 7.3757-3.4537l-.142.0805L8.704 5.459a.7948.7948 0 0 0-.3927.6813zm1.0976-2.3654l2.602-1.4998 2.6069 1.4998v2.9994l-2.5974 1.4997-2.6067-1.4997Z",
        Order: 0);

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
        return TextCut.Head(trimmed, FirstPromptLength);
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
