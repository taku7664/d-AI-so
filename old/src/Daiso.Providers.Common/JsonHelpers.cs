using System.Globalization;
using System.Text.Json;

namespace Daiso.Providers.Common;

/// <summary>JsonElement에서 값을 안전하게 꺼내는 도우미. 형식이 다르면 조용히 null을 돌려준다.</summary>
public static class JsonHelpers
{
    /// <summary>객체의 속성을 찾는다. 객체가 아니거나 없으면 null.</summary>
    public static JsonElement? Prop(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : null;

    /// <summary>null이거나 객체가 아니면 null.</summary>
    public static JsonElement? Prop(this JsonElement? element, string name) =>
        element?.Prop(name);

    /// <summary>중첩 속성을 순서대로 따라간다.</summary>
    public static JsonElement? Path(this JsonElement element, params string[] names)
    {
        JsonElement? current = element;

        foreach (var name in names)
        {
            current = current?.Prop(name);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>null이면 그대로 null.</summary>
    public static JsonElement? Path(this JsonElement? element, params string[] names) =>
        element?.Path(names);

    /// <summary>문자열 값. 문자열이 아니거나 비어 있으면 null.</summary>
    public static string? Text(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } value && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>정수 값. 숫자가 아니면 null.</summary>
    public static long? Number(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number)
            ? number
            : null;

    /// <summary>정수 값. 없으면 0.</summary>
    public static long NumberOrZero(this JsonElement? element) => element.Number() ?? 0;

    /// <summary>true/false 값. 불리언이 아니면 null.</summary>
    public static bool? Boolean(this JsonElement? element) => element?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    /// <summary>배열 원소. 배열이 아니면 빈 목록.</summary>
    public static IEnumerable<JsonElement> Items(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    /// <summary>ISO 8601 타임스탬프. 파싱 실패 시 null.</summary>
    public static DateTimeOffset? Timestamp(this JsonElement? element) =>
        element.Text() is { } text
        && DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    /// <summary>한 줄을 JsonDocument로 파싱한다. 깨진 줄은 null.</summary>
    public static JsonDocument? TryParseLine(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
