using System.Text.RegularExpressions;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 세션 화면(목록 · 상세 짝)이 지켜야 할 것을 XAML 에서 검사한다.
/// <para>
/// 규칙 하나: <b>지금 할 수 없는 일은 잠그지 말고 감춘다.</b> 잠긴 단추는 눌러야 할 것처럼 보이는데 눌리지 않고,
/// 무엇을 하는 단추인지도 알 수 없다. 목록/상세 짝 화면에서 지우기 같은 파괴 명령은 <b>선택에 딸린 명령</b>이라
/// 고른 것이 있을 때만 나온다 (learn.microsoft.com/windows/apps/design/controls/list-details).
/// 2026-09-11 사람의 지적으로 잠갔다.
/// </para>
/// </summary>
public sealed partial class SessionsPageTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string PagePath =
        Path.Combine(Root, "src", "Daiso.App", "Views", "SessionsPage.xaml");

    [Fact]
    public void 지우기는_고른_것이_있을_때만_나온다()
    {
        var text = File.ReadAllText(PagePath);

        SelectionCommandsHidden().IsMatch(text).Should().BeTrue(
            because: "휴지통·영구 삭제는 선택에 딸린 명령이라 고른 것이 없으면 자리 자체가 없다");

        foreach (var id in new[] { "RecycleButton", "PermanentButton" })
        {
            ButtonEnabledByChecked(text, id).Should().BeFalse(
                because: $"{id} 는 잠그는 것이 아니라 감춘다 — 회색 휴지통은 무엇을 지우는지 말해 주지 않는다");
        }
    }

    [Fact]
    public void 상세_도구_줄은_세션을_골라야_나온다()
    {
        // 아무것도 안 골랐을 때 "이어서 열기"·"Markdown 내보내기"가 잠긴 채로 떠 있으면,
        // 그 아래 "왼쪽에서 세션을 고르면 …" 안내와 같은 말을 두 번 하는 셈이다
        var text = File.ReadAllText(PagePath);

        foreach (var id in new[] { "ResumeButton", "ExportButton" })
        {
            ButtonEnabledBySelection(text, id).Should().BeFalse(
                because: $"{id} 는 IsEnabled 로 잠그지 않는다");
        }

        text.Should().Contain(
            "Visibility=\"{x:Bind ViewModel.HasSelectedSession, Mode=OneWay}\"",
            because: "도구 줄은 고른 세션이 있을 때만 나온다");
    }

    private static bool ButtonEnabledByChecked(string text, string automationId) =>
        Regex.IsMatch(
            text,
            @"AutomationId=""" + Regex.Escape(automationId) + @"""[^>]*IsEnabled=""\{x:Bind ViewModel\.HasChecked",
            RegexOptions.CultureInvariant);

    private static bool ButtonEnabledBySelection(string text, string automationId) =>
        Regex.IsMatch(
            text,
            @"AutomationId=""" + Regex.Escape(automationId) + @"""[^>]*IsEnabled=""\{x:Bind ViewModel\.HasSelectedSession",
            RegexOptions.CultureInvariant);

    [GeneratedRegex(
        @"<\w+(?=[^>]*AutomationId=""SelectionCommands"")(?=[^>]*Visibility=""\{x:Bind ViewModel\.HasChecked)[^>]*>",
        RegexOptions.CultureInvariant)]
    private static partial Regex SelectionCommandsHidden();

    [Fact]
    public void 고르면_머리가_선택_명령_줄로_바뀐다()
    {
        // 제목·건수·고르기·휴지통 둘을 한 줄에 다 넣으면 목록 칸(최소 220)에서 휴지통이 잘린다.
        // 평소 얼굴은 HasNoChecked, 선택 명령 줄은 HasChecked — 둘이 자리를 나눠 쓰지 않고 갈아탄다
        var text = File.ReadAllText(PagePath);

        text.Should().Contain(
            "Visibility=\"{x:Bind ViewModel.HasNoChecked, Mode=OneWay}\"",
            because: "고른 것이 있으면 제목·건수 자리를 선택 명령 줄에 내준다");
        text.Should().Contain("ClearChecksButton", because: "선택 명령 줄에서 빠져나올 길이 있어야 한다");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Daiso.sln 을 찾지 못했다");
    }
}
