namespace Daiso.Core.Sessions;

/// <summary>
/// 세션 목록을 날짜로 묶을 때 쓰는 갈래.
/// <para>
/// 상대 시각("3시간 전")은 한 줄을 읽기 쉽게 하지만, 줄이 서른 개면 <b>어디까지가 오늘인지</b>가 안 보인다.
/// 묶음 머리가 그 경계를 그린다. (docs/TERMINAL_CARD_PLAN.md §4.7)
/// </para>
/// <para>
/// 경과 시간이 아니라 <b>달력 날짜</b>가 기준이다 — 새벽 1시에 본 어제 23시는 "2시간 전"이지만 날짜로는 어제다.
/// 문구는 여기서 만들지 않는다. Core는 화면 문구를 모른다.
/// </para>
/// </summary>
public enum SessionDayGroup
{
    /// <summary>오늘.</summary>
    Today,

    /// <summary>어제.</summary>
    Yesterday,

    /// <summary>그저께부터 이레 전까지.</summary>
    ThisWeek,

    /// <summary>그보다 오래됐다.</summary>
    Older,
}

/// <summary>날짜 묶음을 고른다.</summary>
public static class SessionDay
{
    /// <summary>
    /// <paramref name="when"/>이 <paramref name="now"/> 기준으로 어느 묶음인지 고른다.
    /// 둘은 같은 기준이어야 한다(둘 다 현지 시각이거나 둘 다 UTC).
    /// </summary>
    public static SessionDayGroup Of(DateTimeOffset when, DateTimeOffset now)
    {
        var days = (now.Date - when.Date).Days;

        return days switch
        {
            // 시계가 어긋나 앞선 날짜여도 오늘로 본다
            <= 0 => SessionDayGroup.Today,
            1 => SessionDayGroup.Yesterday,
            <= 7 => SessionDayGroup.ThisWeek,
            _ => SessionDayGroup.Older,
        };
    }
}
