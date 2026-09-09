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
    /// <summary>
    /// 태그·연속 공백을 걷어낸 한 줄짜리 프롬프트. 빈 값이면 빈 문자열이다(문구는 부르는 쪽이 정한다).
    /// </summary>
    /// <remarks>
    /// <b>지금은 다음 둘을 못 고친다.</b> 옮겨 온 그대로이고, 고치는 것은 Stage 0-3이다
    /// (docs/TERMINAL_CARD_PLAN.md §6 Stage 0). <see cref="Daiso.Core.Tests"/>의 특성화 테스트가
    /// 이 동작을 고정하고 있으므로, 고칠 때 그 기댓값을 함께 뒤집어야 한다.
    /// <list type="number">
    /// <item>제어문자·ANSI 이스케이프를 안 지운다 — <see cref="char.IsWhiteSpace(char)"/>는 ESC(U+001B)를 안 잡는다</item>
    /// <item>본문에 나온 <c>&lt;</c> 가 뒤를 통째로 삼킨다 — 닫는 <c>&gt;</c> 가 없으면 문자열 끝까지 버린다</item>
    /// </list>
    /// </remarks>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(raw.Length);
        var depth = 0;
        var lastWasSpace = false;

        foreach (var ch in raw)
        {
            if (ch == '<')
            {
                depth++;
                continue;
            }

            if (ch == '>' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }
}
