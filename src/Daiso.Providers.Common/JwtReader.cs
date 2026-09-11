using System.Text.Json;

namespace Daiso.Providers.Common;

/// <summary>
/// JWT payload 에서 <b>필요한 조각만</b> 꺼낸다. 서명은 검증하지 않고, 토큰 원문은 즉시 버린다. (ARCHITECTURE §4.2, §7.1)
/// <para>
/// 꺼내는 것은 만료 시각과 계정 표시값(이메일·요금제)뿐이다. 그 밖의 클레임은 읽지 않는다 —
/// 화면에 쓸 데가 없는 값을 메모리에 올리지 않는 것이 §7.1 을 지키는 가장 싼 방법이다.
/// </para>
/// </summary>
public static class JwtReader
{
    /// <summary>ChatGPT 로그인이 요금제를 담는 클레임. 이름이 URL 인 것은 OIDC 사용자 정의 클레임 관례다.</summary>
    private const string OpenAiAuthClaim = "https://api.openai.com/auth";

    /// <summary>액세스 토큰의 만료 시각. 디코드 실패 시 null.</summary>
    public static DateTimeOffset? Expiry(string? jwt)
    {
        using var payload = Payload(jwt);

        if (payload is null)
        {
            return null;
        }

        try
        {
            return payload.RootElement.Prop("exp").Number() is { } exp
                ? DateTimeOffset.FromUnixTimeSeconds(exp)
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            // exp 가 DateTimeOffset 범위를 벗어난 값이면 만료를 모르는 것으로 둔다
            return null;
        }
    }

    /// <summary>
    /// id 토큰이 말하는 계정. 둘 다 없으면 (null, null) —
    /// <b>없는 값을 다른 값으로 메우지 않는다.</b> 예전에는 계정 이름 자리에 인증 방식(`chatgpt`)을 넣어
    /// 계정처럼 보이게 했는데, 그것은 계정이 아니다 (2026-09-11 사람의 지적).
    /// </summary>
    public static (string? Email, string? Plan) Account(string? jwt)
    {
        using var payload = Payload(jwt);

        if (payload is null)
        {
            return (null, null);
        }

        var root = payload.RootElement;

        return (root.Prop("email").Text(), Plan(root));
    }

    /// <summary>요금제. 사용자 정의 클레임 안에 있고, 없으면 최상위도 본다 (토큰 판마다 자리가 달랐다).</summary>
    private static string? Plan(JsonElement root) =>
        root.Prop(OpenAiAuthClaim).Prop("chatgpt_plan_type").Text()
        ?? root.Prop("chatgpt_plan_type").Text();

    /// <summary>payload 를 JSON 으로 푼다. 세 토막이 아니거나 base64 가 깨졌으면 null.</summary>
    private static JsonDocument? Payload(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        var parts = jwt.Split('.');

        if (parts.Length < 2 || Decode(parts[1]) is not { } bytes)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
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
