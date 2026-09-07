using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.Core;
using Daiso.App.Strings;

namespace Daiso.App.ViewModels;

/// <summary>토큰 사용량 통계. 비용은 사용자 단가표 기반 "추정". (REQUIREMENTS §7, GOAL Step 14)</summary>
public sealed partial class UsageViewModel : ObservableObject
{
    /// <summary>막대 그래프의 최대 너비(px).</summary>
    private const double BarMaxWidth = 260;

    private readonly IndexService _indexService;
    private readonly ISettingsStore _settings;

    private UsageSummary _summary = new([], new Dictionary<string, TokenUsage>(), new Dictionary<string, TokenUsage>());

    [ObservableProperty]
    private int periodIndex;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private decimal estimatedCost;

    public UsageViewModel(IndexService indexService, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(indexService);
        ArgumentNullException.ThrowIfNull(settings);

        _indexService = indexService;
        _settings = settings;

        // 단가표를 바꾸면 비용을 즉시 다시 계산한다.
        _settings.Changed += (_, _) => Recalculate();
    }

    /// <summary>기간 선택 항목.</summary>
    public IReadOnlyList<string> Periods { get; } =
    [
        UiStrings.Get("Usage_Period7"),
        UiStrings.Get("Usage_Period30"),
        UiStrings.All,
    ];

    /// <summary>일별 막대.</summary>
    public ObservableCollection<UsageDayViewModel> Days { get; } = [];

    /// <summary>프로젝트별 상위 10.</summary>
    public ObservableCollection<UsageRowViewModel> TopProjects { get; } = [];

    /// <summary>모델별 비율.</summary>
    public ObservableCollection<UsageRowViewModel> ByModel { get; } = [];

    /// <summary>비용은 항상 추정임을 밝힌다.</summary>
    public string EstimatedCostText => UiStrings.Format("Usage_EstimatedCost", EstimatedCost);

    /// <summary>인덱스에서 사용량을 다시 읽는다.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var to = DateOnly.FromDateTime(DateTime.UtcNow);
            var from = PeriodIndex switch
            {
                0 => to.AddDays(-6),
                1 => to.AddDays(-29),
                _ => new DateOnly(2000, 1, 1),
            };

            _summary = await _indexService.Index.GetUsageAsync(from, to, ct).ConfigureAwait(true);
            Recalculate();

            StatusText = UiStrings.Format("Usage_Range", from, to, Days.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>읽어 둔 데이터로 표와 비용을 다시 만든다.</summary>
    public void Recalculate()
    {
        Days.Clear();
        TopProjects.Clear();
        ByModel.Clear();

        var maxDayTotal = _summary.Days.Count > 0 ? _summary.Days.Max(day => day.Usage.Total) : 0;

        foreach (var day in _summary.Days)
        {
            var width = maxDayTotal > 0 ? day.Usage.Total * BarMaxWidth / maxDayTotal : 0;
            Days.Add(new UsageDayViewModel(day, width));
        }

        var projectTotal = _summary.ByProject.Sum(entry => entry.Value.Total);

        foreach (var entry in _summary.ByProject.OrderByDescending(entry => entry.Value.Total).Take(10))
        {
            TopProjects.Add(new UsageRowViewModel(entry.Key, entry.Value, projectTotal, 0));
        }

        var modelTotal = _summary.ByModel.Sum(entry => entry.Value.Total);
        var cost = 0m;

        foreach (var entry in _summary.ByModel.OrderByDescending(entry => entry.Value.Total))
        {
            var modelCost = CostOf(entry.Key, entry.Value);
            cost += modelCost;
            ByModel.Add(new UsageRowViewModel(entry.Key, entry.Value, modelTotal, modelCost));
        }

        EstimatedCost = cost;
        OnPropertyChanged(nameof(EstimatedCostText));
    }

    /// <summary>단가표에 없는 모델은 0으로 둔다. 없는 값을 추측하지 않는다.</summary>
    private decimal CostOf(string model, TokenUsage usage)
    {
        if (!_settings.Current.Prices.TryGetValue(model, out var price))
        {
            return 0m;
        }

        const decimal Million = 1_000_000m;

        return (usage.Input / Million * price.InputPerMillion)
            + (usage.Output / Million * price.OutputPerMillion)
            + (usage.CacheCreate / Million * price.CacheWritePerMillion)
            + (usage.CacheRead / Million * price.CacheReadPerMillion);
    }

    partial void OnPeriodIndexChanged(int value) => _ = LoadCommand.ExecuteAsync(null);
}

/// <summary>일별 막대 한 줄.</summary>
public sealed class UsageDayViewModel
{
    public UsageDayViewModel(UsageDay day, double barWidth)
    {
        Day = day;
        BarWidth = barWidth;
    }

    public UsageDay Day { get; }

    public double BarWidth { get; }

    public string DateText => Day.Date.ToString("MM-dd");

    public string TotalText => $"{Day.Usage.Total:N0}";

    public string Detail =>
        UiStrings.Format(
            "Usage_DayBreakdown",
            Day.Usage.Input,
            Day.Usage.Output,
            Day.Usage.CacheCreate,
            Day.Usage.CacheRead);
}

/// <summary>프로젝트별·모델별 한 줄.</summary>
public sealed class UsageRowViewModel
{
    public UsageRowViewModel(string label, TokenUsage usage, long total, decimal cost)
    {
        Label = label;
        Usage = usage;
        Percent = total > 0 ? usage.Total * 100.0 / total : 0;
        Cost = cost;
    }

    public string Label { get; }

    public TokenUsage Usage { get; }

    public double Percent { get; }

    public decimal Cost { get; }

    public string TotalText => $"{Usage.Total:N0}";

    public string PercentText => $"{Percent:N1}%";

    public string CostText =>
        Cost > 0 ? UiStrings.Format("Usage_CostEstimate", Cost) : UiStrings.Get("Usage_NoPrice");

    public string ShortLabel => Label.Length <= 60 ? Label : "…" + Label[^60..];
}
