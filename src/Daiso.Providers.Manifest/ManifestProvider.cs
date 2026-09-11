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
public sealed class ManifestProvider : IProvider
{
    private readonly ToolManifest _manifest;
    private readonly string _manifestDirectory;
    private readonly string _home;

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
    }

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
    public string? InstallUri => _manifest.InstallUri;

    /// <inheritdoc />
    public string RulesFileName => _manifest.RulesFileName;

    /// <inheritdoc />
    public bool SupportsInstructionImports => _manifest.SupportsInstructionImports;

    /// <inheritdoc />
    public bool AppendOnlySessions => _manifest.AppendOnlySessions;

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
    /// <remarks>어댑터가 붙기 전에는 빈 목록이다. 화면은 "이 폴더의 대화 0개"로 뜬다.</remarks>
    public async IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <inheritdoc />
    public Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct) =>
        throw new NotSupportedException($"`{Kind.Id}` 는 세션 기록을 읽는 어댑터가 없다");

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionMessage> ReadMessagesAsync(
        string filePath,
        long fromByteOffset,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

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
    private string Fill(string template, string? projectDir = null) =>
        template
            .Replace("{USERPROFILE}", _home, StringComparison.Ordinal)
            .Replace("{HERE}", _manifestDirectory, StringComparison.Ordinal)
            .Replace("{PROJECT}", projectDir ?? string.Empty, StringComparison.Ordinal)
            .Replace('/', Path.DirectorySeparatorChar);
}
