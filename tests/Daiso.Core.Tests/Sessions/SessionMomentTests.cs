using Daiso.Core.Sessions;

namespace Daiso.Core.Tests.Sessions;

/// <summary>
/// <see cref="SessionMoment"/> — 세션 시각을 "3시간 전"·"어제"로 읽히게 만들 갈래 고르기
/// (docs/TERMINAL_CARD_PLAN.md §6 Stage 0-3).
/// </summary>
public sealed class SessionMomentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 15, 0, 0, TimeSpan.FromHours(9));

    private static SessionMoment At(int days, int hours, int minutes) =>
        SessionMoment.Of(Now.AddDays(-days).AddHours(-hours).AddMinutes(-minutes), Now);

    [Fact]
    public void 방금_전은_JustNow다()
    {
        At(0, 0, 0).Should().Be(new SessionMoment(SessionMomentKind.JustNow, 0));
        At(0, 0, 0).Kind.Should().Be(SessionMomentKind.JustNow);
    }

    [Fact]
    public void 시계가_어긋나_앞선_시각이어도_JustNow다()
    {
        SessionMoment.Of(Now.AddMinutes(5), Now).Kind.Should().Be(SessionMomentKind.JustNow);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(59, 59)]
    public void 한_시간_안은_분으로_센다(int minutes, int expected)
    {
        At(0, 0, minutes).Should().Be(new SessionMoment(SessionMomentKind.MinutesAgo, expected));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(23)]
    public void 하루_안은_시간으로_센다(int hours)
    {
        At(0, hours, 0).Should().Be(new SessionMoment(SessionMomentKind.HoursAgo, hours));
    }

    [Fact]
    public void 어제_23시_50분을_오늘_0시_10분에_보면_어제가_아니라_20분_전이다()
    {
        var midnight = new DateTimeOffset(2026, 9, 9, 0, 10, 0, TimeSpan.FromHours(9));
        var lastNight = new DateTimeOffset(2026, 9, 8, 23, 50, 0, TimeSpan.FromHours(9));

        SessionMoment.Of(lastNight, midnight).Should().Be(new SessionMoment(SessionMomentKind.MinutesAgo, 20));
    }

    [Fact]
    public void 하루가_넘고_어제_날짜면_Yesterday다()
    {
        // 9/9 15:00 에서 25시간 전 = 9/8 14:00
        At(1, 1, 0).Kind.Should().Be(SessionMomentKind.Yesterday);
    }

    [Fact]
    public void 그보다_오래되면_Older다()
    {
        At(2, 0, 0).Kind.Should().Be(SessionMomentKind.Older);
        At(30, 0, 0).Kind.Should().Be(SessionMomentKind.Older);
    }
}
