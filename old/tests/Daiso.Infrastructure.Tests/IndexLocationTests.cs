using Daiso.Providers.Tests;

namespace Daiso.Infrastructure.Tests;

/// <summary>
/// 인덱스 경로를 못 쓸 때 <b>앱이 죽지 않고</b> 기본 경로로 물러서는지. (2026-09-11 회귀)
///
/// <para>
/// 예전에는 설정에 없는 드라이브를 적으면 DI 를 짜는 도중에 던져 창이 뜨기 전에 앱이 끝났다.
/// 설정 화면을 열 수 없으니 손으로 <c>settings.json</c> 을 고쳐야만 살아났다.
/// </para>
/// </summary>
public sealed class IndexLocationTests
{
    /// <summary>이 PC 에 없는 드라이브. 경로 꼴은 맞지만 열 수 없다.</summary>
    private static string MissingDrivePath
    {
        get
        {
            var used = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
            var free = "ZYXWVU".First(letter => !used.Contains(letter));

            return $"{free}:\\daiso-none\\index.db";
        }
    }

    [Fact]
    public void Blank_means_the_default_path()
    {
        var location = IndexLocation.Resolve(null);

        location.Path.Should().Be(SqliteSessionIndex.DefaultDatabasePath);
        location.Failure.Should().BeNull();
    }

    [Fact]
    public void A_usable_path_is_used_as_written()
    {
        var directory = Fixtures.CreateTempDirectory();
        var wanted = Path.Combine(directory, "moved.db");

        var location = IndexLocation.Resolve(wanted);

        location.Path.Should().Be(wanted);
        location.Failure.Should().BeNull();
    }

    [Fact]
    public void An_unusable_path_falls_back_to_the_default_and_says_why()
    {
        var location = IndexLocation.Resolve(MissingDrivePath);

        location.Path.Should().Be(SqliteSessionIndex.DefaultDatabasePath,
            because: "여기서 던지면 창이 뜨기 전에 앱이 죽고, 설정 화면으로 고칠 길이 없다");
        location.Failure.Should().NotBeNullOrWhiteSpace(because: "조용히 다른 곳을 쓰면 인덱스가 왜 비었는지 알 수 없다");
        location.Requested.Should().NotBeNull();
    }

    [Fact]
    public void Probe_says_null_when_the_path_can_be_opened()
    {
        var directory = Fixtures.CreateTempDirectory();

        IndexLocation.Probe(Path.Combine(directory, "probe.db")).Should().BeNull();
        IndexLocation.Probe(MissingDrivePath).Should().NotBeNullOrWhiteSpace();
    }
}
