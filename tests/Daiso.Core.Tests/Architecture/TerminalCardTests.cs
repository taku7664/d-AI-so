using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 새 터미널 카드가 지켜야 할 것을 XAML 에서 검사한다 (docs/TERMINAL_CARD_PLAN.md §6 Stage 7).
/// <para>
/// 이 카드의 대상은 <b>AI CLI 를 처음 써 보는 일반인</b>이다. 그 사람에게 <c>--sandbox</c> 는 글자일 뿐이라
/// 화면 문구에 날 플래그를 내놓지 않는다. 플래그를 아는 사람은 `옵션 인자` 칸에 직접 적는다.
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
        // 문구 값에 "--foo" 가 있으면 그 말이 그대로 화면에 나온다
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
            because: "화면 문구는 사람 말이어야 한다. 플래그를 쓸 사람은 옵션 인자 칸에 직접 적는다");
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
    public void 세부_설정은_필수_세_단계를_다_답해야_열린다()
    {
        // 2026-09-10: 3단계가 "펼쳐지기만" 하면 열던 때는, 이어서를 고르고 대화를 안 골랐는데도
        // 모델·인자가 먼저 나와 무엇이 남았는지 흐려졌다. AdvancedExpanded 는 SessionDone 을 본다
        var text = File.ReadAllText(PagePath);

        AdvancedOpensWhenRequiredDone().IsMatch(text).Should().BeTrue(
            because: "(선택) 세부 설정은 필수 1~3 단계에 답이 다 채워진 뒤에 열린다");
        text.Should().NotContain("muxc:Expander", because: "세부 설정은 접는 상자가 아니라 단계다");
    }

    [Theory]
    [InlineData("Terminal_StepTool", "(필수) ")]
    [InlineData("Terminal_WorkFolder", "(필수) ")]
    [InlineData("Terminal_Session", "(필수) ")]
    [InlineData("Terminal_Advanced", "(선택) ")]
    public void 단계_머리말은_필수인지_선택인지_밝힌다(string key, string prefix)
    {
        var value = XDocument.Parse(File.ReadAllText(StringsPath)).Root!
            .Elements("data")
            .Single(data => (string?)data.Attribute("name") == key)
            .Element("value")!.Value;

        value.Should().StartWith(prefix);
    }

    [Fact]
    public void 규칙_편집은_세부_설정_단계에_있다()
    {
        var text = File.ReadAllText(PagePath);
        var advanced = text.IndexOf("x:Name=\"StepAdvancedHeader\"", StringComparison.Ordinal);
        var rules = text.IndexOf("AutomationProperties.AutomationId=\"EditRulesButton\"", StringComparison.Ordinal);

        advanced.Should().BePositive();
        rules.Should().BeGreaterThan(advanced, because: "규칙 편집은 폴더 단계가 아니라 (선택) 세부 설정 안에 둔다");
    }

    [Fact]
    public void 프롬프트는_세부_설정_단계에_있다()
    {
        var text = File.ReadAllText(PagePath);
        var advanced = text.IndexOf("x:Name=\"StepAdvancedHeader\"", StringComparison.Ordinal);
        var prompt = text.IndexOf("AutomationProperties.AutomationId=\"StartPromptBox\"", StringComparison.Ordinal);

        advanced.Should().BePositive();
        prompt.Should().BeGreaterThan(advanced, because: "프롬프트는 골라도 되고 안 골라도 되는 것이라 (선택) 세부 설정 안에 둔다");
    }

    [Fact]
    public void 프롬프트는_새로_시작이든_이어서든_보인다()
    {
        // 2026-09-10: 새로 시작에만 걸어 뒀더니 이어서를 고르면 칸이 말없이 사라졌다.
        // 세 도구 다 이어서에 첫 메시지를 붙일 수 있다 — claude --resume ID "..." · codex resume ID "..." · agy --conversation ID -i "..."
        var text = File.ReadAllText(PagePath);

        PromptAlwaysVisible().IsMatch(text).Should().BeTrue(
            because: "고를 수 있는 칸을 조건부로 숨기면 어디 갔는지 찾게 된다");
    }

    [GeneratedRegex(
        @"<ComboBox(?=[^>]*AutomationId=""StartPromptBox"")(?![^>]*Visibility=)[^>]*>",
        RegexOptions.CultureInvariant)]
    private static partial Regex PromptAlwaysVisible();

    [Fact]
    public void 무효화_안내문은_두지_않는다()
    {
        // 앞 단계를 바꿔 답이 풀리면 3단계가 다시 비어 열린다. 그것으로 이미 보이는데
        // "다시 골라야 해요" 한 줄을 더 얹으면 화면이 사람을 훈계한다 (2026-09-10 사람의 지적)
        var text = File.ReadAllText(PagePath);

        text.Should().NotContain("InvalidationNotice", because: "빈 단계가 스스로 말한다");
    }

    [Fact]
    public void 세부_설정은_라벨_칸을_맞춘_한_격자다()
    {
        // Header 와 회색 설명줄이 번갈아 쌓이면 어디까지가 한 항목인지 보이지 않는다.
        // 1~3 단계의 "대화" 줄과 같은 72 폭 라벨 칸을 쓴다
        var text = File.ReadAllText(PagePath);
        var body = text[text.IndexOf("AutomationId=\"StepAdvancedBody\"", StringComparison.Ordinal)..];

        foreach (var key in new[] { "Terminal_RulesLabel", "Terminal_PromptLabel", "Terminal_ModelHeader", "Terminal_OptionArgs" })
        {
            body.Should().Contain($"Text=\"{{loc:Str Key={key}}}\"", because: "네 줄 다 같은 라벨 칸에서 시작한다");
        }

        body.Should().NotContain("Header=\"{loc:Str", because: "라벨은 칸 위가 아니라 왼쪽 칸에 둔다 — 한 세로선을 따라 읽힌다");
    }

    [Fact]
    public void 옵션_인자_아래에_추천_단추를_두지_않는다()
    {
        // 2026-09-10 사람의 요청. 적을 사람은 적고, 모르는 사람은 비워 둔다.
        // 잘린 라벨 두 개가 칸 밑에 붙어 있는 것이 도움이 된 적이 없다
        var text = File.ReadAllText(PagePath);

        text.Should().NotContain("ArgumentPreset", because: "프리셋 표는 걷어냈다");
        text.Should().NotContain("OnPresetClick", because: "누를 단추가 없다");
    }

    [Fact]
    public void 새_터미널_단추는_탭_높이의_정사각형이다()
    {
        var text = File.ReadAllText(PagePath);

        NewTabSquare().IsMatch(text).Should().BeTrue(because: "+ 단추가 방 탭(높이 40)과 같은 줄에서 혼자 작으면 어긋나 보인다");
        text.Should().Contain("<Setter Property=\"Height\" Value=\"40\" />", because: "방 탭 높이도 40 으로 고정해야 둘이 맞는다");
    }

    [GeneratedRegex(@"<ToggleButton(?=[^>]*x:Name=""NewTab"")(?=[^>]*Width=""40"")(?=[^>]*Height=""40"")[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex NewTabSquare();

    [Fact]
    public void 이어서_할_대화는_콤보박스로_고른다()
    {
        var text = File.ReadAllText(PagePath);

        text.Should().Contain("AutomationProperties.AutomationId=\"ResumeSessionBox\"", because: "폴더 칸처럼 콤보박스 하나로 고른다");
        text.Should().NotContain("ResumeSessionList", because: "목록은 쌓인 단계 사이에서 카드 높이를 먹는다");
    }

    [Fact]
    public void 새로_시작도_같은_콤보에서_고른다()
    {
        // 2026-09-10: 카드 2장(새로 시작 / 하던 대화 이어서)으로 갈래를 먼저 묻고 그 아래에 대화 칸이 또 있었다.
        // 지난 대화를 고르려면 두 번 눌러야 했고, 카드와 콤보가 같은 것을 두 번 물었다.
        // 이제 목록 첫 줄이 "새로 시작"이다 (ResumeCandidateViewModel.NewSession)
        var text = File.ReadAllText(PagePath);

        text.Should().NotContain("NewSessionRadio", because: "새로 시작은 카드가 아니라 목록의 첫 줄이다");
        text.Should().NotContain("ResumeSessionRadio", because: "이어서도 카드가 아니라 목록의 나머지 줄이다");
        text.Should().Contain("ViewModel.SessionChoices", because: "새로 시작 + 지난 대화가 한 목록이다");
    }

    [Fact]
    public void 지난_대화_한_줄은_콤보_칸을_넘지_않는다()
    {
        // 펼친 목록은 무한 폭으로 재므로 TextTrimming 만으로는 안 줄어든다. 제목에 MaxWidth 가 있어야
        // 긴 첫 프롬프트 하나가 팝업을 카드 밖으로 밀어내지 않는다 (2026-09-10 사람의 지적)
        var templates = File.ReadAllText(Path.Combine(Root, "src", "Daiso.App", "Ui", "ItemTemplates.xaml"));
        var template = templates[templates.IndexOf("ResumeCandidateTemplate", StringComparison.Ordinal)..];

        SummaryHasMaxWidth().IsMatch(template).Should().BeTrue(
            because: "제목이 길어도 목록이 콤보 칸 밖으로 나가지 않아야 한다");
    }

    [GeneratedRegex(
        @"<TextBlock(?=[^>]*x:Bind Summary)(?=[^>]*MaxWidth=)(?=[^>]*TextTrimming=""CharacterEllipsis"")[^>]*>",
        RegexOptions.CultureInvariant)]
    private static partial Regex SummaryHasMaxWidth();

    [GeneratedRegex(@"<Button[^>]*Style=""\{StaticResource StepHeader\}""", RegexOptions.CultureInvariant)]
    private static partial Regex StepHeaderButton();

    [GeneratedRegex(
        @"<StackPanel(?=[^>]*AutomationId=""StepAdvancedBody"")(?=[^>]*Visibility=""\{x:Bind ViewModel\.AdvancedExpanded)[^>]*>",
        RegexOptions.CultureInvariant)]
    private static partial Regex AdvancedOpensWhenRequiredDone();

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
