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
    /// <remarks>로고는 simple-icons(CC0). 색은 Anthropic 주황.</remarks>
    public ToolDisplay Display { get; } = new(
        Title: "Claude Code",
        Vendor: "Anthropic",
        Short: "Claude",
        Initial: "C",
        ColorStops: ["#D97757"],
        LogoPath: "m4.7144 15.9555 4.7174-2.6471.079-.2307-.079-.1275h-.2307l-.7893-.0486-2.6956-.0729-2.3375-.0971-2.2646-.1214-.5707-.1215-.5343-.7042.0546-.3522.4797-.3218.686.0608 1.5179.1032 2.2767.1578 1.6514.0972 2.4468.255h.3886l.0546-.1579-.1336-.0971-.1032-.0972L6.973 9.8356l-2.55-1.6879-1.3356-.9714-.7225-.4918-.3643-.4614-.1578-1.0078.6557-.7225.8803.0607.2246.0607.8925.686 1.9064 1.4754 2.4893 1.8336.3643.3035.1457-.1032.0182-.0728-.164-.2733-1.3539-2.4467-1.445-2.4893-.6435-1.032-.17-.6194c-.0607-.255-.1032-.4674-.1032-.7285L6.287.1335 6.6997 0l.9957.1336.419.3642.6192 1.4147 1.0018 2.2282 1.5543 3.0296.4553.8985.2429.8318.091.255h.1579v-.1457l.1275-1.706.2368-2.0947.2307-2.6957.0789-.7589.3764-.9107.7468-.4918.5828.2793.4797.686-.0668.4433-.2853 1.8517-.5586 2.9021-.3643 1.9429h.2125l.2429-.2429.9835-1.3053 1.6514-2.0643.7286-.8196.85-.9046.5464-.4311h1.0321l.759 1.1293-.34 1.1657-1.0625 1.3478-.8804 1.1414-1.2628 1.7-.7893 1.36.0729.1093.1882-.0183 2.8535-.607 1.5421-.2794 1.8396-.3157.8318.3886.091.3946-.3278.8075-1.967.4857-2.3072.4614-3.4364.8136-.0425.0304.0486.0607 1.5482.1457.6618.0364h1.621l3.0175.2247.7892.522.4736.6376-.079.4857-1.2142.6193-1.6393-.3886-3.825-.9107-1.3113-.3279h-.1822v.1093l1.0929 1.0686 2.0035 1.8092 2.5075 2.3314.1275.5768-.3218.4554-.34-.0486-2.2039-1.6575-.85-.7468-1.9246-1.621h-.1275v.17l.4432.6496 2.3436 3.5214.1214 1.0807-.17.3521-.6071.2125-.6679-.1214-1.3721-1.9246L14.38 17.959l-1.1414-1.9428-.1397.079-.674 7.2552-.3156.3703-.7286.2793-.6071-.4614-.3218-.7468.3218-1.4753.3886-1.9246.3157-1.53.2853-1.9004.17-.6314-.0121-.0425-.1397.0182-1.4328 1.9672-2.1796 2.9446-1.7243 1.8456-.4128.164-.7164-.3704.0667-.6618.4008-.5889 2.386-3.0357 1.4389-1.882.929-1.0868-.0062-.1579h-.0546l-6.3385 4.1164-1.1293.1457-.4857-.4554.0608-.7467.2307-.2429 1.9064-1.3114Z",
        Order: 1);

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

    /// <inheritdoc />
    /// <remarks>목록 명령이 없어 별칭과 <c>~/.claude.json</c> 의 계정별 모델을 합친다 (<see cref="ClaudeModelList"/>).</remarks>
    public async Task<IReadOnlyList<ModelOption>> ListModelsAsync(CancellationToken ct)
    {
        var path = _home.Combine(".claude.json");
        string? json = null;

        try
        {
            if (File.Exists(path))
            {
                json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // CLI 가 쓰는 중이다. 별칭만으로도 고를 수 있다
        }

        return ClaudeModelList.From(json);
    }

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
