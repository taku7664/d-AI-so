namespace Daiso.Infrastructure;

/// <summary>
/// 이 앱이 자기 것을 두는 폴더. 인덱스·설정·계정 보관함·프롬프트·로그가 모두 이 아래에 있다.
///
/// <para>
/// 이름을 <c>d-AI-so</c> 에서 <b><c>DAIso</c></b> 로 바꾸면서(2026-09-12) <b>쓰던 것을 옮겨 온다</b>.
/// 새 폴더를 만들고 끝내면 인덱스 100MB 와 보관해 둔 계정이 갈 곳을 잃은 채 옛 폴더에 남는다 —
/// 사람이 보기에는 그냥 사라진 것이다.
/// </para>
///
/// <para>
/// 옮기는 일은 <b>처음 묻는 그 순간 한 번</b> 일어난다. 어느 곳이 먼저 묻든 같은 답을 받으므로
/// 부르는 쪽이 순서를 지킬 필요가 없다. 옮기지 못하면(파일이 잠겨 있거나 권한이 없으면)
/// <b>옛 폴더를 그대로 쓴다</b> — 못 옮겼다고 쓰던 것을 못 쓰게 되는 쪽이 더 나쁘다.
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>지금 쓰는 이름.</summary>
    private const string CurrentFolder = "DAIso";

    /// <summary>0.1.1 까지 쓰던 이름.</summary>
    private const string LegacyFolder = "d-AI-so";

    /// <summary>자료 폴더. 처음 부를 때 한 번 정해진다.</summary>
    public static string Root { get; } = Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>자료 폴더 아래 경로.</summary>
    public static string Combine(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        return Path.Combine([Root, .. parts]);
    }

    /// <summary>
    /// 어느 폴더를 쓸지 정하고, 필요하면 옛것을 옮겨 온다.
    /// <paramref name="local"/> 를 받는 이유는 테스트가 진짜 사용자 폴더를 건드리지 않게 하기 위해서다.
    /// </summary>
    public static string Resolve(string local)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(local);

        var current = Path.Combine(local, CurrentFolder);
        var legacy = Path.Combine(local, LegacyFolder);

        if (!Directory.Exists(legacy))
        {
            // 옛것이 없다. 처음 쓰는 PC 이거나 이미 옮긴 뒤다
            return current;
        }

        if (!Directory.Exists(current))
        {
            return TryMove(legacy, current) ? current : legacy;
        }

        // 새 폴더가 이미 있다. 비어 있고 옛것에 내용이 있으면 앞선 이사가 덜 끝난 것이다 — 마저 옮긴다
        if (IsEmpty(current) && !IsEmpty(legacy))
        {
            return TryMoveContents(legacy, current) ? current : legacy;
        }

        return current;
    }

    private static bool IsEmpty(string directory)
    {
        try
        {
            return !Directory.EnumerateFileSystemEntries(directory).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>폴더째 옮긴다. 같은 드라이브면 이름만 바뀌므로 크기와 상관없이 한순간이다.</summary>
    private static bool TryMove(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 누가 그 안의 파일을 열어 두고 있다(앱이 아직 떠 있는 경우 등). 옛 폴더를 그대로 쓴다
            return false;
        }
    }

    /// <summary>안에 든 것만 하나씩 옮긴다. 하나라도 실패하면 옛 폴더를 계속 쓴다.</summary>
    private static bool TryMoveContents(string from, string to)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(from))
            {
                Directory.Move(directory, Path.Combine(to, Path.GetFileName(directory)));
            }

            foreach (var file in Directory.EnumerateFiles(from))
            {
                File.Move(file, Path.Combine(to, Path.GetFileName(file)));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
