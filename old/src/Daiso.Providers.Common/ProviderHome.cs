namespace Daiso.Providers.Common;

/// <summary>
/// Provider가 파일을 찾는 기준 폴더. 기본값은 사용자 프로필이고, 테스트는 fixture 폴더를 넘긴다.
/// </summary>
public sealed record ProviderHome(string Directory)
{
    /// <summary>%USERPROFILE% 기준.</summary>
    public static ProviderHome FromUserProfile() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>기준 폴더 아래 경로를 만든다.</summary>
    public string Combine(params string[] parts) =>
        System.IO.Path.Combine([Directory, .. parts]);
}

/// <summary>실행 중인 프로세스 확인. 테스트에서 갈아끼운다.</summary>
public interface IProcessProbe
{
    /// <summary>해당 pid의 프로세스가 살아 있는지.</summary>
    bool IsAlive(int pid);
}

/// <summary>실제 OS 프로세스를 확인한다.</summary>
public sealed class ProcessProbe : IProcessProbe
{
    /// <inheritdoc />
    public bool IsAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
