using Daiso.Core.Sessions;

namespace Daiso.Core.Tests.Sessions;

/// <summary>
/// <see cref="SessionDay"/> — 세션 목록의 날짜 묶음 (docs/TERMINAL_CARD_PLAN.md §6 Stage 4).
/// </summary>
public sealed class SessionDayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 15, 0, 0, TimeSpan.FromHours(9));

    private static SessionDayGroup At(int year, int month, int day, int hour = 12) =>
        SessionDay.Of(new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.FromHours(9)), Now);

    [Fact]
    public void 같은_날은_오늘이다()
    {
        At(2026, 9, 9, 0).Should().Be(SessionDayGroup.Today);
        At(2026, 9, 9, 23).Should().Be(SessionDayGroup.Today);
    }

    [Fact]
    public void 새벽에_본_어젯밤은_두_시간_전이어도_어제다()
    {
        // 경과가 아니라 달력 날짜가 기준이다 — SessionMoment 는 "2시간 전"이라고 하지만 묶음은 어제다
        var midnight = new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.FromHours(9));
        var lastNight = new DateTimeOffset(2026, 9, 8, 23, 0, 0, TimeSpan.FromHours(9));

        SessionDay.Of(lastNight, midnight).Should().Be(SessionDayGroup.Yesterday);
    }

    [Fact]
    public void 하루_전은_어제다() => At(2026, 9, 8).Should().Be(SessionDayGroup.Yesterday);

    [Theory]
    [InlineData(7)]
    [InlineData(5)]
    [InlineData(2)]
    public void 이레_안은_이번_주다(int day) => At(2026, 9, day).Should().Be(SessionDayGroup.ThisWeek);

    [Fact]
    public void 이레를_넘으면_그_이전이다()
    {
        At(2026, 9, 1).Should().Be(SessionDayGroup.Older);
        At(2026, 1, 1).Should().Be(SessionDayGroup.Older);
    }

    [Fact]
    public void 시계가_어긋나_앞선_날짜여도_오늘이다() => At(2026, 9, 10).Should().Be(SessionDayGroup.Today);
}
