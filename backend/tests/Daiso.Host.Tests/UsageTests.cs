using System.Net;
using System.Net.Http.Json;
using Daiso.Core;
using Daiso.Host.Services;
using Daiso.Host.Tabs.Usage;
using Microsoft.Extensions.DependencyInjection;

namespace Daiso.Host.Tests;

/// <summary>사용량 탭: 묶는 규칙(<see cref="UsageReport"/>)과 경로 <c>/api/usage</c>, 인덱스 갱신 <c>/api/index</c>.</summary>
public sealed class UsageTests
{
    private static readonly DateOnly Today = new(2026, 10, 7); // 수요일

    private static UsageSummary Summary(params (DateOnly Date, long Input)[] days) => new(
        [.. days.OrderBy(day => day.Date).Select(day => new UsageDay(day.Date, new TokenUsage(day.Input, 0, 0, 0, "m")))],
        new Dictionary<string, TokenUsage>(),
        new Dictionary<string, TokenUsage>());

    [Fact]
    public void Daily_buckets_are_the_last_30_days_without_gaps()
    {
        var report = UsageReport.Build(Summary((Today.AddDays(-40), 5), (Today.AddDays(-2), 7), (Today, 3)), UsageGrain.Day, Today);

        report.Buckets.Should().HaveCount(30);
        report.Buckets[^1].Start.Should().Be(Today);
        report.Buckets[0].Start.Should().Be(Today.AddDays(-29));
        report.Buckets.Count(bucket => bucket.Tokens.Total == 0).Should().Be(28, because: "기록이 없는 날도 0 칸으로 남아야 그래프에서 빈 날이 보인다");
        report.FirstDay.Should().Be(Today.AddDays(-40));
    }

    [Fact]
    public void Weeks_start_on_monday_and_months_on_the_first()
    {
        UsageReport.StartOf(Today, UsageGrain.Week).Should().Be(new DateOnly(2026, 10, 5));
        UsageReport.StartOf(new DateOnly(2026, 10, 4), UsageGrain.Week).Should().Be(new DateOnly(2026, 9, 28), because: "일요일은 그 주 월요일로 간다");
        UsageReport.StartOf(Today, UsageGrain.Month).Should().Be(new DateOnly(2026, 10, 1));

        var weeks = UsageReport.Build(Summary((new DateOnly(2026, 10, 5), 1), (new DateOnly(2026, 10, 4), 10)), UsageGrain.Week, Today);
        weeks.Buckets.Should().HaveCount(12);
        weeks.Buckets[^1].Tokens.Input.Should().Be(1);
        weeks.Buckets[^2].Tokens.Input.Should().Be(10);

        var months = UsageReport.Build(Summary((new DateOnly(2025, 11, 30), 4), (new DateOnly(2025, 10, 31), 99)), UsageGrain.Month, Today);
        months.Buckets.Should().HaveCount(12);
        months.Buckets[0].Start.Should().Be(new DateOnly(2025, 11, 1));
        months.Buckets[0].Tokens.Input.Should().Be(4, because: "열두 칸 밖(2025-10)은 버린다");
    }

    [Fact]
    public void Totals_cover_today_seven_and_thirty_days()
    {
        var report = UsageReport.Build(Summary((Today, 1), (Today.AddDays(-6), 10), (Today.AddDays(-7), 100), (Today.AddDays(-29), 1000), (Today.AddDays(-30), 10000)), UsageGrain.Day, Today);

        report.Totals.Today.Total.Should().Be(1);
        report.Totals.Last7Days.Total.Should().Be(11);
        report.Totals.Last30Days.Total.Should().Be(1111);
    }

    [Fact]
    public void Projects_are_the_top_ten_with_labels_that_tell_same_names_apart()
    {
        var projects = Enumerable.Range(1, 12).ToDictionary(i => $@"C:\work\p{i:00}", i => new TokenUsage(i, 0, 0, 0, null));
        projects[@"C:\a\same"] = new TokenUsage(500, 0, 0, 0, null);
        projects[@"C:\b\same"] = new TokenUsage(400, 0, 0, 0, null);
        var summary = new UsageSummary([], projects, new Dictionary<string, TokenUsage> { ["m"] = new(1, 0, 0, 0, "m") });

        var report = UsageReport.Build(summary, UsageGrain.Day, Today);

        report.ByProject.Should().HaveCount(10);
        report.ByProject[0].Key.Should().Be(@"C:\a\same");
        report.ByProject[0].Label.Should().NotBe(report.ByProject[1].Label, because: "폴더 이름이 같으면 상위 폴더까지 붙여 가른다");
        report.ByProject.Sum(row => row.Share).Should().BeLessThan(1, because: "비율은 상위 열 개가 아니라 전체 대비다");
        report.ByModel.Should().ContainSingle().Which.Share.Should().Be(1);
    }

    [Theory]
    [InlineData("?grain=year")]
    [InlineData("?tool=NOT A TOOL")]
    public async Task Bad_arguments_are_refused(string query)
    {
        await using var host = await RunningHost.StartAsync();
        using var client = host.Client();
        using var request = host.Authed(HttpMethod.Get, "/api/usage" + query);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_empty_index_gives_an_empty_but_full_report()
    {
        await using var host = await RunningHost.StartAsync();
        using var client = host.Client();
        using var request = host.Authed(HttpMethod.Get, "/api/usage?grain=week&tool=claude&project=C%3A%5Cnowhere");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = (await response.Content.ReadFromJsonAsync<UsageResponse>())!;
        report.FirstDay.Should().BeNull();
        report.Buckets.Should().HaveCount(12).And.OnlyContain(bucket => bucket.Tokens.Total == 0);
    }

    [Fact]
    public async Task The_index_is_refreshed_when_the_host_starts_and_can_be_refreshed_again()
    {
        await using var host = await RunningHost.StartAsync();
        var index = host.App.Services.GetRequiredService<IndexService>();
        await index.Current.WaitAsync(TimeSpan.FromSeconds(30));

        index.Status.Should().Match<IndexStatus>(status => !status.Running && status.Error == null && status.FinishedAt != null);

        using var client = host.Client();
        using var request = host.Authed(HttpMethod.Post, "/api/index/refresh", host.Url);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await index.Current.WaitAsync(TimeSpan.FromSeconds(30));
        index.Status.Error.Should().BeNull();
    }

    [Fact]
    public async Task A_broken_index_is_reported_instead_of_crashing()
    {
        await using var host = await RunningHost.StartAsync(options =>
        {
            File.WriteAllText(Path.Combine(options.DataDirectory, "index.db"), new string('x', 4096));
            return options;
        });
        var index = host.App.Services.GetRequiredService<IndexService>();
        await index.Current.WaitAsync(TimeSpan.FromSeconds(30));

        index.Status.Error.Should().NotBeNullOrEmpty(because: "화면이 왜 비었는지 말할 수 있어야 한다");
        index.Status.Running.Should().BeFalse();
    }
}
