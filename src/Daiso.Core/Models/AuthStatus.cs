namespace Daiso.Core;

/// <summary>로그인 상태. (ARCHITECTURE §2.3)</summary>
public enum AuthState
{
    LoggedIn,
    ExpiringSoon,
    Expired,
    Missing,
}

/// <summary>
/// 로그인 카드에 한 줄로 붙는 부가 정보.
///
/// <para>
/// <b>완성된 문장을 담지 않는다.</b> Provider 는 어느 나라 말도 몰라야 한다 —
/// 예전에는 여기에 "만료: 알 수 없음 (CLI가 알아서 갱신합니다)" 같은 한국어가 그대로 들어 있어서
/// resw 밖에 있었고, 번역도 수정도 할 수 없었다.
/// 지금은 <b>문구 키와 값</b>만 담고, 그리는 쪽(앱)이 문구를 찾는다.
/// </para>
/// </summary>
/// <param name="Key">문구 키. 앱의 <c>Resources.resw</c> 항목 이름이다.</param>
/// <param name="Argument">문구의 <c>{0}</c> 자리에 들어갈 값. 없으면 null. <b>토큰 값은 절대 담지 않는다.</b></param>
public sealed record AuthNote(string Key, string? Argument = null);

/// <summary>
/// 도구별 로그인 상태. 토큰 값은 어떤 필드에도 담지 않는다. (ARCHITECTURE §7.1)
/// </summary>
public sealed record AuthStatus(
    ToolKind Tool,
    AuthState State,
    string? AccountLabel,
    string? Email,
    DateTimeOffset? SessionExpiresAt,
    IReadOnlyList<AuthNote> Extras)
{
    /// <summary>ExpiringSoon으로 볼 남은 기간.</summary>
    public static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromDays(7);

    /// <summary>인증 파일이 아예 없을 때의 상태.</summary>
    public static AuthStatus Missing(ToolKind tool) =>
        new(tool, AuthState.Missing, null, null, null, []);

    /// <summary>
    /// 재로그인이 필요해지는 시각으로 상태를 판정한다.
    /// null이면 만료 개념이 없는 것으로 보고 LoggedIn.
    /// </summary>
    public static AuthState StateFor(DateTimeOffset? sessionExpiresAt, DateTimeOffset now)
    {
        if (sessionExpiresAt is null)
        {
            return AuthState.LoggedIn;
        }

        if (sessionExpiresAt <= now)
        {
            return AuthState.Expired;
        }

        return sessionExpiresAt - now <= ExpiringSoonWindow ? AuthState.ExpiringSoon : AuthState.LoggedIn;
    }
}
