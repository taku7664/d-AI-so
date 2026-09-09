using System.Runtime.CompilerServices;
using System.Text;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Antigravity;

/// <summary>
/// Google Antigravity CLI(`agy`) 어댑터. (ARCHITECTURE §4.5)
/// <para>
/// 2026-06-18 부터 개인 계정(무료·AI Pro·Ultra)에서 Gemini CLI 가 요청을 멈추고 Antigravity CLI 가 그 자리를 받았다.
/// 그래서 <b>실행·설치·설정은 Antigravity 쪽</b>이고, <b>세션 기록과 로그인 파일은 아직 `~/.gemini` 쪽</b>이다 —
/// 이 PC 에 남은 기록은 은퇴한 Gemini CLI 가 쓴 것이고, 그것을 지울 이유가 없으므로 계속 읽어 목록·검색·사용량에 보여 준다.
/// </para>
/// </summary>
public sealed class AntigravityProvider : IProvider, IUsageReader
{
    private const string RulesFile = "GEMINI.md";
    private const string RulesPresetFile = "PROJECT_RULES.daiso";
    private const int FirstPromptLength = 200;
    private const int MetaScanLines = 3;

    /// <summary>Antigravity가 남기는 서버 세션. 대화가 없어 목록에서 뺀다.</summary>
    private const string ServerSessionId = "a2a-server";

    private readonly ProviderHome _home;

    private readonly ICredentialProbe _credentials;

    public AntigravityProvider()
        : this(ProviderHome.FromUserProfile(), new WindowsCredentialProbe())
    {
    }

    public AntigravityProvider(ProviderHome home)
        : this(home, new WindowsCredentialProbe())
    {
    }

    public AntigravityProvider(ProviderHome home, ICredentialProbe credentials)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(credentials);

        _home = home;
        _credentials = credentials;
    }

    /// <inheritdoc />
    public ToolKind Kind => ToolKind.Antigravity;

    /// <inheritdoc />
    public string ExecutableName => "agy";

    /// <inheritdoc />
    /// <remarks>
    /// npm 패키지가 아니다. Go 로 만든 단일 실행 파일이라 공식 설치 스크립트를 받아 돌린다.
    /// 설치되는 곳은 `%LOCALAPPDATA%\agy\bin` 이고 설치 스크립트가 PATH 에 넣는다.
    /// <para>
    /// <b>앱이 이 명령을 돌리지는 않는다.</b> <see cref="InstallUri"/> 가 있으므로 안내 페이지를 열 뿐이다 —
    /// 이 값은 화면에 "공식 설치 명령은 이것"이라고 보여 주는 용도다. 이유는 <see cref="IProvider.InstallUri"/> 주석에 있다.
    /// </para>
    /// </remarks>
    public string InstallCommand => "irm https://antigravity.google/cli/install.ps1 | iex";

    /// <inheritdoc />
    public string? InstallUri => "https://antigravity.google/docs/cli/install/";

    /// <inheritdoc />
    /// <remarks>기록에 목록 교체(`$set.messages`)와 되감기(`$rewindTo`)가 있어 중간부터 이어 읽을 수 없다.</remarks>
    public bool AppendOnlySessions => false;

    /// <inheritdoc />
    public string RulesFileName => RulesFile;

    /// <summary>
    /// `~/.gemini`. Antigravity CLI 도 상태를 이 폴더 밑에 둔다(공식 설치 문서). 은퇴한 Gemini CLI 의 기록·로그인 파일도 여기 있다.
    /// </summary>
    public string HomeDirectory => _home.Combine(".gemini");

    /// <summary>`~/.gemini/antigravity-cli`. `settings.json`·`keybindings.json` 이 있는 곳.</summary>
    public string ConfigDirectory => Path.Combine(HomeDirectory, "antigravity-cli");

    /// <summary>
    /// 세션 기록 루트. 그 아래 `{name|hash}/chats/session-*.jsonl`.
    /// <para>
    /// 은퇴한 Gemini CLI 가 쓴 `~/.gemini/tmp` 다. Antigravity CLI 가 자기 기록을 어디에 어떤 형식으로 쓰는지는
    /// 아직 확인하지 못했다 — 같은 계열인 Antigravity IDE 는 `~/.gemini/antigravity/conversations/*.pb` 에 <b>암호화</b>해 두므로
    /// (12만 바이트에 읽을 수 있는 문자열이 하나도 없다) CLI 도 그렇다면 앱이 읽을 길이 없다.
    /// `agy` 를 깐 뒤 실제 파일을 보고 정한다.
    /// </para>
    /// </summary>
    public string SessionsRoot => Path.Combine(HomeDirectory, "tmp");

    /// <inheritdoc />
    public IReadOnlyList<string> ContextFilePatterns(string projectDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var dir = ProjectPathNormalizer.Normalize(projectDir) ?? projectDir;
        var patterns = new List<string> { Path.Combine(HomeDirectory, RulesFile) };

        // 프로젝트 루트(git) → cwd 방향으로 GEMINI.md를 쌓는다. Antigravity 가 이 이름을 그대로 읽는지는 확인 대상이다.
        foreach (var directory in GitRootChain(dir))
        {
            patterns.Add(Path.Combine(directory, RulesFile));
        }

        patterns.Add(Path.Combine(dir, RulesPresetFile));

        return patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>설치 스크립트가 실행 파일을 놓는 곳. PATH 등록은 사용자 PATH 레지스트리에 하므로 이미 돌던 프로세스는 못 본다.</summary>
    private static string DefaultBinaryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "agy",
            "bin",
            "agy.exe");

    /// <inheritdoc />
    /// <remarks>
    /// PATH 뿐 아니라 기본 설치 위치도 본다. 설치 스크립트는 <b>사용자 PATH 레지스트리</b>에 등록하고 브로드캐스트하는데,
    /// 이미 떠 있는 프로세스의 PATH 사본은 그대로다 — 설치기 자신도 "터미널을 다시 열라"고 안내한다.
    /// PATH 만 보면 앱을 다시 켜기 전까지 방금 깐 도구를 못 깔린 것으로 표시한다.
    /// </remarks>
    public Task<bool> IsInstalledAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ExecutableLocator.ExistsOnPath(ExecutableName) || File.Exists(DefaultBinaryPath));
    }

    /// <inheritdoc />
    /// <remarks>
    /// PATH 에 있으면 이름 그대로, 없고 기본 설치 위치에 있으면 그 절대 경로. 둘 다 아니면 이름을 준다(셸이 거절하게 둔다).
    /// 찾은 값은 기억한다 — 미리보기가 이 값을 자주 읽고, 한 번 찾은 실행 파일이 사라지는 일은 드물다.
    /// 못 찾은 결과는 기억하지 않는다. 앱이 떠 있는 동안 설치가 끝날 수 있다.
    /// </remarks>
    public string LaunchTarget
    {
        get
        {
            if (_launchTarget is { } cached)
            {
                return cached;
            }

            if (ExecutableLocator.ExistsOnPath(ExecutableName))
            {
                _launchTarget = ExecutableName;
            }
            else if (File.Exists(DefaultBinaryPath))
            {
                _launchTarget = DefaultBinaryPath;
            }

            return _launchTarget ?? ExecutableName;
        }
    }

    private string? _launchTarget;

    /// <inheritdoc />
    /// <remarks>
    /// Antigravity CLI 는 로그인 토큰을 파일이 아니라 <b>Windows 자격 증명 관리자</b>에 넣는다. 앱은 값을 읽지 않는다.
    /// 그래서 <b>필수 파일이 없고, 로그인 프로필(§5.7)로 옮길 수 있는 것도 없다</b> — 자격 증명은 파일이 아니라 복사 대상이 아니다.
    /// 여기 있는 것은 설정 파일 하나이고, 프로필에 담아도 로그인이 따라가지는 않는다.
    /// </remarks>
    public IReadOnlyList<AuthFile> AuthFiles =>
    [
        new AuthFile(Path.Combine(ConfigDirectory, "settings.json"), Required: false),
    ];

    /// <inheritdoc />
    public async Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct)
    {
        var settings = await ReadTextOrNullAsync(Path.Combine(ConfigDirectory, "settings.json"), ct).ConfigureAwait(false);
        var hasCredential = _credentials.Exists(AntigravityAuthReader.CredentialTarget);
        var hasLegacyLogin = File.Exists(Path.Combine(HomeDirectory, "oauth_creds.json"));

        return AntigravityAuthReader.Read(hasCredential, settings, hasLegacyLogin);
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
        var transcript = await ReplayAsync(filePath, ct).ConfigureAwait(false);

        string? firstPrompt = null;
        string? model = null;
        var users = 0;
        var assistants = 0;
        var usage = TokenUsage.Zero;

        foreach (var message in transcript.Messages)
        {
            model ??= message.Model;

            if (message.Usage is { } u)
            {
                usage = usage.Add(u);
            }

            switch (message.Type)
            {
                case "user" when message.Text.Length > 0:
                    users++;
                    firstPrompt ??= Shorten(message.Text);
                    break;
                case "gemini" when message.Text.Length > 0:
                    assistants++;
                    break;
                default:
                    break;
            }
        }

        return new SessionInfo(
            ToolKind.Antigravity,
            transcript.SessionId ?? SessionIdFromFileName(filePath),
            filePath,
            ProjectPathNormalizer.Normalize(ResolveProject(filePath, transcript.ProjectHash, projects)),
            transcript.StartTime ?? new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
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
    /// <remarks>오프셋은 무시한다. 기록을 처음부터 리플레이해야 최종 상태가 나온다.</remarks>
    public async IAsyncEnumerable<SessionMessage> ReadMessagesAsync(
        string filePath,
        long fromByteOffset,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var transcript = await ReplayAsync(filePath, ct).ConfigureAwait(false);

        foreach (var message in GeminiTranscriptReader.Flatten(transcript))
        {
            yield return message;
        }
    }

    /// <inheritdoc />
    /// <summary>Ctrl+V. Antigravity CLI 도 Gemini CLI 와 같은 readline 꼴이라 이 키로 클립보드 그림을 첨부한다.</summary>
    public string ImagePasteKeys => "\x16";

    /// <remarks>
    /// `agy --help` (1.1.28) 로 확인: `--conversation <ID>` 가 "Resume a previous conversation by ID" 다.
    /// `--resume` 는 없다 — 그것은 은퇴한 Gemini CLI 의 플래그였다. 가장 최근 대화만 이어려면 `--continue`(`-c`) 다.
    /// </remarks>
    public string BuildResumeArguments(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"--conversation {session.Id}";
    }

    /// <inheritdoc />
    /// <remarks>이 기록 형식은 응답마다 그 응답의 토큰이 붙으므로 날짜별로 더한다.</remarks>
    public bool UsageIsAdditive => true;

    /// <inheritdoc />
    public async IAsyncEnumerable<UsageDay> ReadUsageAsync(
        string filePath,
        long fromByteOffset,
        DateOnly sessionDate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var transcript = await ReplayAsync(filePath, ct).ConfigureAwait(false);
        var byDay = new Dictionary<DateOnly, TokenUsage>();

        foreach (var message in transcript.Messages)
        {
            if (message.Usage is not { } usage)
            {
                continue;
            }

            var date = message.At == default ? sessionDate : DateOnly.FromDateTime(message.At.UtcDateTime);
            byDay[date] = byDay.TryGetValue(date, out var existing) ? existing.Add(usage) : usage;
        }

        foreach (var (date, usage) in byDay.OrderBy(pair => pair.Key))
        {
            yield return new UsageDay(date, usage);
        }
    }

    /// <summary>파일을 처음부터 끝까지 리플레이한다.</summary>
    private static async Task<GeminiTranscript> ReplayAsync(string filePath, CancellationToken ct)
    {
        var reader = new GeminiTranscriptReader();

        await foreach (var line in JsonlReader.ReadLinesAsync(filePath, 0, ct).ConfigureAwait(false))
        {
            reader.Apply(line);
        }

        return reader.Result();
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

        var header = new GeminiTranscriptReader();

        foreach (var line in await JsonlReader.ReadHeadLinesAsync(filePath, MetaScanLines, ct).ConfigureAwait(false))
        {
            header.Apply(line);
        }

        var meta = header.Result();
        sessionId = meta.SessionId;
        projectHash = meta.ProjectHash;
        startedAt = meta.StartTime;

        if (string.Equals(sessionId, ServerSessionId, StringComparison.Ordinal))
        {
            return null;
        }

        return new SessionInfo(
            ToolKind.Antigravity,
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

        // projects.json 키는 소문자 경로다. 디스크의 실제 대소문자로 되돌려야 다른 도구의 같은 프로젝트와 한 묶음이 된다
        return ProjectPathNormalizer.RestoreCasing(projects.Resolve(projectDir) ?? projects.Resolve(projectHash));
    }

    private async Task<GeminiProjectMap> LoadProjectMapAsync(CancellationToken ct) =>
        GeminiProjectMap.From(await ReadTextOrNullAsync(Path.Combine(HomeDirectory, "projects.json"), ct).ConfigureAwait(false));

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
