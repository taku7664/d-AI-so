using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Infrastructure;

/// <summary>
/// 방 하나가 지금 쓰고 있는 세션 파일을 따라 읽어 새 메시지를 흘린다. (ARCHITECTURE §5.3)
///
/// CLI는 실행하면 자기 세션 루트에 파일을 새로 만든다. 우리는 그 파일 이름을 미리 모르므로
/// "방을 연 시각 이후에 생기거나 자란, 이 프로젝트의 가장 최근 파일"을 활성 세션으로 잡는다.
/// 파일이 바뀔 때마다 provider로 전체 메시지를 다시 읽고, 지난번보다 늘어난 만큼만 새 것으로 넘긴다.
/// </summary>
/// <remarks>
/// 폴링(1초)으로 감시한다. FileSystemWatcher는 네트워크 드라이브·에디터 잠금에서 이벤트를 놓쳐,
/// 초당 한 번 크기·수정시각만 보는 편이 대화 한 턴을 잡는 데 충분하고 더 튼튼하다.
/// </remarks>
public sealed class SessionTail : IDisposable
{
    private readonly IProvider _provider;
    private readonly string _projectDirectory;
    private readonly DateTimeOffset _since;
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _interval;

    /// <summary>
    /// 이 프로젝트 것이 아니라고 이미 판정한 파일. <b>다시 열지 않는다.</b>
    /// 활성 파일을 찾기 전에는 매 초 후보를 훑는데, 판정하려면 파일을 파싱해야 해서
    /// 세션이 여러 개 새로 생기면 같은 파일을 초마다 다시 읽었다 (2026-09-11 점검).
    /// </summary>
    private readonly HashSet<string> _notMine = new(StringComparer.OrdinalIgnoreCase);

    private string? _activeFile;
    private int _delivered;
    private long _lastSize = -1;
    private DateTime _lastWriteUtc;
    private bool _disposed;

    public SessionTail(IProvider provider, string projectDirectory, DateTimeOffset since, TimeSpan? interval = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _projectDirectory = ProjectPathNormalizer.Normalize(projectDirectory) ?? projectDirectory;
        _since = since;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>이 방의 새 메시지. 처음 잡히면 그동안 쌓인 것까지 순서대로. 백그라운드 스레드에서 온다.</summary>
    public event Action<IReadOnlyList<SessionMessage>>? MessagesAppended;

    /// <summary>활성 세션 파일을 처음 찾았을 때. 인자는 파일 경로.</summary>
    public event Action<string>? SessionFound;

    /// <summary>폴링을 시작한다.</summary>
    public void Start()
    {
        _started = true;
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    private bool _started;

    /// <summary>지금 따라가는 세션 파일. 아직 못 찾았으면 null.</summary>
    public string? ActiveFile => _activeFile;

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // 파일이 잠겼거나 사라졌다. 다음 폴에서 다시 본다
            }

            try
            {
                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // 루프가 완전히 빠져나온 뒤에 해제한다. Dispose 가 먼저 해제하면
        // 아직 토큰을 쓰고 있던 이 루프가 ObjectDisposedException 을 맞고, 그것은
        // 아무도 지켜보지 않는 Task 로 사라진다 (2026-09-11 점검)
        _cts.Dispose();
    }

    private async Task TickAsync(CancellationToken ct)
    {
        _activeFile ??= await FindActiveFileAsync(ct).ConfigureAwait(false);
        if (_activeFile is null)
        {
            return;
        }

        var info = new FileInfo(_activeFile);
        if (!info.Exists)
        {
            return;
        }

        // 크기·수정시각이 그대로면 다시 읽지 않는다
        if (info.Length == _lastSize && info.LastWriteTimeUtc == _lastWriteUtc)
        {
            return;
        }

        // 크기·수정시각이 바뀔 때만(=대개 한 턴에 한 번) 여기 온다. 처음부터 다시 읽어 최종 상태를 만든다.
        // Antigravity는 되감기·목록 교체가 있어 반드시 처음부터라야 하고, Claude·Codex도 턴 경계에서만 읽으므로 부담이 크지 않다.
        _lastSize = info.Length;
        _lastWriteUtc = info.LastWriteTimeUtc;

        var all = new List<SessionMessage>();
        await foreach (var message in _provider.ReadMessagesAsync(_activeFile, 0, ct).ConfigureAwait(false))
        {
            all.Add(message);
        }

        if (all.Count <= _delivered)
        {
            // 되감기·목록 교체로 줄었을 수 있다. 기준을 맞추기만 하고 새로 흘리지 않는다
            _delivered = all.Count;
            return;
        }

        var fresh = all.GetRange(_delivered, all.Count - _delivered);
        _delivered = all.Count;
        MessagesAppended?.Invoke(fresh);
    }

    /// <summary>
    /// 이 프로젝트의, 방을 연 뒤 **새로 만들어진** 가장 최근 세션 파일. 생성 시각으로 거른다.
    /// 이미 열려 있던 다른 세션(이 앱을 띄운 세션까지)은 갱신돼도 만들어진 지 오래라 잡히지 않는다.
    /// </summary>
    private async Task<string?> FindActiveFileAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_provider.SessionsRoot))
        {
            return null;
        }

        string? best = null;
        var bestTime = DateTime.MinValue;

        foreach (var file in EnumerateSessionFiles())
        {
            DateTime created;
            try
            {
                created = File.GetCreationTimeUtc(file);
            }
            catch (IOException)
            {
                continue;
            }

            // 방을 연 시각보다 조금 앞선 것까지 허용(파일이 버튼 누름 직전에 만들어질 수 있다)
            if (created < _since.UtcDateTime.AddSeconds(-3) || created <= bestTime)
            {
                continue;
            }

            if (await BelongsToProjectAsync(file, ct).ConfigureAwait(false))
            {
                best = file;
                bestTime = created;
            }
        }

        if (best is not null)
        {
            SessionFound?.Invoke(best);
        }

        return best;
    }

    private IEnumerable<string> EnumerateSessionFiles()
    {
        // 도구마다 루트 아래 계층이 다르다. 하위 전체에서 jsonl을 훑는다
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_provider.SessionsRoot, "*.jsonl", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return files;
    }

    /// <summary>
    /// 파일 하나가 이 프로젝트 것인지. 세션 메타의 프로젝트 경로로 판정한다.
    /// <para>
    /// 아니라고 나온 파일은 기억해 두고 다시 열지 않는다 — 판정은 파일을 파싱하는 일이고,
    /// 이 검사는 활성 파일을 찾을 때까지 <b>매 초</b> 돈다.
    /// </para>
    /// </summary>
    private async Task<bool> BelongsToProjectAsync(string file, CancellationToken ct)
    {
        if (_notMine.Contains(file))
        {
            return false;
        }

        try
        {
            var info = await _provider.ReadSessionInfoAsync(file, ct).ConfigureAwait(false);

            if (ProjectPathNormalizer.AreSame(info.ProjectPath, _projectDirectory))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
        {
            // 아직 반쯤 쓰인 파일이거나 잠겼다. 다음 기회에 다시 본다
            return false;
        }

        _notMine.Add(file);

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();

        // 돌고 있는 루프가 있으면 그 루프가 빠져나올 때 해제한다. 여기서 해제하면 루프가 터진다
        if (!_started)
        {
            _cts.Dispose();
        }
    }
}
