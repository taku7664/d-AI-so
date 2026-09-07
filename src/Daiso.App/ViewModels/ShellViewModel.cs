using CommunityToolkit.Mvvm.ComponentModel;
using Daiso.App.Services;

namespace Daiso.App.ViewModels;

/// <summary>Shell 창의 상태바와 첫 실행 인덱싱을 맡는다.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IndexService _indexService;

    /// <summary>상태바 문구.</summary>
    [ObservableProperty]
    private string statusMessage;

    /// <summary>0~100. 0이면 진행률을 모르는 상태다.</summary>
    [ObservableProperty]
    private double statusPercent;

    /// <summary>인덱싱이 돌고 있는지.</summary>
    [ObservableProperty]
    private bool isIndexing;

    public ShellViewModel(IndexService indexService)
    {
        ArgumentNullException.ThrowIfNull(indexService);

        _indexService = indexService;
        statusMessage = IndexStatus.Idle.Display;

        _indexService.StatusChanged += OnStatusChanged;
    }

    /// <summary>UI 스레드로 값을 옮길 때 쓴다. 창이 붙기 전에는 null이다.</summary>
    public Action<Action>? Dispatch { get; set; }

    /// <summary>첫 실행에 백그라운드로 인덱스를 갱신한다. 창을 막지 않는다.</summary>
    public void StartBackgroundRefresh() =>
        _ = Task.Run(() => _indexService.RefreshAsync());

    private void OnStatusChanged(object? sender, IndexStatus status)
    {
        void Apply()
        {
            StatusMessage = status.Display;
            StatusPercent = status.Percent;
            IsIndexing = status.IsRunning;
        }

        if (Dispatch is { } dispatch)
        {
            dispatch(Apply);
        }
        else
        {
            Apply();
        }
    }
}
