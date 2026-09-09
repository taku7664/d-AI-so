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

    /// <summary>보여줄 사용량이 있는가.</summary>
    public bool HasData => Days.Any(day => day.Day.Usage.Total > 0);

    /// <summary>기간에 기록이 없어 빈 상태를 보여줄 때.</summary>
    public bool IsEmpty => !HasData;

    /// <summary>사용량은 있는데 단가표가 없어 비용이 0인 상태.</summary>
    public bool NeedsPrices => HasData && EstimatedCost == 0m;

    /// <summary>인덱스에서 사용량을 다시 읽는다.</summary>
    /// <summary>
    /// 몇 번째 읽기인가. <c>IsBusy</c> 를 보고 되돌아가면 안 되기 때문에 둔다:
    /// <c>[RelayCommand]</c> 는 같은 명령을 다시 부르면 앞 실행의 토큰을 끊고 새 실행을 시작하는데,
    /// 그때 <c>IsBusy</c> 는 아직 앞 실행의 <c>true</c> 라서 새 실행이 그대로 돌아가 버렸다.
    /// 탭이나 기간을 바꿔도 화면이 옛 값 그대로 남던 원인이다.
    /// </summary>
    private int loadGeneration;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct)
    {
        var generation = ++loadGeneration;

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

            _summary = await _indexService.Index.GetUsageAsync(from, to, SelectedTool, ct).ConfigureAwait(true);
            Recalculate();

            StatusText = UiStrings.Format("Usage_Range", from, to, Days.Count);
        }
        finally
        {
            // 나를 밀어낸 뒤 실행이 이미 돌고 있으면 그쪽이 끝날 때 끈다
            if (generation == loadGeneration)
            {
                IsBusy = false;
            }
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
        var top = _summary.ByProject
            .OrderByDescending(entry => entry.Value.Total)
            .Take(10)
            .ToList();

        // 이름이 겹치는 프로젝트는 상위 폴더까지 붙여 구분한다 (세션 화면과 같은 규칙)
        var labels = Formats.Labels([.. top.Select(entry => entry.Key)]);

        for (var i = 0; i < top.Count; i++)
        {
            TopProjects.Add(new UsageRowViewModel(
                top[i].Key,
                top[i].Value,
                projectTotal,
                0,
                isPath: true,
                displayName: labels[i]));
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
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(NeedsPrices));
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

    partial void OnPeriodIndexChanged(int value) => UiCommands.Start(LoadCommand);

    /// <summary>탭. 0은 전체, 그 뒤는 <see cref="Services.ToolLook.DisplayOrder"/> 순서의 도구 하나. (요약과 같은 규칙)</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTool))]
    private int selectedTabIndex;

    /// <summary>지금 탭의 도구. 전체면 null.</summary>
    public ToolKind? SelectedTool =>
        SelectedTabIndex >= 1 && SelectedTabIndex <= Services.ToolLook.DisplayOrder.Count
            ? Services.ToolLook.DisplayOrder[SelectedTabIndex - 1]
            : null;

    partial void OnSelectedTabIndexChanged(int value) => UiCommands.Start(LoadCommand);
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

    public string TotalText => Formats.Tokens(Day.Usage.Total);

    public string TotalExact => Formats.Exact(Day.Usage.Total);

    public string Detail =>
        UiStrings.Format(
            "Usage_DayBreakdown",
            Day.Usage.Input,
            Day.Usage.Output,
            Day.Usage.CacheCreate,
            Day.Usage.CacheRead);

    /// <summary>줄 위에 올렸을 때 뜨는 툴팁. 정확한 총합과 내역을 한 곳에 모은다.</summary>
    public string HoverText => TotalExact + Environment.NewLine + Detail;
}

/// <summary>프로젝트별·모델별 한 줄.</summary>
public sealed class UsageRowViewModel
{
    public UsageRowViewModel(
        string label,
        TokenUsage usage,
        long total,
        decimal cost,
        bool isPath = false,
        string? displayName = null)
    {
        Label = label;
        IsPath = isPath;
        DisplayName = displayName;
        Usage = usage;
        Percent = total > 0 ? usage.Total * 100.0 / total : 0;
        Cost = cost;
    }

    public string Label { get; }

    /// <summary>Label이 파일 경로인가 (프로젝트별 목록).</summary>
    public bool IsPath { get; }

    public TokenUsage Usage { get; }

    public double Percent { get; }

    public decimal Cost { get; }

    public string TotalText => Formats.Tokens(Usage.Total);

    public string TotalExact => Formats.Exact(Usage.Total);

    /// <summary>겹치는 이름을 구분한 표시 이름. 없으면 규칙대로 만든다.</summary>
    public string? DisplayName { get; }

    /// <summary>목록에 크게 보일 이름. 경로면 폴더 이름(겹치면 상위 폴더까지).</summary>
    public string PrimaryText => DisplayName ?? (IsPath ? Formats.FolderName(Label) : Label);

    /// <summary>보조 설명. 경로일 때만 전체 경로를 옅게 보여준다.</summary>
    public string SecondaryText => IsPath ? Label : string.Empty;

    /// <summary>보조 설명이 있는가.</summary>
    public bool HasSecondary => SecondaryText.Length > 0;

    public string PercentText => $"{Percent:N1}%";

    public string CostText =>
        Cost > 0 ? UiStrings.Format("Usage_CostEstimate", Cost) : UiStrings.Get("Usage_NoPrice");

    public string ShortLabel => Label.Length <= 60 ? Label : "…" + Label[^60..];
}
