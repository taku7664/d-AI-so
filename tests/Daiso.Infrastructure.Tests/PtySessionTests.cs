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
