using Daiso.Core;
using Daiso.Host.Notifications;

namespace Daiso.Host.Services;

/// <summary>인덱스 갱신 상태. 화면 아래 줄에 그대로 보여 준다.</summary>
/// <param name="Running">돌고 있는가.</param>
/// <param name="Rebuilding">처음부터 다시 만드는 중인가. false 면 바뀐 것만 이어 읽는다.</param>
/// <param name="Done">끝낸 세션 수. 다시 만들 때만 센다.</param>
/// <param name="Total">전체 세션 수. 모르면 0.</param>
/// <param name="Error">마지막 실행이 실패한 까닭. 성공했으면 null.</param>
/// <param name="FinishedAt">마지막으로 끝난 때. 한 번도 안 돌았으면 null.</param>
public sealed record IndexStatus(bool Running, bool Rebuilding, int Done, int Total, string? Error, DateTimeOffset? FinishedAt)
{
    public static readonly IndexStatus Idle = new(false, false, 0, 0, null, null);
}

/// <summary>
/// 인덱스 갱신을 서버 전체에서 한 번만 돌린다. 옛 앱 <c>IndexService</c>(old/src/Daiso.App/Services)를 옮겼다.
/// 다른 점: 문구를 들고 있지 않고(화면이 정한다), 진행·끝을 <c>/ws</c> 로 알린다.
/// <para>
/// 인덱스는 처음 쓸 때 꺼낸다. 인덱스는 만들 때 DB 를 열어서, DB 가 깨졌으면 꺼내는 데서 던진다 —
/// 그것도 실패로 남겨 화면에 보여 준다(2026-10-06 깨진 사용자 인덱스로 재현).
/// </para>
/// </summary>
public sealed class IndexService : IDisposable
{
    /// <summary>알림의 <c>tab</c> 자리에 넣는 이름.</summary>
    public const string Topic = "index";

    /// <summary>갱신이 끝나면 다시 받아야 하는 화면들. 인덱스를 읽는 곳이다.</summary>
    private static readonly string[] Readers = [Topic, "projects", "usage", "sessions", "dashboard"];

    /// <summary>진행 알림 사이 최소 간격. 세션마다 보내면 화면이 쉬지 않고 다시 받는다.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    private readonly IServiceProvider _services;
    private readonly NotificationHub _hub;
    private readonly ILogger<IndexService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();
    private DateTimeOffset _lastProgress;

    public IndexService(IServiceProvider services, NotificationHub hub, ILogger<IndexService> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>지금 상태.</summary>
    public IndexStatus Status { get; private set; } = IndexStatus.Idle;

    /// <summary>마지막으로 시작한 실행. 시험이 끝나기를 기다릴 때 쓴다.</summary>
    internal Task Current { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// 갱신을 시작한다. 이미 돌고 있으면 아무것도 하지 않고 false 를 돌려준다.
    /// </summary>
    /// <param name="rebuild">처음부터 다시 만들지. false 면 바뀐 파일만 이어 읽는다.</param>
    public bool TryStart(bool rebuild)
    {
        lock (_gate)
        {
            if (Status.Running)
            {
                return false;
            }

            Status = new IndexStatus(true, rebuild, 0, 0, null, Status.FinishedAt);
            Current = Task.Run(() => RunAsync(rebuild, _stopping.Token));
        }

        _ = PublishAsync([Topic]);
        return true;
    }

    private async Task RunAsync(bool rebuild, CancellationToken ct)
    {
        string? error = null;

        try
        {
            var index = _services.GetRequiredService<ISessionIndex>();

            if (rebuild)
            {
                await index.RebuildAsync(new SyncProgress(Report), ct).ConfigureAwait(false);
            }
            else
            {
                await index.RefreshAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // 무엇이 실패하든 서버는 계속 돈다. 까닭은 상태로 남겨 화면에 보인다
            _logger.LogError(ex, "인덱스를 {Kind} 못했다", rebuild ? "다시 만들지" : "갱신하지");
            error = ex.Message;
        }

        lock (_gate)
        {
            Status = new IndexStatus(false, rebuild, Status.Done, Status.Total, error, DateTimeOffset.Now);
        }

        await PublishAsync(Readers).ConfigureAwait(false);
    }

    private void Report(IndexProgress progress)
    {
        lock (_gate)
        {
            Status = Status with { Done = progress.Done, Total = progress.Total };
        }

        var now = DateTimeOffset.Now;
        if (now - _lastProgress < ProgressInterval)
        {
            return;
        }

        _lastProgress = now;
        _ = PublishAsync([Topic]);
    }

    private async Task PublishAsync(IEnumerable<string> topics)
    {
        foreach (var topic in topics)
        {
            await _hub.PublishAsync(new Notification(topic, "changed"), CancellationToken.None).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    /// <summary><see cref="Progress{T}"/> 는 동기화 문맥에 올려 보내 순서가 섞인다. 받은 자리에서 바로 부른다.</summary>
    private sealed class SyncProgress(Action<IndexProgress> report) : IProgress<IndexProgress>
    {
        public void Report(IndexProgress value) => report(value);
    }
}

/// <summary>
/// 서버가 뜨면 인덱스를 한 번 갱신한다. 옛 앱도 뜰 때 갱신했다. 화면은 끝났다는 알림을 받고 다시 받는다.
/// </summary>
public sealed class IndexRefreshOnStart(IndexService index, IHostApplicationLifetime lifetime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => index.TryStart(rebuild: false));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
