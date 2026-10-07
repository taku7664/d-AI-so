using System.Text.RegularExpressions;

namespace Daiso.Host.Tabs.Terminal;

/// <summary>
/// CLI 가 입력 줄에 그리는 그림 첨부 표시(Claude Code 는 <c>[Image #1]</c>). 그림 붙이기 키를 보낸 뒤 첨부가 끝났는지 이것으로 안다.
/// Claude 는 키를 받으면 PowerShell 로 클립보드를 읽어 1초 넘게 걸린다. 그동안 글을 보내면 그림이 글 뒤에 붙어 빠진다(2026-10-07 확인).
/// 번호는 세션 안에서 계속 올라간다(#1, #2, 입력을 지운 뒤에도 #3). 입력 줄을 다시 그릴 때 옛 표시도 다시 나오므로,
/// 키를 보내기 직전 화면에 없던 번호가 새로 나와야 첨부된 것으로 본다.
/// </summary>
internal static partial class ImageMark
{
    /// <summary>화면 글자에서 첨부 표시의 번호들. 번호 없는 표시는 빈 글자.</summary>
    public static HashSet<string> Numbers(string screen)
    {
        var plain = Escapes().Replace(screen, string.Empty);
        return Mark().Matches(plain).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>나중 화면에 앞 화면에 없던 표시가 있다.</summary>
    public static bool Added(HashSet<string> before, string after) => Numbers(after).Any(number => !before.Contains(number));

    [GeneratedRegex(@"\[image(?: #(\d+))?[^\]\r\n]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex Mark();

    // CSI(색·커서)와 OSC(창 제목) 시퀀스
    [GeneratedRegex(@"\x1b\[[0-9;?]*[ -/]*[@-~]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)")]
    private static partial Regex Escapes();
}
