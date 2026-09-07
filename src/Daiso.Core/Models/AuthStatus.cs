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
/// 도구별 로그인 상태. 토큰 값은 어떤 필드에도 담지 않는다. (ARCHITECTURE §7.1)
/// </summary>
public sealed record AuthStatus(
    ToolKind Tool,
    AuthState State,
    string? AccountLabel,
    string? Email,
    DateTimeOffset? SessionExpiresAt,
    IReadOnlyList<string> Extras)
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
