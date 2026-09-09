namespace Daiso.Core.Sessions;

/// <summary>세션 시각을 사람 말로 옮길 때 고르는 갈래.</summary>
public enum SessionMomentKind
{
    /// <summary>1분이 안 됐다.</summary>
    JustNow,

    /// <summary>1시간이 안 됐다. 값은 분.</summary>
    MinutesAgo,

    /// <summary>24시간이 안 됐다. 값은 시간.</summary>
    HoursAgo,

    /// <summary>어제 날짜다. 값은 쓰지 않는다 — 부르는 쪽이 시:분을 붙인다.</summary>
    Yesterday,

    /// <summary>그보다 오래됐다. 부르는 쪽이 날짜를 그대로 쓴다.</summary>
    Older,
}

/// <summary>
/// "3시간 전"·"어제 15:02"처럼 읽히는 시각을 고르기 위한 갈래와 값.
/// <para>
/// <c>09-09 15:02</c> 같은 절대 시각만 보여 주면 2분 차이 나는 두 세션 중 어느 것이 최근인지
/// 눈으로 못 가린다 (docs/TERMINAL_CARD_PLAN.md §2-A).
/// </para>
/// <para>
/// <b>문구는 여기서 만들지 않는다.</b> Core는 화면 문구를 모른다 — 갈래만 정하고 말은 App이 붙인다.
/// 시간대 변환도 하지 않는다. 부르는 쪽이 같은 기준(둘 다 현지 시각)으로 넘긴다.
/// </para>
/// </summary>
public readonly record struct SessionMoment(SessionMomentKind Kind, int Value)
{
    /// <summary>
    /// <paramref name="when"/>이 <paramref name="now"/> 기준으로 어느 갈래인지 고른다.
    /// 둘은 같은 기준이어야 한다(둘 다 현지 시각이거나 둘 다 UTC).
    /// </summary>
    public static SessionMoment Of(DateTimeOffset when, DateTimeOffset now)
    {
        var elapsed = now - when;

        // 앞선 시각(시계가 어긋났거나 방금 쓴 파일)은 "방금"으로 본다
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return new SessionMoment(SessionMomentKind.JustNow, 0);
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return new SessionMoment(SessionMomentKind.MinutesAgo, (int)elapsed.TotalMinutes);
        }

        // 어제 23:50 을 오늘 00:10 에 보면 "어제"보다 "20분 전"이 낫다. 그래서 경과를 먼저 본다
        if (elapsed < TimeSpan.FromHours(24))
        {
            return new SessionMoment(SessionMomentKind.HoursAgo, (int)elapsed.TotalHours);
        }

        if (when.Date == now.Date.AddDays(-1))
        {
            return new SessionMoment(SessionMomentKind.Yesterday, 0);
        }

        return new SessionMoment(SessionMomentKind.Older, 0);
    }
}
