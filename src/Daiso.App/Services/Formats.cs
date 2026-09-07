using System.Globalization;
using Daiso.App.Strings;

namespace Daiso.App.Services;

/// <summary>
/// 화면에 숫자·경로·시각을 사람이 읽기 쉽게 만든다. (ARCHITECTURE §6.2)
/// 원래 값은 ToolTip으로 남기므로 여기서는 과감히 줄인다.
/// </summary>
public static class Formats
{
    private const double Thousand = 1_000d;
    private const double Million = 1_000_000d;
    private const double Billion = 1_000_000_000d;

    /// <summary>토큰 수처럼 큰 수를 줄여 쓴다. 1,234 → 1.2K, 729,015,129 → 729.0M.</summary>
    public static string Tokens(long value)
    {
        var abs = Math.Abs((double)value);

        return abs switch
        {
            >= Billion => UiStrings.Format("Number_Billion", value / Billion),
            >= Million => UiStrings.Format("Number_Million", value / Million),
            >= Thousand => UiStrings.Format("Number_Thousand", value / Thousand),
            _ => value.ToString("N0", CultureInfo.CurrentCulture),
        };
    }

    /// <summary>정확한 값. ToolTip에 쓴다.</summary>
    public static string Exact(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>재로그인까지 남은 기간. 날짜와 남은 일수를 함께 보여준다.</summary>
    public static string Expiry(DateTimeOffset? at)
    {
        if (at is not { } deadline)
        {
            return UiStrings.Get("Auth_NoExpiry");
        }

        var local = deadline.ToLocalTime();
        var days = (int)Math.Floor((local - DateTimeOffset.Now).TotalDays);

        return days switch
        {
            < 0 => UiStrings.Get("Auth_Expired"),
            0 => UiStrings.Format("Auth_ExpiresToday", local),
            _ => UiStrings.Format("Auth_ExpiresIn", local, days),
        };
    }

    /// <summary>
    /// 경로 목록을 목록에 보여줄 이름으로 바꾼다.
    /// 폴더 이름만 쓰면 `TASK-1`처럼 겹치므로, 겹치는 것끼리 **처음으로 달라지는 상위 폴더**를
    /// 하나만 앞에 붙인다 (`as-r / TASK-1`, `as-m1 / TASK-1`). 중간 단계는 붙이지 않는다.
    /// </summary>
    public static IReadOnlyList<string> Labels(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var segments = paths.Select(Segments).ToList();
        var labels = paths.Select(FolderName).ToList();

        var groups = labels
            .Select((label, index) => (label, index))
            .GroupBy(entry => entry.label, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in groups)
        {
            var members = group.Select(entry => entry.index).ToList();
            var ancestor = FirstDifferingAncestor(members, segments);

            foreach (var index in members)
            {
                var parts = segments[index];

                // 어느 단계에서도 갈리지 않으면(사실상 같은 폴더) 이름만 둔다.
                // 전체 경로를 목록에 뿌리면 목록이 경로로 뒤덮인다. 전체 값은 ToolTip에 있다.
                if (ancestor is { } level && parts.Length > level)
                {
                    labels[index] = $"{parts[^(level + 1)]} / {labels[index]}";
                }
            }
        }

        return labels;
    }

    /// <summary>
    /// 같은 이름을 가진 경로들 사이에서 처음으로 값이 갈리는 상위 단계를 찾는다.
    /// 1이면 바로 위 폴더, 2면 그 위. 끝까지 같으면 null.
    /// </summary>
    private static int? FirstDifferingAncestor(List<int> members, List<string[]> segments)
    {
        var deepest = members.Max(index => segments[index].Length);

        for (var level = 1; level < deepest; level++)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var distinct = true;

            foreach (var index in members)
            {
                var parts = segments[index];
                var name = parts.Length > level ? parts[^(level + 1)] : string.Empty;

                if (!seen.Add(name))
                {
                    distinct = false;
                    break;
                }
            }

            if (distinct)
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>경로를 폴더 단위로 자른다.</summary>
    private static string[] Segments(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? []
            : path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>경로에서 사람이 부르는 이름(마지막 폴더)만 뽑는다.</summary>
    public static string FolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return UiStrings.Get("Common_Unknown");
        }

        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);

        return name.Length > 0 ? name : trimmed;
    }
}
