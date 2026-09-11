namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// "사람이 이 방을 보고 있는가" 는 <c>RoomManager</c> 한 곳이 정한다.
///
/// <para>
/// 예전에는 터미널 화면이 방을 바꿀 때 직접 <c>MarkActive</c>/<c>MarkInactive</c> 를 불렀다.
/// 그래서 <b>다른 화면으로 가거나 다른 앱을 쓰는 동안</b>에는 마지막에 보던 방이 계속 "보고 있음" 으로 남았고,
/// 도구가 일을 마쳐도 탭에 점이 켜지지 않았다 — 자리를 비운 사이에 끝난 일을 알 방법이 없었다 (2026-09-11 사람의 지적).
/// </para>
///
/// <para>
/// 지금은 창이 앞에 있고(<c>SetWindowActive</c>) 터미널 화면이 열려 있고(<c>SetTerminalVisible</c>)
/// 그 방 탭이 골라져 있을 때만 본 것으로 친다. 화면은 <c>ActiveRoom</c> 만 적는다.
/// </para>
/// </summary>
public sealed class RoomWatchingTests
{
    private static readonly string AppRoot = Path.Combine(FindRepositoryRoot(), "src", "Daiso.App");

    /// <summary>이 표시를 <b>손으로</b> 옮겨도 되는 곳. 정의하는 자리와, 정하는 자리 하나뿐이다.</summary>
    private static readonly string[] MayMark =
    [
        Path.Combine("ViewModels", "IRoom.cs"),
        Path.Combine("ViewModels", "RoomManager.cs"),
        Path.Combine("ViewModels", "TerminalRoomViewModel.cs"),
        Path.Combine("ViewModels", "StreamingRoomViewModel.cs"),
    ];

    [Fact]
    public void Only_the_room_manager_decides_which_room_is_being_watched()
    {
        var offenders = SourceFiles()
            .Where(path => !MayMark.Any(allowed => path.EndsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            .Where(path => File.ReadAllText(path) is var text
                && (text.Contains("MarkActive(", StringComparison.Ordinal) || text.Contains("MarkInactive(", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            because: "화면이 직접 본 것으로 치면, 다른 화면·다른 앱에 가 있는 동안 끝난 일에 점이 켜지지 않는다");
    }

    [Fact]
    public void The_shell_tells_the_room_manager_about_the_window_and_the_page()
    {
        var shell = File.ReadAllText(Path.Combine(AppRoot, "ShellWindow.xaml.cs"));

        shell.Should().Contain("SetWindowActive(", because: "창이 뒤로 가면 보고 있는 방이 없다");
        shell.Should().Contain("SetTerminalVisible(", because: "터미널 화면을 떠나면 그 방을 보고 있지 않다");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(AppRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("저장소 뿌리를 찾지 못했다");
    }
}
