namespace Daiso.Core;

/// <summary>
/// AI CLI 도구 하나를 다루는 어댑터. 구현은 Daiso.Providers.* 에 있다. (ARCHITECTURE §3.2)
/// </summary>
public interface IProvider
{
    ToolKind Kind { get; }

    /// <summary>이 도구가 세션 파일을 두는 뿌리 폴더. 방이 활성 세션 파일을 찾을 때 훑는다.</summary>
    string SessionsRoot { get; }

    /// <summary>실행 파일 이름. npm 셸(.cmd)만 사용한다.</summary>
    string ExecutableName { get; }

    /// <summary>실행 파일이 없을 때 새 터미널에서 돌릴 설치 명령 한 줄. 예: <c>npm install -g @openai/codex</c>.</summary>
    string InstallCommand { get; }

    /// <summary>도구가 읽는 프로젝트 지시문 파일 이름. "CLAUDE.md" | "AGENTS.md" | "GEMINI.md".</summary>
    string RulesFileName { get; }

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
    /// Claude·Codex는 true, Gemini는 목록 교체·되감기 레코드가 있어 false다. (ARCHITECTURE §5.1)
    /// </summary>
    bool AppendOnlySessions { get; }

    /// <summary><paramref name="fromByteOffset"/>부터 이어 읽는다. <see cref="AppendOnlySessions"/>가 false인 도구는 오프셋을 무시하고 처음부터 읽는다.</summary>
    IAsyncEnumerable<SessionMessage> ReadMessagesAsync(string filePath, long fromByteOffset, CancellationToken ct);

    /// <summary>세션을 이어서 열기 위한 명령 인자. "--resume &lt;id&gt;" | "resume &lt;id&gt;".</summary>
    string BuildResumeArguments(SessionInfo session);

    /// <summary>
    /// 로그인 상태를 이루는 파일. 프로필 저장·전환이 이 목록만 다룬다. (ARCHITECTURE §5.7)
    /// 값은 읽지 않고 바이트로만 옮긴다.
    /// </summary>
    IReadOnlyList<AuthFile> AuthFiles { get; }
}
