namespace Daiso.Core;

/// <summary>
/// 로그인 상태를 이루는 파일 하나. (ARCHITECTURE §5.7)
/// </summary>
/// <param name="Path">파일 경로.</param>
/// <param name="Required">없으면 로그인으로 볼 수 없는 파일인가.</param>
public sealed record AuthFile(string Path, bool Required);

/// <summary>
/// 이름 붙여 보관한 로그인 상태. **토큰 값은 이 모델에 담지 않는다.**
/// 화면·로그에 그대로 써도 되는 표시용 정보만 있다. (ARCHITECTURE §7.1)
/// </summary>
/// <param name="Name">사람이 붙인 이름. 파일 이름으로도 쓰이므로 경로 문자는 걸러진다.</param>
/// <param name="Tool">어느 도구의 로그인인가.</param>
/// <param name="AccountLabel">계정 표시 이름. 저장 시점의 값.</param>
/// <param name="Email">계정 이메일. 없으면 null.</param>
/// <param name="SavedAt">저장한 시각.</param>
/// <param name="SessionExpiresAt">저장 시점 기준 재로그인이 필요해지는 시각.</param>
public sealed record AuthProfile(
    string Name,
    ToolKind Tool,
    string? AccountLabel,
    string? Email,
    DateTimeOffset SavedAt,
    DateTimeOffset? SessionExpiresAt);
