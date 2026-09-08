using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Daiso.Core.Chat;
using Daiso.Infrastructure.Pty;

namespace Daiso.Infrastructure.Chat;

/// <summary>
/// 터미널 없이 Claude를 챗봇처럼 다루는 엔진. `claude --print --output-format stream-json --input-format stream-json
/// --include-partial-messages`를 stdin/stdout JSON으로 잇는다. (FEATURE_PLAN B 모드)
///
/// 사용자 메시지는 JSON 한 줄로 stdin에 쓰고, 답은 stdout에서 줄 단위로 읽어 <see cref="ClaudeStreamParser"/>로 사건을 올린다.
/// 도구 승인은 <see cref="ChatEvent.PermissionRequest"/>가 오면 화면이 허용/거부를 정해 <see cref="Respond"/>로 돌려준다.
/// PTY가 아니라 표준 파이프라서 xterm이 필요 없다. 출력·입력은 화면·메모리에만 두고 파일로 남기지 않는다.
/// </summary>
public sealed class ClaudeChatSession : IChatSession
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeGate = new();
    private bool _disposed;

    private ClaudeChatSession(Process process)
    {
        _process = process;
        _stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };

        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(WaitExitAsync);
    }

    /// <inheritdoc />
    public event Action<ChatEvent>? Event;

    /// <inheritdoc />
    public event Action<int>? Exited;

    /// <inheritdoc />
    public bool HasExited => _process.HasExited;

    /// <summary>
    /// 방을 연다. <paramref name="executable"/>은 보통 셸로 감싼 claude(예: pwsh -Command). 인자는 stream-json 고정.
    /// </summary>
    public static ClaudeChatSession Start(string executable, string arguments, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var info = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        // 중첩 Claude Code 세션 안에서 앱이 떠도 안의 claude가 하위 세션으로 오작동하지 않게 표식을 걷어낸다
        info.Environment.Clear();
        foreach (var (key, value) in PtyEnvironment.Sanitized())
        {
            info.Environment[key] = value;
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = false };
        process.Start();

        return new ClaudeChatSession(process);
    }

    /// <inheritdoc />
    public void Send(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || _disposed || HasExited)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content = new[] { new { type = "text", text } } },
        });

        WriteLine(payload);
    }

    /// <inheritdoc />
    public void Respond(string requestId, bool allow)
    {
        if (string.IsNullOrEmpty(requestId) || _disposed || HasExited)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "control_response",
            response = new
            {
                subtype = "success",
                request_id = requestId,
                response = new { behavior = allow ? "allow" : "deny", message = allow ? "허용됨" : "거부됨" },
            },
        });

        WriteLine(payload);
    }

    private void WriteLine(string payload)
    {
        lock (_writeGate)
        {
            try
            {
                _stdin.WriteLine(payload);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // 프로세스가 막 끝났다. 종료 이벤트가 곧 온다
            }
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                foreach (var chatEvent in ClaudeStreamParser.Parse(line))
                {
                    if (chatEvent is not ChatEvent.Ignored)
                    {
                        Event?.Invoke(chatEvent);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // 파이프가 닫혔다. 정상 종료 경로
        }
    }

    private async Task WaitExitAsync()
    {
        try
        {
            await _process.WaitForExitAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var code = _process.HasExited ? _process.ExitCode : -1;
        Exited?.Invoke(code);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // 이미 끝났다
        }

        _stdin.Dispose();
        _process.Dispose();
        _cts.Dispose();
    }
}
