using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Daiso.Providers.Manifest;

/// <summary>
/// 어댑터 프로세스 하나와 주고받는 통로 (docs/PLUGIN_PLAN.md Stage 5).
/// <para>
/// <b>오래 사는 프로세스 하나다.</b> 파일마다 띄우면 세션 수백 개를 훑는 데 프로세스 수백 개가 뜬다.
/// 요청을 한 줄 써 보내고 <c>done</c> 이 올 때까지 줄을 읽는다.
/// </para>
/// <para>
/// <b>죽어도 앱은 산다.</b> 시간이 넘거나 프로세스가 꺼지면 그 요청만 실패하고
/// <see cref="LastError"/> 에 이유가 남는다 — 설정 화면이 그것을 보여 준다(Stage 6).
/// </para>
/// </summary>
public sealed class AdapterChannel : IDisposable
{
    /// <summary>
    /// <b>한 줄</b>이 이만큼 안에 와야 한다. 멈춘 어댑터가 화면을 붙잡지 못하게 한다.
    /// <para>
    /// 요청 전체에 걸던 때는 세션이 크면 멀쩡히 흘려보내던 어댑터가 30초에 잘렸다.
    /// 기다리는 이유는 "답이 오지 않는 것"이지 "답이 많은 것"이 아니다.
    /// </para>
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>어댑터가 stderr 에 쏟은 말 중 남겨 둘 길이. 오류 문구에 꼬리로 붙인다.</summary>
    private const int ErrorTailLength = 2000;

    /// <summary>이유를 적기 전에 stderr 가 다 들어오기를 기다리는 시간. 다른 스레드로 오기 때문이다.</summary>
    private static readonly TimeSpan StderrGrace = TimeSpan.FromSeconds(2);

    private readonly string _command;
    private readonly string _workingDirectory;
    private readonly SemaphoreSlim _turn = new(1, 1);

    private readonly object _errorGate = new();
    private readonly StringBuilder _errorTail = new();

    private Process? _process;
    private bool _disposed;

    /// <param name="command">띄울 명령 한 줄. 첫 토막이 실행 파일, 나머지가 인자다.</param>
    /// <param name="workingDirectory">그 프로세스의 작업 폴더. 매니페스트가 있는 곳이다.</param>
    public AdapterChannel(string command, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        _command = command;
        _workingDirectory = workingDirectory;
    }

    /// <summary>마지막으로 어긋난 이유. 없으면 null. 어댑터가 stderr 에 남긴 말이 있으면 뒤에 붙는다.</summary>
    public string? LastError { get; private set; }

    /// <summary>어댑터가 stderr 에 남긴 마지막 말. 플러그인을 만드는 사람이 볼 유일한 단서다.</summary>
    public string? ErrorOutput
    {
        get
        {
            lock (_errorGate)
            {
                return _errorTail.Length == 0 ? null : _errorTail.ToString();
            }
        }
    }

    /// <summary>어댑터가 <c>hello</c> 에서 알려 준 이름.</summary>
    public string? AdapterName { get; private set; }

    /// <summary>어댑터가 <c>hello</c> 에서 알려 준 값. 매니페스트의 <c>appendOnly</c> 를 덮어쓴다.</summary>
    public bool? AppendOnly { get; private set; }

    /// <summary>말이 통하는지 한 번 물어본다. 판이 다르면 여기서 걸린다.</summary>
    public async Task<bool> HandshakeAsync(CancellationToken ct)
    {
        await foreach (var line in SendAsync(new AdapterRequest(AdapterProtocol.Version, "hello"), ct))
        {
            if (line.V != 0 && line.V != AdapterProtocol.Version)
            {
                LastError = $"어댑터가 모르는 판으로 답한다: v{line.V} (이 앱은 v{AdapterProtocol.Version})";
                return false;
            }

            AdapterName ??= line.Name;
            AppendOnly ??= line.AppendOnly;
        }

        return LastError is null;
    }

    /// <summary>요청 한 줄을 보내고 <c>done</c> 까지의 답을 흘려보낸다.</summary>
    public async IAsyncEnumerable<AdapterResponse> SendAsync(
        AdapterRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 통로가 하나뿐이라 요청이 겹치면 답이 섞인다. 한 번에 하나만 보낸다
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        var finished = false;

        try
        {
            var process = Start();

            if (process is null)
            {
                finished = true;
                yield break;
            }

            await process.StandardInput
                .WriteLineAsync(JsonSerializer.Serialize(request, AdapterProtocol.Json).AsMemory(), ct)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);

            while (true)
            {
                var line = await ReadLineAsync(process, ct).ConfigureAwait(false);

                if (line is null)
                {
                    Fail("어댑터가 답을 끝내기 전에 닫혔다");
                    Kill();
                    finished = true;
                    yield break;
                }

                AdapterResponse? parsed;

                try
                {
                    parsed = JsonSerializer.Deserialize<AdapterResponse>(line, AdapterProtocol.Json);
                }
                catch (JsonException ex)
                {
                    Fail($"어댑터가 JSON 이 아닌 줄을 보냈다: {ex.Message}");
                    Kill();
                    finished = true;
                    yield break;
                }

                if (parsed is null)
                {
                    continue;
                }

                if (parsed.Error is { Length: > 0 } error)
                {
                    Fail(error);
                    finished = true;
                    yield break;
                }

                yield return parsed;

                if (parsed.Done)
                {
                    finished = true;
                    yield break;
                }
            }
        }
        finally
        {
            // 받는 쪽이 `done` 전에 그만뒀으면(상한에 걸려 break 한다) 남은 줄이 통로에 그대로 있다.
            // 그대로 두면 <b>다음 요청의 답에 섞인다</b>. 통로를 접어 다음에 새로 띄운다
            if (!finished)
            {
                Kill();
            }

            _turn.Release();
        }
    }

    /// <summary>어긋난 이유를 적는다. 어댑터가 stderr 에 남긴 말이 있으면 꼬리로 붙인다.</summary>
    private void Fail(string reason)
    {
        // stderr 는 다른 스레드로 온다. 이미 끝난 프로세스라면 남은 줄이 다 들어오기를 잠깐 기다린다 —
        // 인자 있는 WaitForExit 은 비동기 읽기가 끝나는 것까지 같이 기다려 준다
        if (_process is { HasExited: true } process)
        {
            process.WaitForExit((int)StderrGrace.TotalMilliseconds);
        }

        LastError = ErrorOutput is { Length: > 0 } tail ? $"{reason}\n{tail}" : reason;
    }

    /// <summary>
    /// 한 줄 읽기에 시간 제한을 건다. <c>ReadLineAsync</c> 는 토큰을 받지만
    /// 프로세스가 아무것도 안 쓰고 살아 있으면 그대로 매달린다 — 넘으면 죽인다.
    /// </summary>
    private async Task<string?> ReadLineAsync(Process process, CancellationToken ct)
    {
        // 기다림은 줄마다 새로 잰다. 답이 계속 오는 동안에는 얼마든지 오래 걸려도 된다
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);

        try
        {
            return await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Fail($"어댑터가 {Timeout.TotalSeconds:0} 초 안에 한 줄도 보내지 않았다");
            Kill();
            return null;
        }
    }

    private Process? Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_process is { HasExited: false })
        {
            return _process;
        }

        lock (_errorGate)
        {
            _errorTail.Clear();
        }

        var (file, arguments) = Split(_command);

        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                WorkingDirectory = Directory.Exists(_workingDirectory) ? _workingDirectory : string.Empty,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            });

            if (_process is null)
            {
                LastError = $"어댑터를 띄우지 못했다: {file}";

                return null;
            }

            // stderr 를 <b>반드시 읽어야 한다</b>. 리다이렉트해 놓고 읽지 않으면 파이프가 차서
            // 어댑터가 쓰다가 멈추고, 우리는 그것을 "답이 없다"로 잘못 읽는다
            _process.ErrorDataReceived += OnErrorLine;
            _process.BeginErrorReadLine();

            return _process;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            LastError = $"어댑터를 띄우지 못했다: {ex.Message}";
            _process = null;
            return null;
        }
    }

    /// <summary>따옴표로 감싼 경로를 살려 첫 토막과 나머지로 가른다.</summary>
    private static (string File, string Arguments) Split(string command)
    {
        var trimmed = command.Trim();

        if (trimmed.StartsWith('"'))
        {
            var close = trimmed.IndexOf('"', 1);

            return close < 0
                ? (trimmed.Trim('"'), string.Empty)
                : (trimmed[1..close], trimmed[(close + 1)..].Trim());
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? (trimmed, string.Empty) : (trimmed[..space], trimmed[(space + 1)..]);
    }

    /// <summary>어댑터가 stderr 에 남긴 말. 뒤쪽만 남긴다 — 앞을 버리는 쪽이 원인에 가깝다.</summary>
    private void OnErrorLine(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is not { Length: > 0 } line)
        {
            return;
        }

        lock (_errorGate)
        {
            _errorTail.AppendLine(line);

            if (_errorTail.Length > ErrorTailLength)
            {
                _errorTail.Remove(0, _errorTail.Length - ErrorTailLength);
            }
        }
    }

    private void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // 이미 죽었다. 더 할 일이 없다
        }
        finally
        {
            if (_process is { } process)
            {
                process.ErrorDataReceived -= OnErrorLine;
                process.Dispose();
            }

            _process = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Kill();
        _turn.Dispose();
    }
}
