using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.Infrastructure;
using Daiso.Providers.Common;

namespace Daiso.App.ViewModels;

/// <summary>설정. 세션 루트·정리 규칙·단가표·테마·인덱스 재구축. (GOAL Step 15)</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IndexService _indexService;

    [ObservableProperty]
    private string? sessionHomeOverride;

    [ObservableProperty]
    private string? indexDatabasePath;

    [ObservableProperty]
    private int cleanupOlderThanDays;

    [ObservableProperty]
    private int cleanupLargerThanMegabytes;

    [ObservableProperty]
    private int themeIndex;

    [ObservableProperty]
    private string? statusText;

    public SettingsViewModel(ISettingsStore settings, IndexService indexService)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(indexService);

        _settings = settings;
        _indexService = indexService;

        Load();
    }

    /// <summary>테마 항목.</summary>
    public IReadOnlyList<string> Themes { get; } = ["시스템", "라이트", "다크"];

    /// <summary>모델별 단가.</summary>
    public ObservableCollection<PriceRowViewModel> Prices { get; } = [];

    /// <summary>세션 루트를 비웠을 때 쓰이는 기본값.</summary>
    public string DefaultSessionHome => ProviderHome.FromUserProfile().Directory;

    /// <summary>인덱스 경로를 비웠을 때 쓰이는 기본값.</summary>
    public string DefaultIndexDatabasePath => SqliteSessionIndex.DefaultDatabasePath;

    /// <summary>설정 파일 위치.</summary>
    public string SettingsFilePath => (_settings as SettingsStore)?.Path ?? "(알 수 없음)";

    /// <summary>세션 루트나 인덱스 경로를 바꾸면 다시 시작해야 적용된다.</summary>
    public string RestartNotice =>
        "세션 루트와 인덱스 경로는 앱을 다시 시작하면 적용된다. 나머지는 즉시 적용된다.";

    /// <summary>파일에서 값을 다시 읽는다.</summary>
    [RelayCommand]
    public void Load()
    {
        var current = _settings.Current;

        SessionHomeOverride = current.SessionHomeOverride;
        IndexDatabasePath = current.IndexDatabasePath;
        CleanupOlderThanDays = current.CleanupOlderThanDays;
        CleanupLargerThanMegabytes = current.CleanupLargerThanMegabytes;
        ThemeIndex = current.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };

        Prices.Clear();

        foreach (var (model, price) in current.Prices.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            Prices.Add(PriceRowViewModel.From(model, price));
        }
    }

    /// <summary>값을 설정 파일에 쓴다.</summary>
    [RelayCommand]
    public void Save()
    {
        var current = _settings.Current;

        current.SessionHomeOverride = Blank(SessionHomeOverride);
        current.IndexDatabasePath = Blank(IndexDatabasePath);
        current.CleanupOlderThanDays = Math.Max(0, CleanupOlderThanDays);
        current.CleanupLargerThanMegabytes = Math.Max(0, CleanupLargerThanMegabytes);
        current.Theme = ThemeIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System",
        };

        current.Prices.Clear();

        foreach (var row in Prices.Where(row => !string.IsNullOrWhiteSpace(row.Model)))
        {
            current.Prices[row.Model.Trim()] = row.ToPrice();
        }

        _settings.Save();
        StatusText = $"{SettingsFilePath} 에 저장했다";
    }

    /// <summary>단가 행을 추가한다.</summary>
    [RelayCommand]
    public void AddPrice() => Prices.Add(new PriceRowViewModel());

    /// <summary>단가 행을 지운다.</summary>
    [RelayCommand]
    public void RemovePrice(PriceRowViewModel? row)
    {
        if (row is not null)
        {
            Prices.Remove(row);
        }
    }

    /// <summary>인덱스를 처음부터 다시 만든다.</summary>
    [RelayCommand]
    public async Task RebuildIndexAsync()
    {
        StatusText = "인덱스를 다시 만든다 …";
        await _indexService.RebuildAsync().ConfigureAwait(true);
        StatusText = _indexService.Status.Display;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>모델 하나의 단가 편집 행. 100만 토큰당 USD.</summary>
public sealed partial class PriceRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string model = string.Empty;

    [ObservableProperty]
    private double inputPerMillion;

    [ObservableProperty]
    private double outputPerMillion;

    [ObservableProperty]
    private double cacheWritePerMillion;

    [ObservableProperty]
    private double cacheReadPerMillion;

    internal static PriceRowViewModel From(string model, ModelPrice price) => new()
    {
        Model = model,
        InputPerMillion = (double)price.InputPerMillion,
        OutputPerMillion = (double)price.OutputPerMillion,
        CacheWritePerMillion = (double)price.CacheWritePerMillion,
        CacheReadPerMillion = (double)price.CacheReadPerMillion,
    };

    internal ModelPrice ToPrice() => new()
    {
        InputPerMillion = (decimal)InputPerMillion,
        OutputPerMillion = (decimal)OutputPerMillion,
        CacheWritePerMillion = (decimal)CacheWritePerMillion,
        CacheReadPerMillion = (decimal)CacheReadPerMillion,
    };
}
