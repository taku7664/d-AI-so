using Daiso.Core;

namespace Daiso.Host.Tabs.Usage;

/// <summary>토큰 네 갈래와 합.</summary>
public sealed record UsageTokens(long Input, long Output, long CacheCreate, long CacheRead, long Total)
{
    public static readonly UsageTokens Zero = new(0, 0, 0, 0, 0);

    public static UsageTokens From(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return new(usage.Input, usage.Output, usage.CacheCreate, usage.CacheRead, usage.Total);
    }
}

/// <summary>그래프 한 칸. 하루·한 주(월요일 시작)·한 달(1일 시작).</summary>
/// <param name="Start">칸의 첫날.</param>
/// <param name="Tokens">그 칸의 합.</param>
public sealed record UsageBucket(DateOnly Start, UsageTokens Tokens);

/// <summary>프로젝트별·모델별 한 줄.</summary>
/// <param name="Key">프로젝트 경로 또는 모델 이름.</param>
/// <param name="Label">보여 줄 이름. 프로젝트는 폴더 이름이고, 겹치면 상위 폴더까지 붙인다.</param>
/// <param name="Tokens">합.</param>
/// <param name="Share">전체에서 차지하는 비율. 0~1.</param>
public sealed record UsageShare(string Key, string Label, UsageTokens Tokens, double Share);

/// <summary>최근 기간 합.</summary>
public sealed record UsageTotals(UsageTokens Today, UsageTokens Last7Days, UsageTokens Last30Days);

/// <summary>사용량 탭 한 화면.</summary>
/// <param name="FirstDay">기록이 있는 첫날. 기록이 없으면 null.</param>
/// <param name="Today">오늘. 날짜는 UTC 기준이다. 세션 기록의 날짜가 UTC 로 적힌다.</param>
/// <param name="Totals">오늘 · 최근 7일 · 최근 30일 합.</param>
/// <param name="Buckets">그래프 칸. 오늘이 든 칸까지 빈 칸 없이 이어진다.</param>
/// <param name="ByProject">많이 쓴 프로젝트 10개.</param>
/// <param name="ByModel">모델 전부. 많이 쓴 것부터.</param>
public sealed record UsageResponse(
    DateOnly? FirstDay,
    DateOnly Today,
    UsageTotals Totals,
    IReadOnlyList<UsageBucket> Buckets,
    IReadOnlyList<UsageShare> ByProject,
    IReadOnlyList<UsageShare> ByModel);

/// <summary>
/// 인덱스의 날짜별 사용량을 화면 한 장으로 만든다. 옛 <c>UsageViewModel</c>(old/src/Daiso.App/ViewModels)의 묶는 규칙을 옮겼다.
/// 다른 점: 기록이 없는 날도 0 칸으로 채운다. 옛 화면은 기록이 있는 날만 이어 붙여 빈 날이 그래프에서 사라졌다.
/// 단가표로 $ 를 셈하던 것은 뺐다 (docs/DECISIONS.md "사용량은 토큰과 구독 한도만").
/// </summary>
public static class UsageReport
{
    /// <summary>프로젝트 표에 보여 줄 줄 수. 옛 화면과 같다.</summary>
    public const int TopProjects = 10;

    /// <summary>칸 수. 일별은 한 달, 주별·월별은 열두 칸. 옛 화면과 같다.</summary>
    public static int BucketCount(UsageGrain grain) => grain == UsageGrain.Day ? 30 : 12;

    public static UsageResponse Build(UsageSummary summary, UsageGrain grain, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var byDay = summary.Days.ToDictionary(day => day.Date, day => day.Usage);

        return new UsageResponse(
            summary.Days.Count > 0 ? summary.Days.Min(day => day.Date) : null,
            today,
            new UsageTotals(Sum(byDay, today, today), Sum(byDay, today.AddDays(-6), today), Sum(byDay, today.AddDays(-29), today)),
            Buckets(byDay, grain, today),
            Shares(summary.ByProject, TopProjects, keys => PathLabels.For(keys, SessionLabels.Unknown)),
            Shares(summary.ByModel, int.MaxValue, keys => keys));
    }

    /// <summary>칸의 첫날. 주는 월요일, 달은 1일.</summary>
    public static DateOnly StartOf(DateOnly date, UsageGrain grain) => grain switch
    {
        UsageGrain.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        UsageGrain.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date,
    };

    private static DateOnly Next(DateOnly start, UsageGrain grain) => grain switch
    {
        UsageGrain.Week => start.AddDays(7),
        UsageGrain.Month => start.AddMonths(1),
        _ => start.AddDays(1),
    };

    private static List<UsageBucket> Buckets(Dictionary<DateOnly, TokenUsage> byDay, UsageGrain grain, DateOnly today)
    {
        var last = StartOf(today, grain);
        var first = grain switch
        {
            UsageGrain.Week => last.AddDays(-7 * (BucketCount(grain) - 1)),
            UsageGrain.Month => last.AddMonths(-(BucketCount(grain) - 1)),
            _ => last.AddDays(-(BucketCount(grain) - 1)),
        };

        var buckets = new List<UsageBucket>(BucketCount(grain));
        for (var start = first; start <= last; start = Next(start, grain))
        {
            buckets.Add(new UsageBucket(start, Sum(byDay, start, Next(start, grain).AddDays(-1))));
        }

        return buckets;
    }

    private static UsageTokens Sum(Dictionary<DateOnly, TokenUsage> byDay, DateOnly from, DateOnly to) =>
        UsageTokens.From(byDay
            .Where(pair => pair.Key >= from && pair.Key <= to)
            .Aggregate(TokenUsage.Zero, (sum, pair) => sum.Add(pair.Value)));

    private static List<UsageShare> Shares(
        IReadOnlyDictionary<string, TokenUsage> totals,
        int take,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> label)
    {
        var all = totals.Sum(pair => pair.Value.Total);
        var top = totals
            .Where(pair => pair.Value.Total > 0)
            .OrderByDescending(pair => pair.Value.Total)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(take)
            .ToList();
        var labels = label([.. top.Select(pair => pair.Key)]);

        return [.. top.Select((pair, i) => new UsageShare(
            pair.Key,
            labels[i],
            UsageTokens.From(pair.Value),
            all > 0 ? (double)pair.Value.Total / all : 0))];
    }
}

/// <summary>그래프를 묶는 단위.</summary>
public enum UsageGrain
{
    Day,
    Week,
    Month,
}
