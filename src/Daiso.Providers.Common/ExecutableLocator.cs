namespace Daiso.Providers.Common;

/// <summary>
/// PATH에서 실행 파일을 찾는다. claude / codex는 npm이 만든 셸 래퍼(.cmd)다. (ARCHITECTURE §5.3)
/// </summary>
public static class ExecutableLocator
{
    /// <summary>Windows에서 확장자 없이 이름만 주어졌을 때 붙여볼 확장자.</summary>
    private static readonly string[] Extensions = [".cmd", ".exe", ".bat", ".ps1", string.Empty];

    /// <summary>PATH 어딘가에 실행 파일이 있는지.</summary>
    public static bool ExistsOnPath(string name) => Find(name) is not null;

    /// <summary>PATH에서 찾은 첫 경로. 없으면 null.</summary>
    public static string? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in Extensions)
            {
                string candidate;
                try
                {
                    candidate = System.IO.Path.Combine(directory.Trim(), name + extension);
                }
                catch (ArgumentException)
                {
                    break;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
