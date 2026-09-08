using System.Diagnostics;
using System.Text;

namespace Daiso.Infrastructure.Pty;

/// <summary>
/// 의사 콘솔 위의 프로세스 하나 = 방 하나의 엔진. 출력은 바이트 덩어리로 흘리고(UTF-8 + VT), 입력은 글자로 받는다.
/// 이벤트는 백그라운드 스레드에서 온다. UI는 자기 디스패처로 옮겨 써야 한다. (ARCHITECTURE §5.3)
/// </summary>
/// <remarks>
/// 파이프 IO는 <see cref="FileStream"/>으로 한다. FileStream이 핸들 수명을 세어, 읽는 중인 핸들을 먼저 닫지 않게 막는다.
/// 손으로 ReadFile/CloseHandle을 부르면 둘이 겹쳐 힙이 깨진다. 종료할 때는 프로세스 **트리 전체**를 끝내야
/// 자식(예: pwsh 아래 node)이 잡고 있는 콘솔 출력 쪽이 닫혀 읽기가 EOF를 본다. 루트만 죽이면 읽기가 안 풀린다.
/// </remarks>
public sealed class PtySession : IDisposable
{
    private readonly PseudoConsole _console;
    private readonly FileStream _output;
    private readonly FileStream _input;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _readDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _subscribeGate = new();
    private List<byte[]>? _pending = [];
    private Action<byte[]>? _outputReceived;
    private DateTime _lastOutputAt = DateTime.UtcNow;
    private bool _disposed;

    private PtySession(PseudoConsole console, string commandLine)
    {
        _console = console;
        CommandLine = commandLine;
        _output = new FileStream(console.Output, FileAccess.Read);
        _input = new FileStream(console.Input, FileAccess.Write);

        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(WaitExitAsync);
    }

    /// <summary>실행한 명령줄. 화면 미리보기·진단용. 토큰이 들어갈 자리는 없다.</summary>
    public string CommandLine { get; }

    public int ProcessId => _console.ProcessId;

    /// <summary>
    /// 화면 출력 한 덩어리. UTF-8 바이트가 글자 중간에서 잘릴 수 있으니 받는 쪽이 이어 붙여 해석한다.
    /// 첫 구독자가 붙기 전에 나온 출력은 모아 두었다가 구독하는 순간 순서대로 넘긴다. 프롬프트 첫 줄을 잃지 않는다.
    /// </summary>
    public event Action<byte[]>? OutputReceived
    {
        add
        {
            List<byte[]>? backlog;

            lock (_subscribeGate)
            {
                _outputReceived += value;
                backlog = _pending;
                _pending = null;
            }

            if (backlog is not null && value is not null)
            {
                foreach (var chunk in backlog)
                {
                    value(chunk);
                }
            }
        }
        remove
        {
            lock (_subscribeGate)
            {
                _outputReceived -= value;
            }
        }
    }

    /// <summary>프로세스가 끝났다. 인자는 종료 코드.</summary>
    public event Action<int>? Exited;

    /// <summary>끝났는지. 종료 코드를 기다리려면 <see cref="WaitForExitAsync"/>.</summary>
    public bool HasExited => _exited.Task.IsCompleted;

    public Task<int> WaitForExitAsync() => _exited.Task;

    /// <summary>
    /// <paramref name="commandLine"/>을 <paramref name="workingDirectory"/>에서 띄운다. 크기는 글자 단위.
    /// </summary>
    public static PtySession Start(string commandLine, string workingDirectory, int columns = 120, int rows = 30)
    {
        var cols = (short)Math.Clamp(columns, 2, short.MaxValue);
        var lines = (short)Math.Clamp(rows, 1, short.MaxValue);
        var console = PseudoConsole.Start(commandLine, workingDirectory, cols, lines, PtyEnvironment.ToBlock(PtyEnvironment.Sanitized()));

        return new PtySession(console, commandLine) { _columns = cols, _rows = lines };
    }

    /// <summary>키 입력·붙여넣기를 콘솔에 넣는다.</summary>
    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0 || _disposed || HasExited)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);

        try
        {
            _input.Write(bytes, 0, bytes.Length);
            _input.Flush();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 프로세스가 막 끝난 뒤의 쓰기. 종료 이벤트가 곧 온다
        }
    }

    /// <summary>크기가 실제로 바뀔 때만 콘솔에 알린다. 같은 크기를 다시 보내면 ConPTY가 화면을 다시 그려 줄이 겹쳐 보인다.</summary>
    public void Resize(int columns, int rows)
    {
        var cols = (short)Math.Clamp(columns, 2, short.MaxValue);
        var lines = (short)Math.Clamp(rows, 1, short.MaxValue);

        if (cols == _columns && lines == _rows)
        {
            return;
        }

        _columns = cols;
        _rows = lines;
        _console.Resize(cols, lines);
    }

    private short _columns;
    private short _rows;

    /// <summary>강제 종료. 방을 닫을 때 프로세스가 아직 살아 있으면 쓴다.</summary>
    public void Kill() => KillTree();

    private void KillTree()
    {
        try
        {
            using var process = Process.GetProcessById(_console.ProcessId);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // 이미 끝났거나 접근 불가. 콘솔 닫기로 마저 정리된다
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[16 * 1024];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // 동기 파이프를 백그라운드에서 읽는다. 프로세스 트리가 끝나 출력 쪽이 닫히면 0(EOF)이 온다
                var read = await Task.Run(() => _output.Read(buffer, 0, buffer.Length)).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                Deliver(buffer[..read]);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // 파이프가 닫혔다. 정상 종료 경로
        }
        finally
        {
            _readDrained.TrySetResult();
        }
    }

    private void Deliver(byte[] chunk)
    {
        _lastOutputAt = DateTime.UtcNow;
        Action<byte[]>? handler;

        lock (_subscribeGate)
        {
            if (_pending is not null)
            {
                _pending.Add(chunk);
                return;
            }

            handler = _outputReceived;
        }

        handler?.Invoke(chunk);
    }

    private async Task WaitExitAsync()
    {
        int code;

        try
        {
            using var process = Process.GetProcessById(_console.ProcessId);
            await process.WaitForExitAsync(_cts.Token).ConfigureAwait(false);
            code = process.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            code = _console.ExitCode ?? -1;
        }

        // conhost는 마지막 화면을 프레임 단위로 늦게 그린다. 출력이 150ms 잠잠해질 때까지(최대 1초) 기다린 뒤 알린다
        var settledAt = DateTime.UtcNow;
        while (!_disposed && DateTime.UtcNow - settledAt < TimeSpan.FromSeconds(1))
        {
            await Task.Delay(50).ConfigureAwait(false);

            if (DateTime.UtcNow - _lastOutputAt >= TimeSpan.FromMilliseconds(150))
            {
                break;
            }
        }

        _exited.TrySetResult(code);
        Exited?.Invoke(code);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 1) 프로세스 트리를 끝낸다. 그래야 자식이 잡은 콘솔 출력 쪽이 닫혀 읽기가 EOF를 본다
        KillTree();

        // 2) 읽기를 취소하고, 루프가 완전히 빠져나온 뒤에만 스트림·핸들을 닫는다. 겹치면 힙이 깨진다
        _cts.Cancel();
        _readDrained.Task.Wait(TimeSpan.FromSeconds(2));

        _output.Dispose();
        _input.Dispose();
        _console.Dispose();
        _cts.Dispose();
    }
}
