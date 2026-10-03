using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Common;

/// <summary>ARCHITECTURE §4.3 경로 정규화.</summary>
public sealed class ProjectPathNormalizerTests
{
    [Fact]
    public void A_trailing_separator_and_a_lowercase_drive_normalize_to_the_same_value()
    {
        ProjectPathNormalizer.Normalize(@"c:\a\b\").Should().Be(@"C:\a\b");
        ProjectPathNormalizer.Normalize(@"C:\a\b").Should().Be(@"C:\a\b");
        ProjectPathNormalizer.AreSame(@"c:\a\b\", @"C:\a\b").Should().BeTrue();
    }

    [Fact]
    public void Forward_slashes_become_backslashes()
    {
        ProjectPathNormalizer.Normalize("C:/a/b").Should().Be(@"C:\a\b");
    }

    [Fact]
    public void Dot_segments_are_resolved()
    {
        ProjectPathNormalizer.Normalize(@"C:\a\.\b\..\b").Should().Be(@"C:\a\b");
    }

    [Fact]
    public void A_drive_root_keeps_its_separator()
    {
        ProjectPathNormalizer.Normalize(@"c:\").Should().Be(@"C:\");
    }

    [Fact]
    public void Casing_is_restored_from_disk_for_the_part_that_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "daiso-case-" + Guid.NewGuid().ToString("N"));
        var real = Path.Combine(root, "MyProj", "Sub");
        Directory.CreateDirectory(real);

        try
        {
            var lower = Path.Combine(root, "myproj", "sub");
            ProjectPathNormalizer.RestoreCasing(lower).Should().Be(ProjectPathNormalizer.Normalize(real));

            // 없는 꼬리는 받은 그대로
            ProjectPathNormalizer.RestoreCasing(Path.Combine(lower, "Missing"))
                .Should().Be(Path.Combine(ProjectPathNormalizer.Normalize(real)!, "Missing"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Restoring_casing_of_nothing_is_nothing()
    {
        ProjectPathNormalizer.RestoreCasing(null).Should().BeNull();
        ProjectPathNormalizer.RestoreCasing(@"c:\").Should().Be(@"C:\");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_normalizes_to_null(string? path)
    {
        ProjectPathNormalizer.Normalize(path).Should().BeNull();
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        ProjectPathNormalizer.Normalize(@"  C:\a\b  ").Should().Be(@"C:\a\b");
    }

    [Fact]
    public void Different_paths_are_not_the_same()
    {
        ProjectPathNormalizer.AreSame(@"C:\a\b", @"C:\a\c").Should().BeFalse();
    }
}
