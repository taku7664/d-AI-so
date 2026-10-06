using Daiso.Host.Shared;

namespace Daiso.Host.Tests;

/// <summary>워크트리·임시 폴더를 원래 프로젝트로 묶는 규칙. 경로 모양은 2026-10-06 사용자 인덱스에서 본 그대로다.</summary>
public sealed class ProjectGroupsTests
{
    private const string Repo = @"C:\Users\u\Documents\GitHub\Cobblemon-Mods";
    private const string Engine = @"C:\Users\u\Documents\GitHub\JBroEngine";

    [Fact]
    public void A_claude_worktree_belongs_to_its_repository()
    {
        var map = ProjectGroups.Map([Engine, Engine + @"\.claude\worktrees\widget-input-spin-buttons-8106ae"]);

        map[Engine + @"\.claude\worktrees\widget-input-spin-buttons-8106ae"].Should().Be(Engine);
    }

    [Fact]
    public void A_codex_worktree_belongs_to_the_one_project_with_the_same_folder_name()
    {
        var map = ProjectGroups.Map([Repo, @"C:\Users\u\.codex\worktrees\d249\Cobblemon-Mods", @"C:\Users\u\.codex\worktrees\b396\Cobblemon-Mods\"]);

        map[@"C:\Users\u\.codex\worktrees\d249\Cobblemon-Mods"].Should().Be(Repo);
        map[@"C:\Users\u\.codex\worktrees\b396\Cobblemon-Mods"].Should().Be(Repo, because: "끝의 \\ 는 떼고 본다");
    }

    [Fact]
    public void A_codex_worktree_stays_alone_when_the_name_is_ambiguous_or_unknown()
    {
        var map = ProjectGroups.Map([Repo, @"D:\other\Cobblemon-Mods", @"C:\Users\u\.codex\worktrees\d249\Cobblemon-Mods", @"C:\Users\u\.codex\worktrees\aaaa\Nowhere"]);

        map[@"C:\Users\u\.codex\worktrees\d249\Cobblemon-Mods"].Should().Be(@"C:\Users\u\.codex\worktrees\d249\Cobblemon-Mods", because: "같은 이름이 둘이면 어느 쪽인지 모른다");
        map[@"C:\Users\u\.codex\worktrees\aaaa\Nowhere"].Should().Be(@"C:\Users\u\.codex\worktrees\aaaa\Nowhere");
    }

    [Fact]
    public void Claude_desktop_scratch_folders_become_one_project()
    {
        const string root = @"C:\Users\u\AppData\Roaming\Claude\scratch-workspaces";
        var map = ProjectGroups.Map([root + @"\5f7a\83d5\scratch-2026-10-05-22a976", root + @"\5f7a\1111\scratch-2026-09-25-59b774"]);

        map.Values.Should().AllBe(root);
        ProjectGroups.LabelFor(root).Should().Be(ProjectGroups.ScratchLabel);
        ProjectGroups.LabelFor(Repo).Should().BeNull();
    }

    [Fact]
    public void Ordinary_projects_stay_themselves() =>
        ProjectGroups.Map([Repo, Engine]).Should().BeEquivalentTo(new Dictionary<string, string> { [Repo] = Repo, [Engine] = Engine });
}
