namespace Daiso.Core;

/// <summary>구독 한도의 창 하나. 예: 5시간 창, 7일 창.</summary>
/// <param name="WindowMinutes">창의 길이(분). 5시간이면 300, 7일이면 10080.</param>
/// <param name="UsedPercent">쓴 비율. 0~100. 넘을 수도 있다.</param>
/// <param name="ResetsAt">창이 다시 차는 때. 모르면 null.</param>
public sealed record RateLimitWindow(int WindowMinutes, double UsedPercent, DateTimeOffset? ResetsAt);

/// <summary>
/// 도구가 마지막으로 알려 준 구독 한도. 도구를 쓸 때만 새로 적히므로 <see cref="At"/> 기준의 값이다
/// (docs/DECISIONS.md "구독 한도는 도구가 남긴 파일로만 읽는다").
/// </summary>
/// <param name="At">이 값이 적힌 때.</param>
/// <param name="Windows">창 목록. 짧은 창부터.</param>
/// <param name="Plan">요금제 이름. 도구가 알려 주지 않으면 null.</param>
public sealed record RateLimitSnapshot(DateTimeOffset At, IReadOnlyList<RateLimitWindow> Windows, string? Plan);
