namespace Daiso.Core;

/// <summary>
/// 경로 목록을 <b>목록에 보여줄 이름</b>으로 바꾼다.
///
/// <para>
/// 폴더 이름만 쓰면 <c>TASK-1</c> 처럼 겹친다. 겹치는 것끼리는 <b>처음으로 달라지는 상위 폴더</b>를
/// 하나만 앞에 붙인다 (<c>as-r / TASK-1</c>, <c>as-m1 / TASK-1</c>). 중간 단계는 붙이지 않는다 —
/// 전체 경로를 목록에 뿌리면 목록이 경로로 뒤덮이고, 전체 값은 어차피 ToolTip 에 있다.
/// </para>
/// <para>
/// 화면 층(<c>Daiso.App.Services.Formats</c>)에 있던 것을 여기로 내렸다. 규칙이 까다로운데
/// 화면 층에는 테스트가 없었다 (2026-09-11 점검). 문구(모를 때 쓸 이름)만 밖에서 받는다.
/// </para>
/// </summary>
public static class PathLabels
{
    /// <summary>경로에서 사람이 부르는 이름(마지막 폴더)만 뽑는다.</summary>
    public static string FolderName(string? path, string unknown)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return unknown;
        }

        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);

        return name.Length > 0 ? name : trimmed;
    }

    /// <summary>목록에 나란히 놓을 이름들. 겹치는 이름에만 상위 폴더를 붙인다.</summary>
    public static IReadOnlyList<string> For(IReadOnlyList<string> paths, string unknown)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var segments = paths.Select(Segments).ToList();
        var labels = paths.Select(path => FolderName(path, unknown)).ToList();

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

                // 어느 단계에서도 갈리지 않으면(사실상 같은 폴더) 이름만 둔다
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
}
