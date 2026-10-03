using System.Runtime.CompilerServices;
using Daiso.Core;
using Daiso.Core.Plugins;
using Daiso.Providers.Common;

namespace Daiso.Providers.Manifest;

/// <summary>
/// 매니페스트 한 장을 앱이 아는 도구로 만든다 (docs/PLUGIN_PLAN.md Stage 4).
/// <para>
/// <b>세션 기록은 읽지 않는다.</b> 그것은 계산이라 데이터로 적을 수 없다(§4) — 바깥 어댑터가 할 일이다.
/// 어댑터가 없으면 세션 목록이 비고, 그 밖의 것(실행 · 설치 · 규칙 · 프롬프트 · 카드 · 탭)은 다 된다.
/// </para>
/// <para>
/// 경로의 자리(<c>{USERPROFILE}</c> · <c>{PROJECT}</c> · <c>{HERE}</c>)를 채우는 것이 여기 일이다.
/// Core 는 환경변수를 모르기 때문에 채우기가 이쪽에 있다.
/// </para>
/// </summary>
public sealed class ManifestProvider : IProvider, IDisposable
{
    private readonly ToolManifest _manifest;
    private readonly string _manifestDirectory;
    private readonly string _home;

    /// <summary>세션 기록을 읽어 주는 바깥 프로세스. 매니페스트에 어댑터가 없으면 null 이다.</summary>
    private readonly AdapterChannel? _adapter;

    /// <param name="manifest">읽어 낸 매니페스트.</param>
    /// <param name="manifestDirectory"><c>{HERE}</c> 가 가리키는 곳 — 매니페스트가 놓인 폴더.</param>
    /// <param name="home"><c>{USERPROFILE}</c> 가 가리키는 곳. 테스트는 임시 폴더를 넘긴다.</param>
    public ManifestProvider(ToolManifest manifest, string manifestDirectory, ProviderHome home)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(home);

        _manifest = manifest;
        _manifestDirectory = manifestDirectory;
        _home = home.Directory;

        _adapter = manifest.AdapterCommand is { Length: > 0 } command
            ? new AdapterChannel(FillCommand(command), manifestDirectory)
            : null;
    }

    /// <summary>어댑터가 어긋난 이유. 없거나 멀쩡하면 null. 설정 화면이 보여 준다 (Stage 6).</summary>
    public string? AdapterError => _adapter?.LastError;

    /// <summary>세션 기록을 읽을 수 있는가. 어댑터가 없으면 못 읽는다.</summary>
    public bool HasAdapter => _adapter is not null;

    /// <summary>읽어 낸 매니페스트 원본. 설정 화면이 무엇을 돌리는지 보여 줄 때 쓴다 (Stage 6).</summary>
    public ToolManifest Manifest => _manifest;

    /// <inheritdoc />
    public ToolKind Kind => _manifest.Kind;

    /// <inheritdoc />
    public ToolDisplay Display => _manifest.Display;

    /// <inheritdoc />
    public string SessionsRoot => Fill(_manifest.SessionsRoot);

    /// <inheritdoc />
    public string ExecutableName => _manifest.Executable;

    /// <inheritdoc />
    public string LaunchTarget => ExecutableLocator.Find(_manifest.Executable) ?? _manifest.Executable;

    /// <inheritdoc />
    public string InstallCommand => _manifest.InstallCommand;

    /// <inheritdoc />
    public string LoginArguments => _manifest.LoginArguments;

    /// <inheritdoc />
    public string FirstPromptFlag => _manifest.FirstPromptFlag;

    /// <inheritdoc />
    public string? InstallUri => _manifest.InstallUri;

    /// <inheritdoc />
    public string RulesFileName => _manifest.RulesFileName;

    /// <inheritdoc />
    public bool SupportsInstructionImports => _manifest.SupportsInstructionImports;

    /// <inheritdoc />
    /// <remarks>어댑터가 <c>hello</c> 에서 답한 값이 있으면 그것이 이긴다 — 로그 모양은 어댑터가 더 잘 안다.</remarks>
    public bool AppendOnlySessions => _adapter?.AppendOnly ?? _manifest.AppendOnlySessions;

    /// <inheritdoc />
    public string ImagePasteKeys => _manifest.ImagePasteKeys;

    /// <inheritdoc />
    public bool LoginLivesInFiles => _manifest.LoginLivesInFiles;

    /// <inheritdoc />
    public IReadOnlyList<AuthFile> AuthFiles =>
        [.. _manifest.AuthFiles.Select(file => new AuthFile(Fill(file.Path), file.Required))];

    /// <inheritdoc />
    public IReadOnlyList<string> ContextFilePatterns(string projectDir) =>
        [.. _manifest.ContextPatterns.Select(pattern => Fill(pattern, projectDir))];

    /// <inheritdoc />
    public Task<bool> IsInstalledAsync(CancellationToken ct) =>
        Task.FromResult(ExecutableLocator.ExistsOnPath(_manifest.Executable));

    /// <summary>
    /// 로그인 상태는 <b>파일이 있는가</b>로만 본다. 토큰을 열어 만료를 읽으려면 그 도구의 형식을 알아야 하는데,
    /// 매니페스트에 적을 수 있는 것이 아니다. 필요하면 어댑터가 더 자세히 답한다.
    /// </summary>
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct)
    {
        var files = AuthFiles.Where(file => file.Required).ToList();

        var missing = files.Count == 0 || files.Any(file => !File.Exists(file.Path));

        // 만료 시각을 모르므로 null 이다 — AuthStatus 는 그것을 "만료 개념이 없음 = 로그인됨"으로 읽는다
        return Task.FromResult(missing
            ? AuthStatus.Missing(Kind)
            : new AuthStatus(Kind, AuthState.LoggedIn, AccountLabel: null, Email: null, SessionExpiresAt: null, Extras: []));
    }

    /// <inheritdoc />
    /// <remarks>어댑터가 없으면 빈 목록이다. 화면은 "이 폴더의 대화 0개"로 뜬다 — 오류가 아니다.</remarks>
    public async IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        if (_adapter is null)
        {
            yield break;
        }

        var request = new AdapterRequest(AdapterProtocol.Version, "sessions", Root: SessionsRoot);

        await foreach (var line in _adapter.SendAsync(request, ct).ConfigureAwait(false))
        {
            if (line.Session is { } session)
            {
                yield return ToSessionInfo(session);
            }
        }
    }

    /// <inheritdoc />
    public async Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct)
    {
        if (_adapter is null)
        {
            throw new NotSupportedException($"`{Kind.Id}` 에는 세션 기록을 읽는 어댑터가 없다");
        }

        var request = new AdapterRequest(AdapterProtocol.Version, "session", FilePath: filePath);

        await foreach (var line in _adapter.SendAsync(request, ct).ConfigureAwait(false))
        {
            if (line.Session is { } session)
            {
                return ToSessionInfo(session);
            }
        }

        throw new InvalidOperationException(
            _adapter.LastError ?? $"어댑터가 세션을 돌려주지 않았다: {filePath}");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionMessage> ReadMessagesAsync(
        string filePath,
        long fromByteOffset,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_adapter is null)
        {
            yield break;
        }

        // 뒤에만 붙는 로그가 아니면 오프셋을 주지 않는다 — 어댑터가 처음부터 다시 읽어야 한다
        var request = new AdapterRequest(
            AdapterProtocol.Version,
            "messages",
            FilePath: filePath,
            FromByteOffset: AppendOnlySessions ? fromByteOffset : 0);

        await foreach (var line in _adapter.SendAsync(request, ct).ConfigureAwait(false))
        {
            if (line.Message is { } message)
            {
                yield return new SessionMessage(message.At, ParseRole(message.Role), message.Text, message.IsSidechain);
            }
        }
    }

    /// <summary>어댑터가 보낸 줄을 앱의 모양으로. 빠진 값은 안전한 쪽으로 채운다.</summary>
    private SessionInfo ToSessionInfo(AdapterSession session) => new(
        Kind,
        session.Id,
        session.FilePath,
        session.ProjectPath,
        session.StartedAt ?? default,
        session.ModifiedAt ?? session.StartedAt ?? default,
        session.SizeBytes,
        session.UserCount,
        session.AssistantCount,
        session.FirstPrompt,
        session.Usage is { } usage
            ? new TokenUsage(usage.Input, usage.Output, usage.CacheCreate, usage.CacheRead, usage.Model)
            : TokenUsage.Zero,
        session.ToolVersion,
        session.IsArchived,
        session.IsActive);

    /// <summary>모르는 역할은 <c>system</c> 으로 본다 — 화면에서 조용히 사라지는 것보다 낫다.</summary>
    private static MessageRole ParseRole(string? role) => role?.ToLowerInvariant() switch
    {
        "user" => MessageRole.User,
        "assistant" => MessageRole.Assistant,
        "tool" => MessageRole.Tool,
        _ => MessageRole.System,
    };

    /// <summary>말이 통하는지 한 번 물어본다. 어댑터가 없으면 참이다(쓸 일이 없다).</summary>
    public Task<bool> HandshakeAsync(CancellationToken ct) =>
        _adapter?.HandshakeAsync(ct) ?? Task.FromResult(true);

    /// <summary>어댑터가 마지막으로 어긋난 이유. 설정 화면이 보여 준다.</summary>
    public string? LastAdapterError => _adapter?.LastError;

    public void Dispose() => _adapter?.Dispose();

    /// <inheritdoc />
    public string BuildResumeArguments(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return _manifest.ResumeFormat.Replace("{id}", session.Id, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModelOption>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult(_manifest.Models);

    /// <summary>
    /// 경로의 자리를 채운다. <b>셋뿐이다</b> — 표현식·조건을 넣지 않는다(docs/PLUGIN_PLAN.md §7).
    /// 넣고 싶어지는 순간이 어댑터로 갈 신호다.
    /// </summary>
    /// <summary>
    /// <b>경로</b> 자리를 채운다. 매니페스트는 경로를 <c>/</c> 로 적으므로 이 판의 구분자로 바꾼다.
    /// </summary>
    private string Fill(string template, string? projectDir = null) =>
        FillCommand(template, projectDir).Replace('/', Path.DirectorySeparatorChar);

    /// <summary>
    /// <b>명령줄</b> 자리를 채운다. 여기서는 <c>/</c> 를 건드리지 않는다 —
    /// 전에는 경로와 같은 규칙을 써서 <c>cmd /c …</c> 가 <c>cmd \c …</c> 가 됐고,
    /// 그러면 cmd 가 인자를 못 알아듣고 대화 모드로 떨어져 우리 요청을 명령으로 실행했다 (2026-09-11).
    /// 명령 안의 경로는 매니페스트가 <c>{HERE}</c> 로 적고, 그 값은 이미 이 판의 구분자다.
    /// </summary>
    private string FillCommand(string template, string? projectDir = null) =>
        template
            .Replace("{USERPROFILE}", _home, StringComparison.Ordinal)
            .Replace("{HERE}", _manifestDirectory, StringComparison.Ordinal)
            .Replace("{PROJECT}", projectDir ?? string.Empty, StringComparison.Ordinal);
}
