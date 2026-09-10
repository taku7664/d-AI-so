using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 새 터미널 카드가 지켜야 할 것을 XAML 에서 검사한다 (docs/TERMINAL_CARD_PLAN.md §6 Stage 7).
/// <para>
/// 이 카드의 대상은 <b>AI CLI 를 처음 써 보는 일반인</b>이다. 그 사람에게 <c>--sandbox</c> 는 글자일 뿐이라
/// 기본 화면에 날 플래그를 내놓지 않는다. 플래그는 `자세한 설정` 안에서 한국어 라벨 뒤에만 붙는다.
/// </para>
/// </summary>
public sealed partial class TerminalCardTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string PagePath = Path.Combine(Root, "src", "Daiso.App", "Views", "TerminalPage.xaml");

    private static readonly string StringsPath =
        Path.Combine(Root, "src", "Daiso.App", "Strings", "ko-KR", "Resources.resw");

    [Fact]
    public void 화면_문구에_날_플래그가_없다()
    {
        // 문구 값에 "--foo" 가 있으면 그 말이 그대로 화면에 나온다.
        // 프리셋의 플래그는 문구가 아니라 코드(ArgumentPreset.Flag)로 들고 있어야 한다
        var text = File.ReadAllText(StringsPath);
        var document = XDocument.Parse(text);

        var offenders = document.Root!
            .Elements("data")
            .Select(data => (Key: (string?)data.Attribute("name") ?? string.Empty, Value: data.Element("value")?.Value ?? string.Empty))
            .Where(entry => FlagPattern().IsMatch(entry.Value))
            .Select(entry => $"{entry.Key} = {entry.Value}")
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            because: "화면 문구는 사람 말이어야 한다. 플래그는 ArgumentPreset.Flag 로 코드에서 붙인다");
    }

    [Theory]
    [InlineData("예: --continue", true)]
    [InlineData("파일 수정 권한을 --permission-mode 로 바꿉니다", true)]
    [InlineData("잘 모르면 비워 두세요", false)]
    [InlineData("npm install -g codex", false)]
    [InlineData("2026-09-09 — 오늘", false)]
    public void 플래그_검사가_어긴_예를_잡는다(string value, bool shouldFlag)
    {
        // 검사기가 실제로 무엇을 잡는지 여기서 못박는다. 통과만 하는 검사는 검사가 아니다
        FlagPattern().IsMatch(value).Should().Be(shouldFlag);
    }

    [Fact]
    public void 카드에_번호_배지가_없다()
    {
        // ①②③④ 는 위저드 기호인데 이 카드는 위저드가 아니다. 상태 표시(끝남·지금·아직)로 대신한다
        var text = File.ReadAllText(PagePath);

        text.Should().NotContain(
            "StepNumber",
            because: "번호 배지는 걷어냈다. 단계 표시는 StepHeader 의 체크·점·빈 원이다");
    }

    [Fact]
    public void 실행_줄이_사람_말과_명령을_모두_보여준다()
    {
        var text = File.ReadAllText(PagePath);

        text.Should().Contain("ViewModel.PreviewSentence", because: "무엇이 실행되는지 사람 말로 먼저 말한다");
        text.Should().Contain("ViewModel.Preview,", because: "실제 명령도 그대로 보여 준다");
        text.Should().Contain("OnCopyPreviewClick", because: "명령이 잘려도 전문을 가져갈 수 있어야 한다");
    }

    // ── 2026-09-10 사람이 정한 단계 흐름. 여러 번 어긋나게 만들었던 것이라 여기서 잠근다 ──────────

    [Fact]
    public void 단계_머리는_누르는_곳이_아니다()
    {
        // 단계는 답을 따라서만 열리고 닫히지 않는다. 사람이 머리를 눌러 접고 펴게 두면 답한 단계가 사라져 흐름을 잃는다
        var text = File.ReadAllText(PagePath);

        text.Should().NotContain("Click=\"OnStep", because: "단계 머리에 클릭 처리기를 두지 않는다");
        StepHeaderButton().IsMatch(text).Should().BeFalse(because: "머리가 버튼이면 눌러질 것처럼 보인다");
    }

    [Fact]
    public void 자세한_설정은_3단계와_함께_열린다()
    {
        var text = File.ReadAllText(PagePath);

        AdvancedOpensWithSession().IsMatch(text).Should().BeTrue(
            because: "자세한 설정은 3단계가 열릴 때 같이 보인다. 바닥 띠에 늘 붙어 있거나 따로 놀면 안 된다");
    }

    [Fact]
    public void 이어서_할_대화는_콤보박스로_고른다()
    {
        var text = File.ReadAllText(PagePath);

        text.Should().Contain("AutomationProperties.AutomationId=\"ResumeSessionBox\"", because: "폴더 칸처럼 콤보박스 하나로 고른다");
        text.Should().NotContain("ResumeSessionList", because: "목록은 쌓인 단계 사이에서 카드 높이를 먹는다");
    }

    [GeneratedRegex(@"<Button[^>]*Style=""\{StaticResource StepHeader\}""", RegexOptions.CultureInvariant)]
    private static partial Regex StepHeaderButton();

    [GeneratedRegex(
        @"<muxc:Expander(?=[^>]*AutomationId=""AdvancedExpander"")(?=[^>]*Visibility=""\{x:Bind ViewModel\.SessionExpanded)[^>]*>",
        RegexOptions.CultureInvariant)]
    private static partial Regex AdvancedOpensWithSession();

    private static readonly string ViewModelPath =
        Path.Combine(Root, "src", "Daiso.App", "ViewModels", "TerminalViewModel.cs");

    [Fact]
    public void 프리셋_플래그는_값까지_담는다()
    {
        // "--permission-mode " 처럼 플래그만 붙이면 사람이 값을 몰라 그대로 열고, CLI 는 "argument missing" 으로 뜨지도 않는다
        // (2026-09-10 실제로 그랬다). 값 없는 스위치(agy 의 --sandbox)는 끝에 빈칸이 없어 걸리지 않는다
        var offenders = PresetPattern().Matches(File.ReadAllText(ViewModelPath))
            .Select(match => match.Groups["flag"].Value)
            .Where(EndsWithoutValue)
            .ToList();

        offenders.Should().BeEmpty(because: "값이 필요한 플래그는 프리셋에 값까지 넣는다. 값을 사람에게 맡기면 도구가 안 뜬다");
    }

    [Theory]
    [InlineData("--permission-mode ", true)]
    [InlineData("--sandbox ", true)]
    [InlineData("--permission-mode plan", false)]
    [InlineData("--sandbox", false)]
    public void 값_없는_프리셋_검사가_어긴_예를_잡는다(string flag, bool shouldFlag)
    {
        EndsWithoutValue(flag).Should().Be(shouldFlag);
    }

    private static bool EndsWithoutValue(string flag) => flag.EndsWith(' ');

    [GeneratedRegex(@"new\(""Terminal_Preset\w+"",\s*""(?<flag>[^""]*)""\)", RegexOptions.CultureInvariant)]
    private static partial Regex PresetPattern();

    [GeneratedRegex(@"(?<![\w-])--[a-z][a-z-]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex FlagPattern();

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
