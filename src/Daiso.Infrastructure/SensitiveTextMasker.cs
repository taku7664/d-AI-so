using System.Text.RegularExpressions;

namespace Daiso.Infrastructure;

/// <summary>
/// 로그·예외 메시지에서 토큰처럼 보이는 문자열을 가린다. (ARCHITECTURE §7.1)
/// 남기고 후회하기보다, 의심스러우면 가리는 쪽을 택한다.
/// </summary>
public static partial class SensitiveTextMasker
{
    /// <summary>가려진 자리에 들어가는 표시.</summary>
    public const string Redacted = "[REDACTED]";

    /// <summary>주어진 텍스트에서 민감해 보이는 부분을 가린다.</summary>
    public static string MaskSensitive(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var masked = ApiKeyPattern().Replace(text, Redacted);
        masked = JwtPattern().Replace(masked, Redacted);
        masked = BearerPattern().Replace(masked, $"Bearer {Redacted}");
        masked = AssignedSecretPattern().Replace(masked, match => match.Groups["prefix"].Value + Redacted);
        masked = LongOpaquePattern().Replace(masked, Redacted);

        return masked;
    }

    /// <summary>`sk-ant-...` 처럼 접두사가 뚜렷한 키.</summary>
    [GeneratedRegex(@"\bsk-[A-Za-z0-9\-_]{8,}", RegexOptions.None, 200)]
    private static partial Regex ApiKeyPattern();

    /// <summary>세 조각으로 나뉜 JWT.</summary>
    [GeneratedRegex(@"\b[A-Za-z0-9\-_]{8,}\.[A-Za-z0-9\-_]{8,}\.[A-Za-z0-9\-_]{8,}\b", RegexOptions.None, 200)]
    private static partial Regex JwtPattern();

    /// <summary>Authorization 헤더 형태.</summary>
    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-\._~\+/=]{8,}", RegexOptions.IgnoreCase, 200)]
    private static partial Regex BearerPattern();

    /// <summary>`accessToken": "..."`, `api_key=...` 처럼 이름이 토큰임을 알려주는 대입.</summary>
    [GeneratedRegex(
        "(?<prefix>(?:access|refresh|id|api|secret|auth|bearer)[_\\-]?(?:token|key|secret)?\"?\\s*[:=]\\s*\"?)[A-Za-z0-9\\-\\._~\\+/=]{8,}",
        RegexOptions.IgnoreCase,
        200)]
    private static partial Regex AssignedSecretPattern();

    /// <summary>
    /// 길고 뜻 없는 base64/hex 덩어리. 40자 이상만 본다.
    /// 경로·한글 문장은 이 문자 집합에 걸리지 않는다.
    /// </summary>
    [GeneratedRegex(@"\b[A-Za-z0-9\+/=_]{40,}\b", RegexOptions.None, 200)]
    private static partial Regex LongOpaquePattern();
}
