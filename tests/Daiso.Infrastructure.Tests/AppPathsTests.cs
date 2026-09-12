using Daiso.Providers.Tests;

namespace Daiso.Infrastructure.Tests;

/// <summary>
/// 이름을 바꾸면서 쓰던 것을 옮겨 온다. (2026-09-12)
///
/// <para>
/// 새 폴더만 만들고 끝내면 인덱스·설정·계정 보관함이 옛 폴더에 남는데,
/// 사람이 보기에는 쓰던 것이 그냥 사라진 것이다.
/// </para>
/// </summary>
public sealed class AppPathsTests
{
    private static string Legacy(string local) => Path.Combine(local, "d-AI-so");

    private static string Current(string local) => Path.Combine(local, "DAIso");

    [Fact]
    public void A_fresh_machine_just_uses_the_new_folder()
    {
        var local = Fixtures.CreateTempDirectory();

        AppPaths.Resolve(local).Should().Be(Current(local));
    }

    [Fact]
    public void What_was_there_moves_over_with_everything_in_it()
    {
        var local = Fixtures.CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(Legacy(local), "profiles", "codex"));
        File.WriteAllText(Path.Combine(Legacy(local), "index.db"), "index");
        File.WriteAllText(Path.Combine(Legacy(local), "profiles", "codex", "meta.json"), "{}");

        var root = AppPaths.Resolve(local);

        root.Should().Be(Current(local));
        File.Exists(Path.Combine(root, "index.db")).Should().BeTrue(because: "인덱스가 따라와야 한다");
        File.Exists(Path.Combine(root, "profiles", "codex", "meta.json")).Should().BeTrue(because: "보관한 계정도 따라와야 한다");
        Directory.Exists(Legacy(local)).Should().BeFalse(because: "옮긴 뒤에는 옛 폴더가 남지 않는다");
    }

    [Fact]
    public void Already_moved_is_left_alone()
    {
        var local = Fixtures.CreateTempDirectory();
        Directory.CreateDirectory(Current(local));
        File.WriteAllText(Path.Combine(Current(local), "settings.json"), "new");
        Directory.CreateDirectory(Legacy(local));
        File.WriteAllText(Path.Combine(Legacy(local), "settings.json"), "old");

        var root = AppPaths.Resolve(local);

        root.Should().Be(Current(local));
        File.ReadAllText(Path.Combine(root, "settings.json")).Should().Be("new", because: "쓰던 새 폴더를 옛것으로 덮지 않는다");
    }

    /// <summary>새 폴더만 덩그러니 생겨 있고 옛것에 내용이 있으면, 앞선 이사가 덜 끝난 것이다.</summary>
    [Fact]
    public void An_empty_new_folder_does_not_swallow_the_old_one()
    {
        var local = Fixtures.CreateTempDirectory();
        Directory.CreateDirectory(Current(local));
        Directory.CreateDirectory(Path.Combine(Legacy(local), "prompts"));
        File.WriteAllText(Path.Combine(Legacy(local), "index.db"), "index");

        var root = AppPaths.Resolve(local);

        root.Should().Be(Current(local));
        File.Exists(Path.Combine(root, "index.db")).Should().BeTrue();
        Directory.Exists(Path.Combine(root, "prompts")).Should().BeTrue();
    }
}
