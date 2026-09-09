using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Claude;

/// <summary>Claude Code CLI 어댑터. (ARCHITECTURE §4.1)</summary>
public sealed class ClaudeProvider : IProvider, IUsageReader
{
    private const string RulesFile = "CLAUDE.md";
    private const string LocalRulesFile = "CLAUDE.local.md";
    private const string RulesPresetFile = "PROJECT_RULES.daiso";
    private const int FirstPromptLength = 200;
    private const int MetaScanLines = 20;

    private readonly ProviderHome _home;
    private readonly IProcessProbe _processProbe;

    public ClaudeProvider()
        : this(ProviderHome.FromUserProfile(), new ProcessProbe())
    {
    }

    public ClaudeProvider(ProviderHome home, IProcessProbe processProbe)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(processProbe);

        _home = home;
        _processProbe = processProbe;
    }

    /// <inheritdoc />
    public ToolKind Kind => ToolKind.Claude;

    /// <inheritdoc />
    public string ExecutableName => "claude";

    /// <inheritdoc />
    public string InstallCommand => "npm install -g @anthropic-ai/claude-code";

    /// <inheritdoc />
    public bool AppendOnlySessions => true;

    /// <inheritdoc />
    public string RulesFileName => RulesFile;

    /// <inheritdoc />
    /// <remarks><c>@경로</c> import 를 읽는다. 이 앱이 쓰는 규칙 파일 연동이 그 문법에 기대고 있다.</remarks>
    public bool SupportsInstructionImports => true;

    /// <summary>`~/.claude`.</summary>
    public string ConfigDirectory => _home.Combine(".claude");

    /// <summary>세션 jsonl이 모여 있는 루트.</summary>
    public string SessionsRoot => Path.Combine(ConfigDirectory, "projects");

    /// <inheritdoc />
    public IReadOnlyList<string> ContextFilePatterns(string projectDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var dir = ProjectPathNormalizer.Normalize(projectDir) ?? projectDir;
        var patterns = new List<string> { Path.Combine(ConfigDirectory, RulesFile) };

        // 드라이브 루트 → 프로젝트 상위 폴더 순서로 쌓는다.
        foreach (var ancestor in Ancestors(dir))
        {
            patterns.Add(Path.Combine(ancestor, RulesFile));
            patterns.Add(Path.Combine(ancestor, ".claude", RulesFile));
        }

        patterns.Add(Path.Combine(dir, RulesFile));
        patterns.Add(Path.Combine(dir, ".claude", RulesFile));
        patterns.Add(Path.Combine(dir, LocalRulesFile));
        patterns.Add(Path.Combine(dir, ".claude", "rules", "*.md"));
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
    /// <remarks>`.credentials.json`이 로그인 자체이고, `.claude.json`은 계정 표시·설정이다.</remarks>
    public IReadOnlyList<AuthFile> AuthFiles =>
    [
        new AuthFile(Path.Combine(ConfigDirectory, ".credentials.json"), Required: true),
        new AuthFile(_home.Combine(".claude.json"), Required: false),
    ];

    /// <inheritdoc />
    public async Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct)
    {
        var credentials = await ReadTextOrNullAsync(
            Path.Combine(ConfigDirectory, ".credentials.json"), ct).ConfigureAwait(false);
        var claudeJson = await ReadTextOrNullAsync(
            _home.Combine(".claude.json"), ct).ConfigureAwait(false);

        return ClaudeAuthReader.Read(credentials, claudeJson, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!Directory.Exists(SessionsRoot))
        {
            yield break;
        }

        var active = ReadActiveSessionIds();

        foreach (var projectDir in Directory.EnumerateDirectories(SessionsRoot))
        {
            ct.ThrowIfCancellationRequested();

            // 하위 폴더(memory/ 등)는 무시하고 프로젝트 폴더 바로 아래 jsonl만 본다.
            foreach (var file in Directory.EnumerateFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                yield return await ReadMetaAsync(file, active, ct).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var info = new FileInfo(filePath);
        var active = ReadActiveSessionIds();

        string? sessionId = null;
        string? cwd = null;
        string? version = null;
        DateTimeOffset? startedAt = null;
        string? firstPrompt = null;
        var users = 0;
        var assistants = 0;
        var usage = TokenUsage.Zero;

        await foreach (var line in JsonlReader.ReadLinesAsync(filePath, 0, ct).ConfigureAwait(false))
        {
            var record = ClaudeRecordParser.Parse(line);
            if (record is null)
            {
                continue;
            }

            sessionId ??= record.SessionId;
            cwd ??= record.Cwd;
            version ??= record.Version;
            startedAt ??= record.Timestamp;

            if (record.Usage is { } recordUsage)
            {
                usage = usage.Add(recordUsage);
            }

            foreach (var message in record.Messages)
            {
                if (message.IsSidechain)
                {
                    continue;
                }

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

        var id = sessionId ?? Path.GetFileNameWithoutExtension(filePath);

        return new SessionInfo(
            ToolKind.Claude,
            id,
            filePath,
            ProjectPathNormalizer.Normalize(cwd),
            startedAt ?? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            info.Exists ? info.Length : 0,
            users,
            assistants,
            firstPrompt,
            usage,
            version,
            IsArchived: false,
            IsActive: active.Contains(id));
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
            var record = ClaudeRecordParser.Parse(line);
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
    /// <summary>Alt+V. Claude Code 는 Windows 에서 이 키로 클립보드 그림을 첨부한다.</summary>
    public string ImagePasteKeys => "\x1bv";

    public string BuildResumeArguments(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"--resume {session.Id}";
    }

    /// <inheritdoc />
    /// <remarks>Claude는 메시지마다 usage가 붙으므로 날짜별로 더한다.</remarks>
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
            var record = ClaudeRecordParser.Parse(line);
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

    /// <summary>본문을 훑지 않고 파일 앞부분에서만 메타를 읽는다.</summary>
    private async Task<SessionInfo> ReadMetaAsync(
        string filePath,
        IReadOnlySet<string> activeIds,
        CancellationToken ct)
    {
        var info = new FileInfo(filePath);

        string? sessionId = null;
        string? cwd = null;
        string? version = null;
        DateTimeOffset? startedAt = null;

        foreach (var line in await JsonlReader.ReadHeadLinesAsync(filePath, MetaScanLines, ct).ConfigureAwait(false))
        {
            var record = ClaudeRecordParser.Parse(line);
            if (record is null)
            {
                continue;
            }

            sessionId ??= record.SessionId;
            cwd ??= record.Cwd;
            version ??= record.Version;
            startedAt ??= record.Timestamp;

            if (sessionId is not null && cwd is not null && version is not null && startedAt is not null)
            {
                break;
            }
        }

        var id = sessionId ?? Path.GetFileNameWithoutExtension(filePath);

        return new SessionInfo(
            ToolKind.Claude,
            id,
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
            IsArchived: false,
            IsActive: activeIds.Contains(id));
    }

    /// <summary>`~/.claude/sessions/&lt;pid&gt;.json`에서 살아 있는 프로세스의 세션 ID를 모은다.</summary>
    private IReadOnlySet<string> ReadActiveSessionIds()
    {
        var directory = Path.Combine(ConfigDirectory, "sessions");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(directory))
        {
            return ids;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            string content;
            try
            {
                content = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            using var document = JsonHelpers.TryParseLine(content);
            if (document?.RootElement is not { ValueKind: JsonValueKind.Object } root)
            {
                continue;
            }

            var sessionId = root.Prop("sessionId").Text();
            var pid = root.Prop("pid").Number();

            if (sessionId is not null && pid is { } value && _processProbe.IsAlive((int)value))
            {
                ids.Add(sessionId);
            }
        }

        return ids;
    }

    private static IEnumerable<string> Ancestors(string dir)
    {
        var chain = new List<string>();

        for (var current = Directory.GetParent(dir); current is not null; current = current.Parent)
        {
            chain.Add(current.FullName);
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
