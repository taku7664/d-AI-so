namespace Daiso.Core;

/// <summary>
/// 사람이 쓴 글을 길이로 자를 때 <b>글자를 쪼개지 않는다</b>.
///
/// <para>
/// .NET 의 문자열 길이는 UTF-16 단위다. 이모지·일부 한자는 한 글자가 두 단위(서로게이트 짝)라,
/// 그 사이에서 자르면 짝 잃은 반쪽이 남아 화면에 <c>?</c> 로 나오고 인덱스에도 그대로 들어간다
/// (2026-09-11 점검에서 첫 프롬프트·요약 일곱 자리).
/// </para>
/// <para>
/// 글자 묶음(결합 문자·국기 이모지 같은 자소 묶음)까지 지키려면 더 들어가야 하지만,
/// 깨져 보이는 것은 짝을 끊었을 때뿐이라 거기까지만 한다.
/// </para>
/// </summary>
public static class TextCut
{
    /// <summary>앞에서 <paramref name="max"/> 단위까지. 짧으면 그대로. 짝 사이에 걸리면 한 단위 물러난다.</summary>
    public static string Head(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || max <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= max)
        {
            return text;
        }

        // 자를 자리 앞 글자가 상위 서로게이트면 그 짝이 뒤에 있다. 한 단위 물러나 짝을 함께 버린다
        var end = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;

        return text[..end];
    }
}
