namespace Daiso.Core.Plugins;

/// <summary>
/// 플러그인 도구 하나를 적은 파일의 내용. <c>%USERPROFILE%\.daiso\tools\*.yaml</c> (docs/PLUGIN_PLAN.md §7).
/// <para>
/// <b>데이터만 담는다.</b> 경로의 <c>{USERPROFILE}</c> · <c>{HERE}</c> 같은 자리는 여기서 채우지 않는다 —
/// Core 는 환경변수·파일을 모른다(<c>CorePurityTests</c>). 채우는 일은 이것을 <c>IProvider</c> 로 감싸는 쪽이 한다.
/// </para>
/// </summary>
/// <param name="Schema">매니페스트 판. 앱이 모르는 판이면 그 도구만 오류로 내린다.</param>
/// <param name="Kind">도구 id. 인덱스와 계정 보관함 폴더 이름이 된다 — 한 번 정하면 바꾸지 않는다.</param>
/// <param name="Display">이름·제작사·색·로고.</param>
/// <param name="Executable">실행 파일 이름. <c>mycli.cmd</c> 꼴.</param>
/// <param name="InstallCommand">새 터미널에서 돌릴 설치 명령 한 줄.</param>
/// <param name="InstallUri">이것이 있으면 명령을 돌리지 않고 이 주소를 연다.</param>
/// <param name="SessionsRoot">세션 파일이 있는 폴더. 자리 채우기 전의 글이다.</param>
/// <param name="AppendOnlySessions">뒤에만 붙는 로그인가. 아니면 바뀔 때마다 처음부터 다시 읽는다.</param>
/// <param name="RulesFileName">그 도구가 읽는 지시문 파일 이름.</param>
/// <param name="SupportsInstructionImports"><c>@경로</c> import 를 읽을 수 있는가. 확인된 것만 참이다.</param>
/// <param name="ContextPatterns">컨텍스트로 읽히는 파일 경로. 자리 채우기 전의 글이다.</param>
/// <param name="ResumeFormat">이어서 열 때 붙는 인자. <c>{id}</c> 자리에 세션 id 가 들어간다.</param>
/// <param name="ImagePasteKeys">CLI 에 그림을 붙여 넣게 하는 키 입력.</param>
/// <param name="LoginLivesInFiles">로그인이 파일에 들어 있는가. 아니면 계정 보관·전환을 내주지 않는다.</param>
/// <param name="AuthFiles">로그인을 이루는 파일. 자리 채우기 전의 글이다.</param>
/// <param name="Models">모델 칸에 보일 목록. 비어 있으면 "목록 없음".</param>
/// <param name="AdapterCommand">
/// 세션 기록을 읽어 주는 바깥 프로그램. 없으면 <b>세션 기록 없이</b> 동작한다 —
/// 실행 · 설치 · 규칙 · 프롬프트 · 카드 · 탭은 그대로 된다 (docs/PLUGIN_PLAN.md §5 D).
/// </param>
public sealed record ToolManifest(
    int Schema,
    ToolKind Kind,
    ToolDisplay Display,
    string Executable,
    string InstallCommand,
    string? InstallUri,
    string SessionsRoot,
    bool AppendOnlySessions,
    string RulesFileName,
    bool SupportsInstructionImports,
    IReadOnlyList<string> ContextPatterns,
    string ResumeFormat,
    string ImagePasteKeys,
    bool LoginLivesInFiles,
    IReadOnlyList<ManifestAuthFile> AuthFiles,
    IReadOnlyList<ModelOption> Models,
    string? AdapterCommand)
{
    /// <summary>앱이 읽을 수 있는 매니페스트 판. 늘릴 때는 옛 판도 계속 읽을 수 있게 둔다.</summary>
    public const int CurrentSchema = 1;
}

/// <summary>로그인 파일 한 줄. 경로는 자리 채우기 전의 글이다.</summary>
/// <param name="Path">파일 경로 틀.</param>
/// <param name="Required">없으면 로그인이 성립하지 않는 파일인가.</param>
public sealed record ManifestAuthFile(string Path, bool Required);

/// <summary>
/// 매니페스트 하나를 읽은 결과. <b>실패해도 예외를 던지지 않는다</b> —
/// 파일 하나가 깨져도 나머지 도구는 살아야 하고, 무엇이 틀렸는지는 사람에게 보여 줘야 한다
/// (docs/PLUGIN_PLAN.md Stage 6).
/// </summary>
/// <param name="Manifest">읽어 낸 것. 실패면 null.</param>
/// <param name="Errors">사람이 읽는 이유. 성공이면 비어 있다.</param>
public sealed record ToolManifestResult(ToolManifest? Manifest, IReadOnlyList<string> Errors)
{
    public bool Ok => Manifest is not null;

    public static ToolManifestResult Fail(params string[] errors) => new(null, errors);

    public static ToolManifestResult Success(ToolManifest manifest) => new(manifest, []);
}
