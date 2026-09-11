namespace Daiso.Core.Tests;

/// <summary>
/// 목록에 나란히 놓을 이름. 겹칠 때만 상위 폴더를 하나 붙인다.
/// 화면 층에 있어 테스트가 없던 규칙을 Core 로 내리고 여기서 잠근다 (2026-09-11 점검).
/// </summary>
public sealed class PathLabelsTests
{
    private const string Unknown = "(알 수 없음)";

    [Fact]
    public void Names_that_do_not_collide_stay_bare()
    {
        var labels = PathLabels.For([@"C:\work\alpha", @"C:\work\beta"], Unknown);

        labels.Should().Equal("alpha", "beta");
    }

    [Fact]
    public void Colliding_names_get_the_first_differing_parent()
    {
        var labels = PathLabels.For([@"C:\work\as-r\TASK-1", @"C:\work\as-m1\TASK-1"], Unknown);

        labels.Should().Equal("as-r / TASK-1", "as-m1 / TASK-1");
    }

    [Fact]
    public void The_parent_that_is_also_the_same_is_skipped()
    {
        // 바로 위(sub)가 같으므로 한 단계 더 올라가 갈리는 곳(a·b)을 붙인다
        var labels = PathLabels.For([@"C:\a\sub\TASK", @"C:\b\sub\TASK"], Unknown);

        labels.Should().Equal("a / TASK", "b / TASK");
    }

    [Fact]
    public void Paths_that_never_differ_keep_just_the_name()
    {
        // 대소문자만 다른 같은 경로. 붙일 상위가 없으니 이름만 둔다 — 목록을 경로로 덮지 않는다
        var labels = PathLabels.For([@"C:\work\TASK", @"c:\work\task"], Unknown);

        labels.Should().OnlyContain(label => !label.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_colliding_ones_are_touched()
    {
        var labels = PathLabels.For([@"C:\x\TASK", @"C:\y\TASK", @"C:\z\alone"], Unknown);

        labels[2].Should().Be("alone");
        labels[0].Should().Contain("/");
    }

    [Theory]
    [InlineData(@"C:\work\project", "project")]
    [InlineData(@"C:\work\project\", "project")]
    [InlineData("C:/work/project", "project")]
    [InlineData(@"C:\", @"C:")]
    public void A_folder_name_is_the_last_segment(string path, string expected)
    {
        PathLabels.FolderName(path, Unknown).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_path_means_the_unknown_name(string? path)
    {
        PathLabels.FolderName(path, Unknown).Should().Be(Unknown);
    }

    [Fact]
    public void An_empty_list_comes_back_empty()
    {
        PathLabels.For([], Unknown).Should().BeEmpty();
    }
}
