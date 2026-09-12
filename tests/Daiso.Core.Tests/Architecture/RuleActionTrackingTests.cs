namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 규칙의 <b>행동 줄</b>은 뷰모델이 더하고 뺀다.
///
/// <para>
/// 행동 줄이 미리보기와 저장 단추에 닿으려면 뷰모델의 <c>Track</c> 이 <c>PropertyChanged</c> 를 걸어 줘야 한다.
/// 화면이 <c>rule.Actions.Add(new ActionEditViewModel())</c> 로 직접 넣던 때는 그 줄에 친 글이
/// 미리보기에도 <c>CanSave</c> 에도 닿지 않았다 — 조건만 있는 규칙은 저장 단추가 잠긴 채였다 (2026-09-12 점검).
/// </para>
///
/// <para>
/// 그리고 <c>Track</c> 에는 짝이 있어야 한다. 이 뷰모델은 하나뿐이라, 떼지 않으면 프리셋을 갈아 끼울 때마다
/// 행동 줄이 앱이 끝날 때까지 쌓인다.
/// </para>
/// </summary>
public sealed class RuleActionTrackingTests
{
    private static readonly string AppRoot = Path.Combine(FindRepositoryRoot(), "src", "Daiso.App");

    private static string RuleMakerViewModel =>
        File.ReadAllText(Path.Combine(AppRoot, "ViewModels", "RuleMakerViewModel.cs"));

    [Fact]
    public void A_page_does_not_add_or_remove_rule_actions_by_itself()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(AppRoot, "Views"), "*.xaml.cs")
            .Where(path => File.ReadAllText(path) is var text
                && (text.Contains("Actions.Add(", StringComparison.Ordinal)
                    || text.Contains("Actions.Remove(", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            because: "화면이 직접 넣은 행동 줄은 지켜보기가 걸리지 않아, 친 글이 미리보기에도 저장 단추에도 닿지 않는다");
    }

    [Fact]
    public void Tracking_an_action_has_a_matching_release()
    {
        var text = RuleMakerViewModel;

        text.Should().Contain("action.PropertyChanged += OnChildChanged",
            because: "행동 줄이 바뀌면 미리보기가 다시 그려져야 한다");
        text.Should().Contain("action.PropertyChanged -= OnChildChanged",
            because: "떼지 않으면 프리셋을 갈아 끼울 때마다 행동 줄이 그대로 쌓인다");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("저장소 뿌리를 찾지 못했다");
    }
}
