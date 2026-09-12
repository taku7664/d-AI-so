using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace Daiso.App;

/// <summary>
/// 앱의 들머리. WinUI 가 만들어 주던 것을 직접 쓴다(`DISABLE_XAML_GENERATED_MAIN`).
///
/// <para>
/// 이유는 <b>하나만 뜨게</b> 하기 위해서다. 창을 닫아도 앱이 알림 영역에 남으므로(2026-09-12),
/// 사람이 바로 가기를 다시 누르면 <b>두 번째 앱</b>이 떠 인덱스 파일을 같이 쓰게 된다.
/// 이미 떠 있으면 그쪽에 "네가 나와라" 하고 이 프로세스는 조용히 물러난다 —
/// 그래서 다시 실행하는 것이 곧 창을 되부르는 길이 된다.
/// </para>
/// </summary>
public static class Program
{
    /// <summary>이 앱을 가리키는 이름. 같은 이름을 먼저 잡은 프로세스가 주인이다.</summary>
    private const string InstanceKey = "d-AI-so";

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (HandOverToRunningApp())
        {
            return;
        }

        Microsoft.UI.Xaml.Application.Start(parameters =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            _ = new App();
        });
    }

    /// <summary>이미 떠 있는 앱이 있으면 그쪽에 넘기고 true. 내가 주인이면 false.</summary>
    private static bool HandOverToRunningApp()
    {
        AppActivationArguments arguments;
        AppInstance owner;

        try
        {
            arguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            owner = AppInstance.FindOrRegisterForKey(InstanceKey);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // 이 장치에서 등록을 못 한다. 하나만 뜨게 하는 것을 포기하고 그냥 뜬다 — 안 뜨는 것보다 낫다
            return false;
        }

        if (owner.IsCurrent)
        {
            owner.Activated += (_, _) => App.ShowExistingWindow();
            return false;
        }

        try
        {
            // 넘기는 일은 이 스레드에서 기다리면 서로 붙잡고 멈춘다. 다른 스레드에 맡기고 끝나기만 기다린다
            var done = new ManualResetEvent(false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await owner.RedirectActivationToAsync(arguments);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    // 주인이 방금 사라졌다. 아래에서 그냥 뜬다
                }
                finally
                {
                    done.Set();
                }
            });

            return done.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
