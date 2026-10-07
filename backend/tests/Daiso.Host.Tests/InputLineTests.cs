using Daiso.Host.Tabs.Terminal;

namespace Daiso.Host.Tests;

/// <summary>방 입력 줄 따라가기: 질문을 보냈나, 명령을 쳤나만 가르면 된다.</summary>
public sealed class InputLineTests
{
    [Fact]
    public void Typing_keystroke_by_keystroke_gives_the_line_on_enter()
    {
        var line = new InputLine();

        foreach (var key in "/logn")
        {
            line.Feed(key.ToString()).Should().BeEmpty();
        }

        line.Feed("\u007f").Should().BeEmpty();
        line.Feed("in\r").Should().Equal("/login");
    }

    [Fact]
    public void Paste_markers_and_arrow_keys_are_skipped_and_ctrl_u_clears()
    {
        var line = new InputLine();

        line.Feed("\u001b[200~첫 줄\n둘째 줄\u001b[201~").Should().BeEmpty();
        line.Feed("\u001b[D\r").Should().Equal("첫 줄\n둘째 줄");
        line.Feed("지울 글\u0015남길 글\r").Should().Equal("남길 글");
    }
}
