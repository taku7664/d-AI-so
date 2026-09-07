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

    /// <summary>정규화 결과가 같은 경로인지 비교한다.</summary>
    public static bool AreSame(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
