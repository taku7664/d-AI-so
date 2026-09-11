using System.Text;
using Daiso.Infrastructure.Pty;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.3 — 의사 콘솔. 진짜 cmd.exe를 띄우므로 Windows에서만 돈다.</summary>
public sealed class PtySessionTests
{
    [Fact]
    public async Task Output_of_a_child_process_arrives_and_exit_code_is_reported()
    {
        var output = new StringBuilder();
        var gate = new object();

        using var session = PtySession.Start("cmd.exe /c echo pty-hello", Path.GetTempPath(), 80, 24);
        session.OutputReceived += bytes =>
        {
            lock (gate)
            {
                output.Append(Encoding.UTF8.GetString(bytes));
            }
        };

        var code = await session.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));

        code.Should().Be(0);
        // 출력에는 VT 시퀀스가 섞여 있다. 글자만 확인한다
        await WaitUntilAsync(() => { lock (gate) { return output.ToString().Contains("pty-hello", StringComparison.Ordinal); } });
    }

    [Fact]
    public async Task Typed_input_reaches_the_child_and_resize_does_not_throw()
    {
        var output = new StringBuilder();
        var gate = new object();

        using var session = PtySession.Start("cmd.exe /q /k", Path.GetTempPath(), 80, 24);
        session.OutputReceived += bytes =>
        {
            lock (gate)
            {
                output.Append(Encoding.UTF8.GetString(bytes));
            }
        };

        // 프롬프트가 뜰 때까지 잠깐
        await WaitUntilAsync(() => { lock (gate) { return output.Length > 0; } });

        session.Resize(100, 30);
        session.Write("echo typed-in\r");
        await WaitUntilAsync(() => { lock (gate) { return output.ToString().Contains("typed-in", StringComparison.Ordinal); } });

        session.Write("exit\r");
        var code = await session.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));

        code.Should().Be(0);
        session.HasExited.Should().BeTrue();
    }

    [Fact]
    public void A_bad_working_directory_fails_loudly_instead_of_hanging()
    {
        var act = () => PtySession.Start("cmd.exe /c echo x", @"C:\this\folder\does\not\exist\daiso", 80, 24);

        act.Should().Throw<System.ComponentModel.Win32Exception>();
    }

    /// <summary>
    /// 방을 닫으면 <b>남는 프로세스가 없다</b>. 부모가 먼저 죽어 고아가 된 것까지.
    /// <para>
    /// 지금은 작업 개체(Job)가 트리를 잡는다. <b>솔직히 적자면</b> 이 경우는 예전 방식
    /// (<c>Process.Kill(entireProcessTree)</c>)으로도 죽었다 — 의사 콘솔을 닫으면 그 콘솔에 붙은 것이
    /// 같이 끝나기 때문이다. 작업 개체가 더 잡아 주는 것은 <b>콘솔에서 떨어져 나간</b> 프로세스이고,
    /// 그것은 이 자리에서 재현하기 어렵다. 이 테스트는 그 대신 "닫으면 아무것도 안 남는다"를 지킨다
    /// (2026-09-11 점검).
    /// </para>
    /// <para>
    /// 오래 사는 아이로 <c>waitfor</c> 를 쓴다 — Windows 에 늘 있고, 이 이름으로 도는 다른 프로세스가 없어
    /// 살았는지 죽었는지 세기 쉽다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Closing_a_room_takes_a_later_child_with_it()
    {
        var output = new StringBuilder();
        var gate = new object();
        var session = PtySession.Start("cmd.exe /q /k", Path.GetTempPath(), 80, 24);

        try
        {
            session.OutputReceived += bytes =>
            {
                lock (gate)
                {
                    output.Append(Encoding.UTF8.GetString(bytes));
                }
            };

            await WaitUntilAsync(() => { lock (gate) { return output.Length > 0; } });

            // 고아를 만든다: 가운데 cmd 가 waitfor 를 띄우고 바로 끝나므로, 남은 waitfor 에게는
            // 우리 트리로 이어지는 부모가 없다. 스냅숏을 훑는 방식이 놓치던 바로 그 자리다
            session.Write("start /b cmd /c start /b waitfor /t 120 DaisoJobProbe\r");
            await WaitUntilAsync(() => Sleepers() > 0);

            session.Dispose();

            await WaitUntilAsync(() => Sleepers() == 0);
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>지금 도는 <c>waitfor</c> 프로세스 수.</summary>
    private static int Sleepers() => System.Diagnostics.Process.GetProcessesByName("waitfor").Length;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("조건이 20초 안에 참이 되지 않았다");
            }

            await Task.Delay(50);
        }
    }
}
