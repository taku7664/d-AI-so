using Daiso.Core;
using Daiso.Providers.Codex;

namespace Daiso.Providers.Tests.Codex;

public sealed class CodexRateLimitsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "daiso-codex-limits-" + Guid.NewGuid().ToString("N"));

    public CodexRateLimitsTests() => Directory.CreateDirectory(Path.Combine(_root, "2026", "10", "06"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Line(string at, double used, int minutes = 10080, string secondary = "null") =>
        "{\"timestamp\":\"" + at + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"limit_id\":\"codex\","
        + "\"primary\":{\"used_percent\":" + used.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"window_minutes\":" + minutes + ",\"resets_at\":1791593939},"
        + "\"secondary\":" + secondary + ",\"plan_type\":\"prolite\"}}}";

    private string Write(string name, DateTime modified, params string[] lines)
    {
        var path = Path.Combine(_root, "2026", "10", "06", name);
        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, modified);
        return path;
    }

    [Fact]
    public void Reads_the_windows_plan_and_time_of_a_line()
    {
        var snapshot = CodexRateLimits.Parse(Line("2026-10-03T08:40:00.000Z", 12.5, secondary: """{"used_percent":3,"window_minutes":300,"resets_at":1791500000}"""));

        snapshot.Should().NotBeNull();
        snapshot!.At.Should().Be(new DateTimeOffset(2026, 10, 3, 8, 40, 0, TimeSpan.Zero));
        snapshot.Plan.Should().Be("prolite");
        snapshot.Windows.Should().HaveCount(2);
        snapshot.Windows[0].Should().Be(new RateLimitWindow(300, 3, DateTimeOffset.FromUnixTimeSeconds(1791500000)), because: "짧은 창부터");
        snapshot.Windows[1].UsedPercent.Should().Be(12.5);
    }

    [Theory]
    [InlineData("""{"timestamp":"2026-10-03T08:40:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":null}}""")]
    [InlineData("""{"timestamp":"2026-10-03T08:40:00Z","payload":{"rate_limits":{"primary":{"used_percent":"x"}}}}""")]
    [InlineData("""{"type":"event_msg","payload":{"rate_limits":{"primary":{"used_percent":1,"window_minutes":300}}}}""")]
    [InlineData("not json \"rate_limits\"")]
    public void Lines_of_another_shape_give_nothing(string line) =>
        CodexRateLimits.Parse(line).Should().BeNull();

    [Fact]
    public void The_latest_timestamp_wins_even_in_an_older_file()
    {
        // 이어서 연 옛 세션: 수정 시각은 가장 최근이지만 그 안의 한도 줄은 옛것이다
        Write("resumed.jsonl", new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc), Line("2026-09-30T13:50:29.840Z", 58));
        Write("newer.jsonl", new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), Line("2026-10-03T08:00:00Z", 10), Line("2026-10-03T08:40:00Z", 12), "{\"type\":\"other\"}");

        var snapshot = CodexRateLimits.ReadLatest(_root);

        snapshot!.Windows.Single().UsedPercent.Should().Be(12);
    }

    [Fact]
    public void No_sessions_folder_means_no_limits() =>
        CodexRateLimits.ReadLatest(Path.Combine(_root, "missing")).Should().BeNull();
}
