using Daiso.Core;
using Daiso.App.Strings;

namespace Daiso.App.Services;

/// <summary>
/// 인덱스 갱신을 앱 전체에서 한 번만 돌리고 진행률을 알린다.
/// 오래 걸리는 작업이라 UI 스레드를 막지 않는다. (ARCHITECTURE §6)
/// </summary>
public sealed class IndexService
{
    private readonly ISessionIndex _index;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IndexService(ISessionIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        _index = index;
    }

    /// <summary>진행 상황이 바뀔 때마다 발생한다. UI 스레드가 아닐 수 있다.</summary>
    public event EventHandler<IndexStatus>? StatusChanged;

    /// <summary>마지막으로 알려진 상태.</summary>
    public IndexStatus Status { get; private set; } = IndexStatus.Idle;

    /// <summary>인덱스 본체. 화면에서 목록·검색·사용량을 물어본다.</summary>
    public ISessionIndex Index => _index;

    /// <summary>바뀐 세션만 이어 읽는다. 이미 돌고 있으면 그대로 둔다.</summary>
    public Task RefreshAsync(CancellationToken ct = default) =>
        RunAsync(UiStrings.Get("Index_RefreshLabel"), (progress, token) => _index.RefreshAsync(token), ct);

    /// <summary>처음부터 다시 만든다.</summary>
    public Task RebuildAsync(CancellationToken ct = default) =>
        RunAsync(
            UiStrings.Get("Index_RebuildLabel"),
            (progress, token) => _index.RebuildAsync(progress, token),
            ct);

    private async Task RunAsync(
        string label,
        Func<IProgress<IndexProgress>, CancellationToken, Task> work,
        CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            Report(new IndexStatus(true, label, 0, 0, null));

            var progress = new Progress<IndexProgress>(value =>
                Report(new IndexStatus(true, label, value.Done, value.Total, value.CurrentFile)));

            await work(progress, ct).ConfigureAwait(false);

            Report(new IndexStatus(false, UiStrings.Format("Index_Done", label), 0, 0, null));
        }
        catch (OperationCanceledException)
        {
            Report(new IndexStatus(false, UiStrings.Format("Index_Canceled", label), 0, 0, null));
        }
        catch (Exception ex)
        {
            Report(new IndexStatus(false, UiStrings.Format("Index_Failed", label, ex.Message), 0, 0, null));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Report(IndexStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}

/// <summary>상태바에 그대로 보여줄 인덱스 진행 상황.</summary>
/// <param name="IsRunning">돌고 있는지.</param>
/// <param name="Message">상태 문구.</param>
/// <param name="Done">끝난 개수.</param>
/// <param name="Total">전체 개수. 0이면 알 수 없음.</param>
/// <param name="CurrentFile">지금 읽는 파일.</param>
public sealed record IndexStatus(bool IsRunning, string Message, int Done, int Total, string? CurrentFile)
{
    public static readonly IndexStatus Idle = new(false, UiStrings.Get("Index_Idle"), 0, 0, null);

    /// <summary>0~100. 전체 개수를 모르면 0.</summary>
    public double Percent => Total > 0 ? Math.Min(100, Done * 100.0 / Total) : 0;

    /// <summary>진행 개수를 붙인 표시 문구.</summary>
    public string Display => Total > 0 ? $"{Message} ({Done}/{Total})" : Message;
}
