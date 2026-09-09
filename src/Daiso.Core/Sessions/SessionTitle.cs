using System.Text;

namespace Daiso.Core.Sessions;

/// <summary>
/// 세션 첫 프롬프트를 목록 한 줄에 쓸 제목으로 다듬는다.
/// <para>
/// 슬래시 명령으로 시작한 세션은 첫 프롬프트가 <c>&lt;command-name&gt;/goal&lt;/command-name&gt;</c> 같은
/// 원문이라 그대로 두면 읽히지 않는다. 태그를 걷어내고 한 줄로 만든다.
/// </para>
/// <para>
/// 여기는 순수 함수만 둔다. 화면(터미널 새 세션 카드 · 세션 목록 · 세션 상세)이 모두 이것을 쓴다 —
/// 정제 규칙이 화면마다 갈라지면 같은 세션이 화면마다 다르게 보인다.
/// (docs/TERMINAL_CARD_PLAN.md §7.7)
/// </para>
/// </summary>
public static class SessionTitle
{
    private const char Escape = (char)0x1b;

    /// <summary>
    /// 태그·제어문자·연속 공백을 걷어낸 한 줄짜리 프롬프트.
    /// 빈 값이면 빈 문자열이다 — 대신 보여 줄 문구는 부르는 쪽이 정한다.
    /// </summary>
    /// <remarks>
    /// <b>본문에 그냥 나온 <c>&lt;</c> 는 살려 둔다.</b> <c>if (a &lt; b)</c> 로 시작한 세션이
    /// <c>if (a</c> 로 잘리던 것을 고친 것이라, <c>&lt;</c> 는 진짜 태그처럼 보일 때만 태그로 친다
    /// (여는 꺾쇠 다음이 글자거나 <c>/</c>+글자이고, 다음 <c>&lt;</c> 전에 <c>&gt;</c> 가 온다).
    /// </remarks>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(raw.Length);
        var lastWasSpace = false;

        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];

            if (ch == '<' && TryReadTag(raw, i, out var closeIndex))
            {
                i = closeIndex;
                continue;
            }

            if (ch == Escape)
            {
                i = SkipAnsiSequence(raw, i);
                continue;
            }

            // 줄바꿈·탭도 제어문자라 이 검사보다 먼저 와야 한 칸으로 합쳐진다
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            // 화면에 네모(□)로 나오던 것들
            if (char.IsControl(ch))
            {
                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// <paramref name="start"/>의 여는 꺾쇠가 태그의 시작인가. 맞으면 닫는 꺾쇠 자리를 돌려준다.
    /// </summary>
    private static bool TryReadTag(string raw, int start, out int closeIndex)
    {
        closeIndex = start;

        var j = start + 1;

        if (j < raw.Length && raw[j] == '/')
        {
            j++;
        }

        // "< b)" 나 "<3" 은 태그가 아니다
        if (j >= raw.Length || !char.IsLetter(raw[j]))
        {
            return false;
        }

        for (var k = j; k < raw.Length; k++)
        {
            // 닫히기 전에 또 열리면 태그가 아니라 그냥 부등호다
            if (raw[k] == '<')
            {
                return false;
            }

            if (raw[k] == '>')
            {
                closeIndex = k;
                return true;
            }
        }

        return false;
    }

    /// <summary>ANSI 이스케이프(CSI) 하나를 건너뛰고 마지막으로 먹은 글자 자리를 돌려준다.</summary>
    private static int SkipAnsiSequence(string raw, int start)
    {
        var j = start + 1;

        // ESC 뒤에 '['가 없으면 ESC 한 글자만 버린다
        if (j >= raw.Length || raw[j] != '[')
        {
            return start;
        }

        j++;

        // 매개변수·중간 바이트
        while (j < raw.Length && raw[j] is >= ' ' and <= '?')
        {
            j++;
        }

        // 마지막 바이트(@~)까지 먹는다. 끝까지 안 닫혔으면 나머지를 통째로 버린다
        return j < raw.Length ? j : raw.Length - 1;
    }
}
