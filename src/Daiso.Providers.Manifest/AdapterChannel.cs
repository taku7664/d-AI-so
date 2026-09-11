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
    /// <summary>한 요청이 이만큼 안에 끝나야 한다. 멈춘 어댑터가 화면을 붙잡지 못하게 한다.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string _command;
    private readonly string _workingDirectory;
    private readonly SemaphoreSlim _turn = new(1, 1);

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

    /// <summary>마지막으로 어긋난 이유. 없으면 null.</summary>
    public string? LastError { get; private set; }

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

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);

        try
        {
            var process = Start();

            if (process is null)
            {
                yield break;
            }

            await process.StandardInput
                .WriteLineAsync(JsonSerializer.Serialize(request, AdapterProtocol.Json).AsMemory(), deadline.Token)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);

            while (true)
            {
                var line = await ReadLineAsync(process, deadline.Token).ConfigureAwait(false);

                if (line is null)
                {
                    LastError ??= "어댑터가 답을 끝내기 전에 닫혔다";
                    Kill();
                    yield break;
                }

                AdapterResponse? parsed;

                try
                {
                    parsed = JsonSerializer.Deserialize<AdapterResponse>(line, AdapterProtocol.Json);
                }
                catch (JsonException ex)
                {
                    LastError = $"어댑터가 JSON 이 아닌 줄을 보냈다: {ex.Message}";
                    Kill();
                    yield break;
                }

                if (parsed is null)
                {
                    continue;
                }

                if (parsed.Error is { Length: > 0 } error)
                {
                    LastError = error;
                    yield break;
                }

                yield return parsed;

                if (parsed.Done)
                {
                    yield break;
                }
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// 한 줄 읽기에 시간 제한을 건다. <c>ReadLineAsync</c> 는 토큰을 받지만
    /// 프로세스가 아무것도 안 쓰고 살아 있으면 그대로 매달린다 — 넘으면 죽인다.
    /// </summary>
    private async Task<string?> ReadLineAsync(Process process, CancellationToken ct)
    {
        try
        {
            return await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LastError = $"어댑터가 {Timeout.TotalSeconds:0} 초 안에 답하지 않았다";
            Kill();
            return null;
        }
    }

    private Process? Start()
    {
        if (_process is { HasExited: false })
        {
            return _process;
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
            });

            if (_process is null)
            {
                LastError = $"어댑터를 띄우지 못했다: {file}";
            }

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
            _process?.Dispose();
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
