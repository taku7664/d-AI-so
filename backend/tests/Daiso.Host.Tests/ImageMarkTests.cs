using System.Text;
using Daiso.Host.Tabs.Terminal;

namespace Daiso.Host.Tests;

/// <summary>그림 첨부 표시 찾기와 방 출력 끝부분. 화면 글은 2026-10-07 Claude Code v2.1.286 방에서 받은 것을 줄였다.</summary>
public sealed class ImageMarkTests
{
    private const string Esc = "\u001b";

    [Fact]
    public void A_new_number_counts_but_a_redrawn_old_one_does_not()
    {
        var before = ImageMark.Numbers($"{Esc}[2K❯ {Esc}[1m[Image #1]{Esc}[22m\r──────\r");

        ImageMark.Added(before, $"❯ [Image #1]\rPasting…\r").Should().BeFalse(because: "입력 줄을 다시 그리면 옛 표시가 또 나온다");
        ImageMark.Added(before, $"❯ [Image #1] {Esc}[1m[Image #2]{Esc}[22m\r").Should().BeTrue();
    }

    [Fact]
    public void After_the_line_was_cleared_any_mark_is_new()
    {
        var before = ImageMark.Numbers("❯ \rCtrl+Y to paste deleted text\r");

        before.Should().BeEmpty();
        ImageMark.Added(before, $"{Esc}]0;✳ Claude Code{Esc}\\❯ [Image #3]\r").Should().BeTrue();
    }

    [Fact]
    public void Marks_without_a_number_are_found_too()
    {
        ImageMark.Numbers("› [image 1280x820 PNG] 설명해 줘").Should().Equal(string.Empty);
    }

    [Fact]
    public void Output_tail_and_follow()
    {
        var output = new RoomOutput();
        output.Append(Encoding.UTF8.GetBytes("첫 덩어리 "));
        output.Append(Encoding.UTF8.GetBytes("둘째"));

        Encoding.UTF8.GetString(output.Tail(3)).Should().Be("둘째", because: "덩어리 경계에서 자른다");
        Encoding.UTF8.GetString(output.Tail(1000)).Should().Be("첫 덩어리 둘째");

        var live = output.Follow();
        output.Append(Encoding.UTF8.GetBytes("셋째"));
        live.Reader.TryRead(out var next).Should().BeTrue();
        Encoding.UTF8.GetString(next!).Should().Be("셋째", because: "지난 출력은 주지 않는다");
        output.Unsubscribe(live);
    }
}
