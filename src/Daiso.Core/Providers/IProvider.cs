namespace Daiso.Core;

/// <summary>
/// AI CLI 도구 하나를 다루는 어댑터. 구현은 Daiso.Providers.* 에 있다. (ARCHITECTURE §3.2)
/// </summary>
public interface IProvider
{
    ToolKind Kind { get; }

    /// <summary>
    /// 이 도구를 화면에 어떻게 보일 것인가. <b>기본값을 두지 않는다</b> — 도구를 하나 더하면
    /// 컴파일러가 이름·색·로고를 묻는다. 빠뜨리면 회색 원에 물음표로 조용히 뜬다 (docs/PLUGIN_PLAN.md Stage 2).
    /// </summary>
    ToolDisplay Display { get; }

    /// <summary>이 도구가 세션 파일을 두는 뿌리 폴더. 방이 활성 세션 파일을 찾을 때 훑는다.</summary>
    string SessionsRoot { get; }

    /// <summary>실행 파일 이름. npm 셸(.cmd)만 사용한다.</summary>
    string ExecutableName { get; }

    /// <summary>
    /// 실제로 띄울 대상. 보통 <see cref="ExecutableName"/> 그대로다.
    /// <para>
    /// 방금 깐 도구는 다를 수 있다. 설치기가 <b>사용자 PATH 레지스트리</b>에 등록해도 이미 떠 있는 프로세스의 PATH 사본은 그대로이고,
    /// 그 프로세스가 띄우는 셸도 같은 환경을 물려받는다 — 이름만 넘기면 셸이 "그런 명령 없다"고 한다.
    /// 그래서 PATH 에서 못 찾고 아는 설치 위치에 있으면 <b>절대 경로</b>를 준다. 앱을 다시 켜지 않아도 방금 깐 도구가 열린다.
    /// </para>
    /// <para>
    /// 이름(<see cref="ExecutableName"/>)과 나눠 둔 이유: 이름은 문서·테스트가 고정으로 잠그는 값이고,
    /// 이것은 그 PC 의 상태에 따라 달라지는 값이다.
    /// </para>
    /// </summary>
    string LaunchTarget => ExecutableName;

    /// <summary>
    /// 로그인 절차를 띄울 때 <see cref="LaunchTarget"/> 뒤에 붙일 인자. 예: Codex 의 <c>login</c>, Claude 의 <c>auth login</c>.
    /// <para>
    /// 비어 있으면 도구를 그냥 띄운다 — 로그인이 도구 안에서 이뤄지는 경우다.
    /// <b>이미 로그인돼 있어도</b> 이 인자로 띄우면 새 계정으로 로그인하는 흐름이 시작돼야 한다.
    /// 예전에는 요약 화면이 Codex 만 <c>login</c> 을 알고 나머지는 빈 인자로 띄웠는데,
    /// 로그인된 Claude 를 그냥 띄우면 로그인 물음이 나오지 않아 다른 계정으로 바꿀 길이 없었다 (2026-09-11 사람의 지적).
    /// </para>
    /// </summary>
    string LoginArguments => string.Empty;

    /// <summary>
    /// 첫 프롬프트를 인자로 넘길 때 메시지 앞에 붙이는 깃발. 비어 있으면 메시지만 위치 인자로 넘긴다.
    /// <para>
    /// 예: Antigravity 의 <c>-i</c>. 예전에는 터미널 화면이 <c>Kind == Antigravity</c> 를 직접 보고 붙였는데,
    /// 그러면 같은 꼴을 쓰는 플러그인 도구에 프롬프트가 잘못 넘어간다 — 도구의 사정은 도구가 안다
    /// (<see cref="LoginArguments"/> 와 같은 이유, 2026-09-11).
    /// </para>
    /// </summary>
    string FirstPromptFlag => string.Empty;

    /// <summary>실행 파일이 없을 때 새 터미널에서 돌릴 설치 명령 한 줄. 예: <c>npm install -g @openai/codex</c>.</summary>
    string InstallCommand { get; }

    /// <summary>
    /// 설치 안내 페이지. null이 아니면 앱은 <see cref="InstallCommand"/>를 <b>돌리지 않고</b> 이 주소를 브라우저로 연다.
    /// <para>
    /// npm 으로 깔리는 도구(Claude·Codex)는 null이다 — 셸 명령 한 줄이 그대로 설치다.
    /// Antigravity 처럼 "원격 스크립트를 받아 실행"하는 설치는 다르다: 그 꼴은 백신이 흔히 차단하고
    /// (`irm … | iex` 는 메모리에서 코드를 실행하는 모양이라 특히 그렇다), 앱이 사용자에게 백신을 끄라고 할 수는 없다.
    /// 앱이 남의 스크립트를 대신 실행해 주는 것도 옳지 않다. 그래서 공식 안내 페이지를 열고 판단은 사람에게 맡긴다.
    /// </para>
    /// </summary>
    string? InstallUri => null;

    /// <summary>도구가 읽는 프로젝트 지시문 파일 이름. "CLAUDE.md" | "AGENTS.md" | "GEMINI.md".</summary>
    string RulesFileName { get; }

    /// <summary>
    /// 지시문 파일에서 <c>@경로</c> import 를 읽을 수 있는가.
    /// <para>
    /// <b>확인된 것만 true 다.</b> 기본값은 false — 모르는 도구로 옮길 때는 내용을 그 자리에 펼쳐 두므로
    /// 안전한 쪽으로 틀린다. 반대로 잘못 true 로 두면 대상 도구가 import 줄을 글자 그대로 읽는다.
    /// </para>
    /// </summary>
    bool SupportsInstructionImports => false;

    /// <summary>컨텍스트로 로드되는 파일 경로를 로드 순서대로 돌려준다. (ARCHITECTURE §4.4)</summary>
    IReadOnlyList<string> ContextFilePatterns(string projectDir);

    Task<bool> IsInstalledAsync(CancellationToken ct);

    Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct);

    /// <summary>세션 메타만 훑는다. 본문은 파싱하지 않는다.</summary>
    IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync(CancellationToken ct);

    /// <summary>본문을 스캔해 카운트·사용량까지 채운다.</summary>
    Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct);

    /// <summary>
    /// 세션 파일이 뒤에만 붙는 로그인가. true면 인덱스가 커진 만큼만 이어 읽고, false면 바뀔 때마다 처음부터 다시 읽는다.
    /// Claude·Codex는 true, Antigravity는 목록 교체·되감기 레코드가 있어 false다. (ARCHITECTURE §5.1)
    /// </summary>
    bool AppendOnlySessions { get; }

    /// <summary><paramref name="fromByteOffset"/>부터 이어 읽는다. <see cref="AppendOnlySessions"/>가 false인 도구는 오프셋을 무시하고 처음부터 읽는다.</summary>
    IAsyncEnumerable<SessionMessage> ReadMessagesAsync(string filePath, long fromByteOffset, CancellationToken ct);

    /// <summary>세션을 이어서 열기 위한 명령 인자. "--resume &lt;id&gt;" | "resume &lt;id&gt;".</summary>
    string BuildResumeArguments(SessionInfo session);

    /// <summary>
    /// CLI 가 클립보드의 그림을 읽어 첨부하게 하는 키 입력(터미널로 보내는 바이트). 터미널은 키만 보낼 수 있으므로 그림은 CLI 가 스스로 읽는다.
    /// Claude Code 는 Windows 에서 Alt+V(ESC v — Ctrl+V 는 터미널이 먹는다고 보고 피한다), Codex CLI·Antigravity CLI 는 Ctrl+V(0x16).
    /// </summary>
    string ImagePasteKeys { get; }

    /// <summary>
    /// 로그인 상태를 이루는 파일. 프로필 저장·전환이 이 목록만 다룬다. (ARCHITECTURE §5.7)
    /// 값은 읽지 않고 바이트로만 옮긴다.
    /// </summary>
    IReadOnlyList<AuthFile> AuthFiles { get; }

    /// <summary>
    /// 로그인이 <b>파일에</b> 들어 있는가. 계정 보관·전환(<c>IAuthProfileStore</c>)이 되는지를 가른다.
    ///
    /// <para>
    /// 프로필은 <see cref="AuthFiles"/> 를 복사했다가 되돌리는 방식이다. 그러니 로그인이 파일이 아닌 곳
    /// (예: Windows 자격 증명 관리자)에 있으면 <b>복사해도 계정이 바뀌지 않는다</b>.
    /// 그런데도 저장이 되면 "보관해 뒀다"고 믿게 만들어 놓고 되돌리기가 아무 일도 안 한다 — 조용히 틀리는 쪽이다.
    /// </para>
    ///
    /// <para>기본값은 true. 파일이 아닌 도구가 스스로 false 로 밝힌다.</para>
    /// </summary>
    bool LoginLivesInFiles => true;

    /// <summary>
    /// 이 도구가 받는 모델. 새 터미널 카드의 모델 칸이 쓰고, 고른 값은 <c>--model</c> 로 붙는다 (<see cref="ModelArgument"/>).
    ///
    /// <para>
    /// <b>얻는 길이 도구마다 다르다</b> — 공식 명령이 있는 도구(<c>agy models</c>), CLI 가 받아 둔 캐시 파일만 있는 도구(Codex),
    /// 목록이 아예 없어 문서의 별칭에 기대는 도구(Claude). 그래서 계약은 "목록을 달라"까지만 정하고 방법은 구현이 고른다.
    /// </para>
    ///
    /// <para>
    /// 못 읽으면 <b>빈 목록</b>이다. 예외로 알리지 않는다 — 모델 칸은 거들 뿐이고 사람은 인자 칸에 직접 적을 수 있다.
    /// 기본값도 빈 목록이라, 방법을 모르는 새 도구는 칸이 "목록 없음"으로 뜬다.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ModelOption>> ListModelsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ModelOption>>([]);
}
