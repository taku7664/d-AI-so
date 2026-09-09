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

    [Fact]
    public void 세션_목록의_행_높이가_고정이다()
    {
        // 높이가 유동이면 목록 끝에서 행이 반쯤 잘려 "더 있는지 끝인지" 알 수 없다 (§2-C4)
        var text = File.ReadAllText(PagePath);

        text.Should().Contain(
            "<Setter Property=\"Height\" Value=\"40\" />",
            because: "행 높이를 고정해야 목록 높이를 그 정수배로 맞출 수 있다");
    }

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
