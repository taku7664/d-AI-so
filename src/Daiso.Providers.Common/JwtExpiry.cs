using System.Text.Json;

namespace Daiso.Providers.Common;

/// <summary>
/// JWT payload의 `exp`만 꺼낸다. 서명은 검증하지 않고, 토큰 원문은 즉시 버린다. (ARCHITECTURE §4.2, §7.1)
/// </summary>
public static class JwtExpiry
{
    /// <summary>액세스 토큰의 만료 시각. 디코드 실패 시 null.</summary>
    public static DateTimeOffset? Read(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        var parts = jwt.Split('.');
        if (parts.Length < 2 || Decode(parts[1]) is not { } payload)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var exp = document.RootElement.Prop("exp").Number();
            return exp is null ? null : DateTimeOffset.FromUnixTimeSeconds(exp.Value);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[]? Decode(string segment)
    {
        var base64 = segment.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (base64.Length % 4)) % 4;

        if (padding == 3)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64 + new string('=', padding));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
