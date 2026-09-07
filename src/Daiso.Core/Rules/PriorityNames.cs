namespace Daiso.Core;

/// <summary><see cref="Priority"/> ↔ YAML 표기(MUST/SHOULD/MAY) 변환.</summary>
internal static class PriorityNames
{
    internal const string Must = "MUST";
    internal const string Should = "SHOULD";
    internal const string May = "MAY";

    /// <summary>대소문자를 무시하고 우선순위를 해석한다.</summary>
    internal static bool TryParse(string? text, out Priority priority)
    {
        var trimmed = text?.Trim();

        if (string.Equals(trimmed, Must, StringComparison.OrdinalIgnoreCase))
        {
            priority = Priority.Must;
            return true;
        }

        if (string.Equals(trimmed, Should, StringComparison.OrdinalIgnoreCase))
        {
            priority = Priority.Should;
            return true;
        }

        if (string.Equals(trimmed, May, StringComparison.OrdinalIgnoreCase))
        {
            priority = Priority.May;
            return true;
        }

        priority = Priority.Should;
        return false;
    }

    internal static string ToYaml(Priority priority) => priority switch
    {
        Priority.Must => Must,
        Priority.Should => Should,
        Priority.May => May,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, null),
    };
}
