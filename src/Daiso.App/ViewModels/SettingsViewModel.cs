using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.Infrastructure;
using Daiso.Providers.Common;
using Daiso.App.Strings;

namespace Daiso.App.ViewModels;

/// <summary>설정. 세션 루트·정리 규칙·단가표·테마·인덱스 재구축. (GOAL Step 15)</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IndexService _indexService;
    private readonly IndexLocation _indexLocation;

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

    /// <summary>내장 터미널 글자 크기(px).</summary>
    [ObservableProperty]
    private int terminalFontSize;

    /// <summary>이 PC에 WebView2 런타임이 있는가. 없으면 내장 터미널 대신 외부 터미널로 연다고 안내한다.</summary>
    public bool WebViewAvailable { get; } = Daiso.App.Terminal.TerminalHost.IsRuntimeAvailable();

    public Microsoft.UI.Xaml.Visibility NoWebViewVisibility =>
        WebViewAvailable ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    [ObservableProperty]
    private string? statusText;

    private readonly Services.ToolPluginCatalog _plugins;

    public SettingsViewModel(
        ISettingsStore settings,
        IndexService indexService,
        Services.ToolPluginCatalog plugins,
        IndexLocation indexLocation)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(indexService);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(indexLocation);

        _settings = settings;
        _indexService = indexService;
        _plugins = plugins;
        _indexLocation = indexLocation;

        Load();
        RefreshPlugins();
    }

    // ── 도구 플러그인 (docs/PLUGIN_PLAN.md Stage 6) ────────────────────────

    /// <summary>읽은 것과 못 읽은 것 전부. 실패를 감추지 않는다.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ToolPluginRowViewModel> Plugins { get; } = [];

    /// <summary>매니페스트를 놓는 자리. "여기를 봅니다"로 그대로 보여 준다.</summary>
    public string PluginDirectory => _plugins.Directory;

    /// <summary>플러그인이 하나도 없는가. 안 쓰는 사람에게는 안내 한 줄만 보인다.</summary>
    public bool HasNoPlugins => Plugins.Count == 0;

    public bool HasPlugins => Plugins.Count > 0;

    /// <summary>
    /// 다시 읽은 뒤 <b>앱을 다시 켜야 한다고 말한다</b>. 이미 실린 도구는 앱이 뜰 때 물어 둔 것이라
    /// 목록만 새로 읽는다고 화면의 도구가 늘지 않는다 — 조용히 안 늘면 "왜 안 되지"가 된다.
    /// </summary>
    [RelayCommand]
    public void ReloadPlugins()
    {
        _plugins.Reload();
        RefreshPlugins();
        StatusText = UiStrings.Get("Plugins_ReloadedNeedsRestart");
    }

    private void RefreshPlugins()
    {
        Plugins.Clear();

        foreach (var load in _plugins.Loads)
        {
            Plugins.Add(new ToolPluginRowViewModel(load));
        }

        OnPropertyChanged(nameof(HasNoPlugins));
        OnPropertyChanged(nameof(HasPlugins));
    }

    /// <summary>테마를 고르면 창이 바로 갈아입도록 알린다. 값은 "System" | "Light" | "Dark".</summary>
    public event EventHandler<string>? ThemeChanged;

    /// <summary>테마 항목.</summary>
    public IReadOnlyList<string> Themes { get; } =
    [
        UiStrings.Get("Settings_ThemeSystem"),
        UiStrings.Get("Settings_ThemeLight"),
        UiStrings.Get("Settings_ThemeDark"),
    ];

    /// <summary>모델별 단가.</summary>
    public ObservableCollection<PriceRowViewModel> Prices { get; } = [];

    /// <summary>단가 줄이 하나라도 있는가.</summary>
    public bool HasPrices => Prices.Count > 0;

    /// <summary>단가가 비었는가. 안내 문구를 띄운다.</summary>
    public bool HasNoPrices => Prices.Count == 0;

    /// <summary>세션 루트를 비웠을 때 쓰이는 기본값.</summary>
    public string DefaultSessionHome => ProviderHome.FromUserProfile().Directory;

    /// <summary>인덱스 경로를 비웠을 때 쓰이는 기본값.</summary>
    public string DefaultIndexDatabasePath => SqliteSessionIndex.DefaultDatabasePath;

    /// <summary>
    /// 설정한 인덱스 경로를 못 써서 기본 경로로 열었을 때의 사정. 없으면 null.
    /// 조용히 다른 곳을 쓰면 사람은 인덱스가 왜 비었는지 알 길이 없다.
    /// </summary>
    public string? IndexFallbackNotice => _indexLocation.Failure is { } failure
        ? UiStrings.Format("Settings_IndexFallback", _indexLocation.Requested ?? string.Empty, failure)
        : null;

    /// <summary>물러선 안내를 띄우는가.</summary>
    public bool HasIndexFallback => IndexFallbackNotice is not null;

    /// <summary>설정 파일 위치.</summary>
    public string SettingsFilePath =>
        (_settings as SettingsStore)?.Path ?? UiStrings.Get("Common_Unknown");

    /// <summary>앱 판 번호. 정보 카드에 쓴다.</summary>
    public string AppVersion => UiStrings.Format(
        "About_Version",
        typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    /// <summary>로그인 프로필 보관 위치. 무엇이 이 PC 어디에 남는지 밝힌다.</summary>
    public string ProfilesPath => AuthProfileStore.DefaultRoot;

    /// <summary>세션 루트나 인덱스 경로를 바꾸면 다시 시작해야 적용된다.</summary>
    public string RestartNotice =>
        UiStrings.Get("Settings_RestartNotice");

    /// <summary>파일에서 값을 다시 읽는다.</summary>
    [RelayCommand]
    public void Load()
    {
        var current = _settings.Current;

        SessionHomeOverride = current.SessionHomeOverride;
        IndexDatabasePath = current.IndexDatabasePath;
        CleanupOlderThanDays = current.CleanupOlderThanDays;
        CleanupLargerThanMegabytes = current.CleanupLargerThanMegabytes;
        TerminalFontSize = current.TerminalFontSize;
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

        NotifyPriceState();
    }

    /// <summary>
    /// 테마는 고른 즉시 적용하고 저장한다.
    /// 다른 값처럼 저장 버튼을 기다리면 "골랐는데 아무 일도 안 난다"로 보인다.
    /// </summary>
    partial void OnThemeIndexChanged(int value)
    {
        var theme = value switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System",
        };

        if (string.Equals(_settings.Current.Theme, theme, StringComparison.Ordinal))
        {
            return;
        }

        _settings.Current.Theme = theme;
        _settings.Save();

        ThemeChanged?.Invoke(this, theme);
    }

    /// <summary>값을 설정 파일에 쓴다.</summary>
    [RelayCommand]
    public void Save()
    {
        // 쓸 수 없는 인덱스 경로는 저장하지 않는다. 저장해 두면 다음 시작에서 기본 경로로 물러서게 되는데,
        // 고칠 수 있는 지금 말해 주는 편이 낫다 (2026-09-11 없는 드라이브로 앱이 죽던 것의 앞단)
        if (Blank(IndexDatabasePath) is { } wanted && IndexLocation.Probe(wanted) is { } failure)
        {
            StatusText = UiStrings.Format("Settings_IndexPathUnusable", failure);
            return;
        }

        var current = _settings.Current;

        current.SessionHomeOverride = Blank(SessionHomeOverride);
        current.IndexDatabasePath = Blank(IndexDatabasePath);
        current.CleanupOlderThanDays = Math.Max(0, CleanupOlderThanDays);
        current.CleanupLargerThanMegabytes = Math.Max(0, CleanupLargerThanMegabytes);
        current.TerminalFontSize = Math.Clamp(TerminalFontSize, 8, 28);
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
        StatusText = UiStrings.Format("Settings_Saved", SettingsFilePath);
    }

    /// <summary>단가 행을 추가한다.</summary>
    [RelayCommand]
    public void AddPrice()
    {
        Prices.Add(new PriceRowViewModel());
        NotifyPriceState();
    }

    private void NotifyPriceState()
    {
        OnPropertyChanged(nameof(HasPrices));
        OnPropertyChanged(nameof(HasNoPrices));
    }

    /// <summary>단가 행을 지운다.</summary>
    [RelayCommand]
    public void RemovePrice(PriceRowViewModel? row)
    {
        if (row is not null)
        {
            Prices.Remove(row);
            NotifyPriceState();
        }
    }

    /// <summary>인덱스를 처음부터 다시 만든다.</summary>
    [RelayCommand]
    public async Task RebuildIndexAsync()
    {
        StatusText = UiStrings.Get("Settings_Rebuilding");
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
