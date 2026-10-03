using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Daiso.Providers.Common;

/// <summary>도구의 짧은 조회 명령(예: <c>agy models</c>)을 돌려 표준 출력을 받는다. 테스트에서 갈아끼운다.</summary>
public interface ICommandRunner
{
    /// <summary>끝까지 돌려 표준 출력을 준다. 못 띄웠거나, 0 이 아닌 코드로 끝났거나, 시간이 넘으면 null.</summary>
    Task<string?> RunAsync(string executable, string arguments, TimeSpan timeout, CancellationToken ct);
}

/// <summary>
/// 창 없이 프로세스를 띄운다.
/// <para>
/// 입력은 바로 닫는다 — 무언가를 묻는 도구가 영영 기다리지 않게. 표준 오류도 같이 비운다 — 한쪽 파이프 버퍼가 차면 도구가 멈춘다.
/// 시간이 넘으면 트리째 끝낸다.
/// </para>
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    /// <inheritdoc />
    public async Task<string?> RunAsync(string executable, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process? process;

        try
        {
            process = Process.Start(info);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // 깔려 있지 않거나 PATH 에 없다. 목록이 없는 것뿐이다
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        {
            process.StandardInput.Close();

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);

            try
            {
                var output = process.StandardOutput.ReadToEndAsync(limit.Token);
                var error = process.StandardError.ReadToEndAsync(limit.Token);

                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                var text = await output.ConfigureAwait(false);
                await error.ConfigureAwait(false);

                return process.ExitCode == 0 ? text : null;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // 그새 끝났다
                }

                ct.ThrowIfCancellationRequested();
                return null;
            }
        }
    }
}
