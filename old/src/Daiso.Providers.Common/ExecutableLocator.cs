namespace Daiso.Providers.Common;

/// <summary>
/// PATH에서 실행 파일을 찾는다. claude / codex는 npm이 만든 셸 래퍼(.cmd)다. (ARCHITECTURE §5.3)
/// </summary>
public static class ExecutableLocator
{
    /// <summary>Windows에서 확장자 없이 이름만 주어졌을 때 붙여볼 확장자.</summary>
    private static readonly string[] Extensions = [".cmd", ".exe", ".bat", ".ps1", string.Empty];

    /// <summary>npm 셸 래퍼만. <see cref="NpmLaunchTarget"/> 이 쓴다.</summary>
    private static readonly string[] CmdOnly = [".cmd"];

    /// <summary>PATH 어딘가에 실행 파일이 있는지.</summary>
    public static bool ExistsOnPath(string name) => Find(name) is not null;

    /// <summary>PATH에서 찾은 첫 경로. 없으면 null.</summary>
    /// <param name="name">확장자 없는 이름. <c>claude</c>.</param>
    /// <param name="searchPath">훑을 PATH 문자열. null 이면 이 프로세스의 PATH. 테스트가 넣는다.</param>
    public static string? Find(string name, string? searchPath = null) => Find(name, Extensions, searchPath);

    /// <summary>
    /// npm 으로 깔린 도구를 <b>셸에 넘길 때</b> 쓸 대상. PATH 에 <c>{name}.cmd</c> 가 있으면 그 절대 경로, 없으면 이름 그대로.
    /// <para>
    /// npm 은 <c>claude</c>·<c>claude.cmd</c>·<c>claude.ps1</c> 셋을 나란히 만든다. PowerShell 에 이름만 넘기면
    /// <c>.cmd</c> 가 아니라 <c>.ps1</c> 을 고르고, Windows 기본 실행 정책(Restricted)은 서명 없는 스크립트를 거부한다 —
    /// 새 PC 에서 "이 시스템에서 스크립트를 실행할 수 없으므로 claude.ps1 파일을 로드할 수 없습니다" 가 그것이다 (2026-09-11 재현).
    /// <c>.cmd</c> 는 정책과 무관하게 돈다. 그래서 확장자를 우리가 정해 준다.
    /// </para>
    /// <para>
    /// <c>.exe</c> 는 보지 않는다. Codex 데스크톱 앱이 깐 네이티브 exe 가 PATH 앞쪽에 있어도 npm 셸만 쓴다는 규칙(§5.3) 그대로다.
    /// </para>
    /// </summary>
    public static string NpmLaunchTarget(string name, string? searchPath = null) => Find(name, CmdOnly, searchPath) ?? name;

    private static string? Find(string name, string[] extensions, string? searchPath)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var path = searchPath ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
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
