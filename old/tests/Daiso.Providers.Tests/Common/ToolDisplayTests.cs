using Daiso.Core;
using Daiso.Providers.Antigravity;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Common;

/// <summary>
/// 도구마다 화면에 내놓을 것이 다 있는가 (docs/PLUGIN_PLAN.md Stage 2).
/// <para>
/// 예전에는 <c>ToolLook.cs</c> 의 <b>소스 글자</b>를 훑어 <c>switch</c> 에 도구가 빠졌는지 봤다.
/// 이제 표시 규칙은 도구가 들고 있으므로 <b>값을 직접 본다</b> — 검사할 것이 글자가 아니라 동작이다.
/// </para>
/// <para>
/// 빠뜨리면 화면에서 회색 원 + 물음표로 <b>조용히</b> 뜬다. 그래서 여기서 잡는다.
/// </para>
/// </summary>
public sealed class ToolDisplayTests
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "daiso-display-probe");

    /// <summary>앱에 묻어 있는 도구 전부. 도구를 더하면 여기도 한 줄 는다.</summary>
    public static TheoryData<string, IProvider> BuiltIns()
    {
        var home = new ProviderHome(Home);

        return new TheoryData<string, IProvider>
        {
            { "claude", new ClaudeProvider(home, new FakeProcessProbe()) },
            { "codex", new CodexProvider(home) },
            { "antigravity", new AntigravityProvider(home) },
        };
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Every_tool_answers_every_display_question(string id, IProvider provider)
    {
        provider.Kind.Id.Should().Be(id);

        var display = provider.Display;

        display.Title.Should().NotBeNullOrWhiteSpace(because: "카드 제목이 빈칸이면 무엇을 고르는지 알 수 없다");
        display.Vendor.Should().NotBeNullOrWhiteSpace(because: "아는 회사 이름이 붙어야 처음 보는 사람이 감을 잡는다");
        display.Short.Should().NotBeNullOrWhiteSpace(because: "버튼·미리보기가 이 이름을 쓴다");
        display.Initial.Should().HaveLength(1, because: "동그란 배지에 한 글자만 들어간다");
        display.LogoPath.Should().NotBeNullOrWhiteSpace(because: "로고가 없으면 기본 동그라미로 떨어진다");
        display.ColorStops.Should().NotBeEmpty(because: "색이 없으면 배지가 회색이 된다");
    }

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void Colours_are_hex(string id, IProvider provider)
    {
        _ = id;

        foreach (var stop in provider.Display.ColorStops)
        {
            stop.Should().MatchRegex("^#[0-9A-Fa-f]{6}$", because: "꼴이 틀린 색은 화면에서 말없이 회색이 된다");
        }
    }

    [Fact]
    public void Built_in_tools_do_not_share_a_display_order()
    {
        var orders = BuiltIns().Select(row => ((IProvider)row[1]).Display.Order).ToList();

        orders.Should().OnlyHaveUniqueItems(
            because: "탭 인덱스가 표시 순서로 계산된다. 같은 자리를 둘이 쓰면 탭과 도구가 어긋난다");
    }

    [Fact]
    public void Built_in_tools_come_before_plugins()
    {
        var orders = BuiltIns().Select(row => ((IProvider)row[1]).Display.Order).ToList();

        orders.Should().OnlyContain(
            order => order < ToolDisplay.DefaultOrder,
            because: "플러그인이 끼어들어 내장 도구 순서를 흔들면 안 된다");
    }

    [Fact]
    public void The_built_in_list_matches_the_tool_ids()
    {
        var ids = BuiltIns().Select(row => ((IProvider)row[1]).Kind).ToList();

        ids.Should().BeEquivalentTo(
            ToolKind.BuiltIn,
            because: "ToolKind.BuiltIn 은 플러그인이 못 쓰는 예약 id 다. 실제 도구와 어긋나면 예약이 틀린 것이다");
    }

    /// <summary>검사기 자체가 도는지. 답을 빠뜨린 도구는 걸려야 한다.</summary>
    [Fact]
    public void A_display_that_forgets_something_is_caught()
    {
        var incomplete = new ToolDisplay("제목", Vendor: "", "짧게", "?", ["#000000"], "M0 0", 1);

        var act = () => incomplete.Vendor.Should().NotBeNullOrWhiteSpace();

        act.Should().Throw<Exception>(because: "빠진 값을 통과시키는 검사는 검사가 아니다");
    }
}
