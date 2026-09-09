using System.Text.RegularExpressions;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// ARCHITECTURE §6.2 — 도구별 표시 규칙은 <c>ToolLook</c> 한 곳이다.
///
/// <para>
/// 문제는 <c>ToolLook</c> 의 switch 가 전부 <c>_ =&gt;</c> 로 끝난다는 것이다.
/// <see cref="ToolKind"/> 에 값을 하나 더하고 <c>ToolLook</c> 을 안 고치면
/// <b>빌드도 테스트도 통과하고</b>, 화면에는 회색 원에 <c>?</c> 가 뜬다.
/// 목록 순서(<c>DisplayOrder</c>)에서도 빠져 탭 인덱스 계산에서 사라진다.
/// </para>
///
/// <para>
/// 그 폴백을 지울 수는 없다 — switch 식은 모든 입력을 받아야 한다.
/// 그래서 "빠진 것이 있는가"를 여기서 본다. Core 테스트가 App 소스를 글로 읽는 방식은
/// <see cref="StringResourceKeysTests"/> 와 같다.
/// </para>
/// </summary>
public sealed partial class ToolLookTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string ToolLookPath =
        Path.Combine(Root, "src", "Daiso.App", "Services", "ToolLook.cs");

    private static readonly string Source = File.ReadAllText(ToolLookPath);

    private static readonly string[] Kinds = Enum.GetNames<ToolKind>();

    /// <summary>도구마다 답이 있어야 하는 물음들. 이름은 <c>ToolLook</c> 의 멤버 이름이다.</summary>
    public static IEnumerable<object[]> SwitchMembers() =>
        new[] { "Title", "Vendor", "Short", "Initial", "Color", "LogoPath" }
            .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(SwitchMembers))]
    public void Every_tool_has_its_own_answer(string member)
    {
        var body = SwitchBody(member);
        var missing = Kinds.Where(kind => !body.Contains($"ToolKind.{kind} =>", StringComparison.Ordinal)).ToList();

        missing.Should().BeEmpty(
            because: $"ToolLook.{member} 이 답하지 않는 도구는 화면에서 폴백(회색 원 · '?')으로 보인다. 조용히 틀리는 쪽이라 여기서 잡는다");
    }

    [Fact]
    public void Display_order_lists_every_tool()
    {
        var line = Line("DisplayOrder");
        var missing = Kinds.Where(kind => !line.Contains($"ToolKind.{kind}", StringComparison.Ordinal)).ToList();

        missing.Should().BeEmpty(
            because: "DisplayOrder 에 없는 도구는 Rank 가 int.MaxValue 라 목록 맨 뒤로 밀리고, 탭 인덱스 계산에서 빠진다");
    }

    [Fact]
    public void Display_order_does_not_repeat_a_tool()
    {
        var line = Line("DisplayOrder");
        var listed = Kinds.Where(kind => line.Contains($"ToolKind.{kind}", StringComparison.Ordinal)).ToList();

        listed.Should().HaveCount(Kinds.Length, because: "탭 인덱스가 DisplayOrder 위치로 계산된다. 중복되면 탭과 도구가 어긋난다");
    }

    // ── 검사기 자체가 도는지 ──────────────────────────────────────────────

    [Fact]
    public void A_switch_that_forgets_a_tool_is_caught()
    {
        const string Incomplete = "ToolKind.Claude => \"a\", _ => \"?\",";

        Kinds.Where(kind => !Incomplete.Contains($"ToolKind.{kind} =>", StringComparison.Ordinal))
            .Should().NotBeEmpty(because: "폴백만 있고 도구가 빠진 switch 는 걸려야 한다");
    }

    // ── 도구 ─────────────────────────────────────────────────────────────

    /// <summary>멤버 이름 뒤의 <c>switch { ... }</c> 안쪽. 못 찾으면 테스트를 실패시킨다.</summary>
    private static string SwitchBody(string member)
    {
        var at = Source.IndexOf($" {member}(ToolKind", StringComparison.Ordinal);
        at.Should().BeGreaterThan(0, because: $"ToolLook 에 {member}(ToolKind ...) 가 있어야 한다. 이름을 바꿨으면 이 테스트도 같이 고친다");

        var start = Source.IndexOf("switch", at, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, because: $"{member} 은 switch 식이어야 한다");

        var end = Source.IndexOf("};", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        return Source[start..end];
    }

    /// <summary>그 이름이 나오는 첫 줄.</summary>
    private static string Line(string member)
    {
        var line = Source.Split('\n').FirstOrDefault(text => text.Contains($"{member} =", StringComparison.Ordinal));

        line.Should().NotBeNull(because: $"ToolLook 에 {member} 가 있어야 한다");

        return line!;
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

        throw new InvalidOperationException("Daiso.sln 이 있는 저장소 루트를 찾지 못했다");
    }
}
