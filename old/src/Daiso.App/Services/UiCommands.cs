using CommunityToolkit.Mvvm.Input;

namespace Daiso.App.Services;

/// <summary>
/// 화면에서 비동기 명령을 부르는 통로. 취소는 실패가 아니라 정상 종료로 본다.
///
/// <para>
/// <c>[RelayCommand]</c>가 CancellationToken을 받는 메서드로 만든 명령은, 같은 명령을 다시 부르면
/// 앞서 돌던 실행의 토큰을 먼저 끊는다(CommunityToolkit.Mvvm의 AsyncRelayCommand).
/// 첫 읽기가 아직 세션 인덱스를 뒤지는 동안 화면을 옮기거나 탭을 바꾸면 늘 일어나는 일이다.
/// 그러면 앞의 <c>await</c>가 OperationCanceledException을 받는데, 페이지의 Loaded·클릭 핸들러는
/// async void라 그 예외가 처리되지 않은 예외로 올라가 "문제가 생겼습니다" 대화상자를 띄웠다.
/// 화면은 새 실행이 이어받으므로 끊긴 실행은 조용히 끝나야 한다.
/// </para>
/// </summary>
internal static class UiCommands
{
    /// <summary>명령을 실행하고 끝나기를 기다린다. 다음 실행이 끊었으면 조용히 끝난다.</summary>
    internal static async Task RunAsync(IAsyncRelayCommand command, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            await command.ExecuteAsync(parameter).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 다음 실행이 앞의 토큰을 끊었다. 화면은 그 실행이 이어받는다.
        }
    }

    /// <summary>
    /// 기다리지 않고 실행한다. 취소만 여기서 삼키고, 진짜 실패는 예전처럼
    /// 지켜보지 않은 Task로 남아 <see cref="CrashReporter"/>의 "Task" 통로로 올라간다.
    /// </summary>
    internal static void Start(IAsyncRelayCommand command, object? parameter = null) =>
        _ = RunAsync(command, parameter);
}
