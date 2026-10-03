namespace Daiso.Providers.Common;

/// <summary>
/// 프로젝트 경로 정규화. 그룹핑·필터·고아 판정은 모두 이 값으로만 한다. (ARCHITECTURE §4.3)
/// </summary>
public static class ProjectPathNormalizer
{
    /// <summary>드라이브 문자 대문자, 구분자 `\` 통일, 끝 구분자 제거.</summary>
    /// <returns>비어 있거나 정규화할 수 없으면 null.</returns>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        full = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        // 드라이브 루트("C:\")는 끝 구분자를 유지해야 경로가 성립한다.
        var trimmed = full.Length > 3 ? full.TrimEnd(Path.DirectorySeparatorChar) : full;

        return trimmed.Length >= 2 && trimmed[1] == ':'
            ? char.ToUpperInvariant(trimmed[0]) + trimmed[1..]
            : trimmed;
    }

    /// <summary>
    /// 경로의 각 조각을 디스크에 실제로 있는 대소문자로 되돌린다. Gemini는 projects.json에 경로를 소문자로 저장해
    /// 그대로 두면 같은 폴더가 Claude·Codex의 프로젝트와 따로 묶인다. 없는 조각부터는 받은 그대로 둔다.
    /// </summary>
    public static string? RestoreCasing(string? path)
    {
        var normalized = Normalize(path);
        if (normalized is null)
        {
            return null;
        }

        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root) || normalized.Length <= root.Length)
        {
            return normalized;
        }

        var current = root;
        var segments = normalized[root.Length..].Split(Path.DirectorySeparatorChar);

        for (var i = 0; i < segments.Length; i++)
        {
            string? actual = null;
            try
            {
                if (Directory.Exists(current))
                {
                    actual = Directory.EnumerateFileSystemEntries(current)
                        .Select(Path.GetFileName)
                        .FirstOrDefault(name => string.Equals(name, segments[i], StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                actual = null;
            }

            if (actual is null)
            {
                // 여기부터는 디스크에 없다. 남은 조각은 받은 그대로
                return Path.Combine(current, string.Join(Path.DirectorySeparatorChar, segments[i..]));
            }

            current = Path.Combine(current, actual);
        }

        return current;
    }

    /// <summary>정규화 결과가 같은 경로인지 비교한다.</summary>
    public static bool AreSame(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
