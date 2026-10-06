using System.Collections.Concurrent;
using Daiso.Core;
using Daiso.Host.Tabs.Terminal;

namespace Daiso.Host.Services;

/// <summary>
/// 도구들의 세션 폴더를 지켜보다 바뀐 세션 파일만 인덱스에 다시 읽힌다. 그러면 다른 터미널에서 채팅해도 요약·세션이 따라온다.
/// <para>
/// 대화가 이어지는 동안 파일은 줄마다 바뀐다. 바뀐 파일을 모았다가 조용해진 뒤(<see cref="Quiet"/>) 한 번에 읽고,
/// 계속 바뀌어도 <see cref="MaxWait"/> 를 넘기지 않는다. 바뀐 파일만 읽으므로 전체 갱신(세션 570개 앞부분을 다 읽는다)보다 훨씬 가볍다.
/// </para>
/// </summary>
public sealed class IndexWatcher : IHostedService, IDisposable
{
    /// <summary>마지막 변화 뒤 이만큼 조용하면 읽는다.</summary>
    internal static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1.5);

    /// <summary>변화가 그치지 않아도 첫 변화부터 이만큼 지나면 읽는다.</summary>
    internal static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(6);

    private readonly IEnumerable<IProvider> _providers;
    private readonly IndexService _index;
    private readonly RoomService _rooms;
    private readonly ILogger<IndexWatcher> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();
    private Timer? _timer;
    private DateTimeOffset? _firstChange;

    public IndexWatcher(IEnumerable<IProvider> providers, IndexService index, RoomService rooms, ILogger<IndexWatcher> logger)
    {
        _providers = providers;
        _index = index;
        _rooms = rooms;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

        foreach (var root in _providers.Select(provider => provider.SessionsRoot).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(root, "*.jsonl")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Changed += (_, e) => Note(e.FullPath);
                watcher.Created += (_, e) => Note(e.FullPath);
                watcher.Deleted += (_, e) => Note(e.FullPath);
                watcher.Renamed += (_, e) => Note(e.FullPath);

                // 알림이 넘치면 무엇이 바뀌었는지 모른다. 그때는 전체 갱신에 맡긴다
                watcher.Error += (_, e) =>
                {
                    _logger.LogWarning(e.GetException(), "세션 폴더 지켜보기가 넘쳤다. 전체 갱신을 돌린다");
                    _index.TryStart(rebuild: false);
                };
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "{Root} 를 지켜보지 못했다", root);
            }
        }

        return Task.CompletedTask;
    }

    private void Note(string path)
    {
        _pending[path] = 0;

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            _firstChange ??= now;
            var due = Math.Min(Quiet.TotalMilliseconds, Math.Max(0, (_firstChange.Value + MaxWait - now).TotalMilliseconds));
            _timer?.Change((long)due, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        lock (_gate)
        {
            _firstChange = null;
        }

        var paths = _pending.Keys.ToList();
        foreach (var path in paths)
        {
            _pending.TryRemove(path, out _);
        }

        if (paths.Count > 0 && !_stopping.IsCancellationRequested)
        {
            _ = _index.RefreshFilesAsync(paths, _stopping.Token);

            // 터미널 방의 말풍선도 같은 기록 파일을 읽는다
            _rooms.SessionFilesChanged(paths);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }

        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        _timer?.Dispose();
        _timer = null;
    }
}
