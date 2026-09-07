using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.Core;
using Daiso.App.Strings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Daiso.App.ViewModels;

/// <summary>도구별 설치·로그인 상태와 세션·사용량 요약. (REQUIREMENTS §3, GOAL Step 9)</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private const int UsageDays = 7;

    private readonly IReadOnlyList<IProvider> _providers;
    private readonly ITerminalLauncher _launcher;
    private readonly IndexService _indexService;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private int sessionCount;

    [ObservableProperty]
    private long totalSizeBytes;

    [ObservableProperty]
    private TokenUsage recentUsage = TokenUsage.Zero;

    public DashboardViewModel(
        IEnumerable<IProvider> providers,
        ITerminalLauncher launcher,
        IndexService indexService)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(indexService);

        _providers = providers.ToList();
        _launcher = launcher;
        _indexService = indexService;

        Tools = new ObservableCollection<ToolCardViewModel>(
            _providers.Select(provider => new ToolCardViewModel(provider.Kind)));
    }

    /// <summary>요약에 보여줄 최근 세션 수.</summary>
    private const int RecentSessionCount = 5;

    /// <summary>가장 최근에 손댄 세션 몇 건. 바로 이어서 열 수 있다.</summary>
    public ObservableCollection<RecentSessionViewModel> RecentSessions { get; } = [];

    /// <summary>최근 세션이 있는가.</summary>
    public bool HasRecentSessions => RecentSessions.Count > 0;

    /// <summary>최근 세션이 없는가. 첫 실행 안내에 쓴다.</summary>
    public bool HasNoRecentSessions => RecentSessions.Count == 0;

    /// <summary>도구 카드. Claude, Codex 순서.</summary>
    public ObservableCollection<ToolCardViewModel> Tools { get; }

    /// <summary>최근 며칠을 요약하는지.</summary>
    public int UsageDayCount => UsageDays;

    /// <summary>사람이 읽는 총 용량.</summary>
    public string TotalSizeText => FormatSize(TotalSizeBytes);

    /// <summary>최근 사용량 요약 문구.</summary>
    public string RecentUsageText => UiStrings.Format(
        "Dashboard_UsageBreakdown",
        Formats.Tokens(RecentUsage.Input),
        Formats.Tokens(RecentUsage.Output),
        Formats.Tokens(RecentUsage.CacheCreate),
        Formats.Tokens(RecentUsage.CacheRead));

    /// <summary>같은 내용의 정확한 값. ToolTip에 쓴다.</summary>
    public string RecentUsageExact => UiStrings.Format(
        "Dashboard_UsageBreakdown",
        Formats.Exact(RecentUsage.Input),
        Formats.Exact(RecentUsage.Output),
        Formats.Exact(RecentUsage.CacheCreate),
        Formats.Exact(RecentUsage.CacheRead));

    /// <summary>세션 수와 총 용량 한 줄.</summary>
    public string SessionSummaryText =>
        UiStrings.Format("Dashboard_SessionSummary", SessionCount, TotalSizeText);

    /// <summary>세션 수 (단위 포함).</summary>
    public string SessionCountText => UiStrings.Format("Sessions_Count", SessionCount);

    /// <summary>최근 사용량 총합. 줄여 쓴 값.</summary>
    public string RecentTokensText => Formats.Tokens(RecentUsage.Total);

    /// <summary>최근 사용량 총합의 정확한 값. ToolTip용.</summary>
    public string RecentTokensExact => Formats.Exact(RecentUsage.Total);

    /// <summary>사용량 카드 제목.</summary>
    public string UsageHeaderText => UiStrings.Format("Dashboard_StatTokens", UsageDayCount);

    /// <summary>지금 로그인 상태를 다시 읽는다. 프로필 저장에 쓴다.</summary>
    public Task<AuthStatus> ReadAuthStatusAsync(ToolKind tool, CancellationToken ct = default) =>
        _providers.First(provider => provider.Kind == tool).GetAuthStatusAsync(ct);

    /// <summary>최근 세션을 Terminal 화면에 채워 둔다. 실행은 사람이 누른다.</summary>
    public void PrepareResume(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var provider = _providers.First(item => item.Kind == session.Tool);
        var terminal = App.Services.GetRequiredService<TerminalViewModel>();

        terminal.PrepareResume(
            session.ProjectPath ?? string.Empty,
            provider.BuildResumeArguments(session));
    }

    /// <summary>도구 상태와 세션·사용량 요약을 다시 읽는다.</summary>
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
            foreach (var provider in _providers)
            {
                var card = Tools.First(tool => tool.Kind == provider.Kind);
                card.Apply(
                    await provider.IsInstalledAsync(ct).ConfigureAwait(true),
                    await provider.GetAuthStatusAsync(ct).ConfigureAwait(true));
            }

            var sessions = await _indexService.Index
                .ListAsync(SessionFilter.All, ct)
                .ConfigureAwait(true);

            SessionCount = sessions.Count;
            TotalSizeBytes = sessions.Sum(session => session.SizeBytes);

            RecentSessions.Clear();

            foreach (var session in sessions
                .OrderByDescending(session => session.ModifiedAt)
                .Take(RecentSessionCount))
            {
                RecentSessions.Add(new RecentSessionViewModel(session));
            }

            OnPropertyChanged(nameof(HasRecentSessions));
            OnPropertyChanged(nameof(HasNoRecentSessions));

            var to = DateOnly.FromDateTime(DateTime.UtcNow);
            var usage = await _indexService.Index
                .GetUsageAsync(to.AddDays(-(UsageDays - 1)), to, ct)
                .ConfigureAwait(true);

            RecentUsage = usage.Days.Aggregate(TokenUsage.Zero, (total, day) => total.Add(day.Usage));

            OnPropertyChanged(nameof(TotalSizeText));
            OnPropertyChanged(nameof(RecentUsageText));
            OnPropertyChanged(nameof(RecentUsageExact));
            OnPropertyChanged(nameof(SessionSummaryText));
            OnPropertyChanged(nameof(SessionCountText));
            OnPropertyChanged(nameof(RecentTokensText));
            OnPropertyChanged(nameof(RecentTokensExact));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>터미널을 열어 로그인 절차를 띄운다. 앱은 토큰을 다루지 않는다.</summary>
    [RelayCommand]
    public async Task LoginAsync(ToolCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var provider = _providers.First(p => p.Kind == card.Kind);
        var arguments = provider.Kind == ToolKind.Codex ? "login" : string.Empty;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        await _launcher.LaunchAsync(home, provider.ExecutableName, arguments).ConfigureAwait(true);
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):N1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):N2} GB",
    };
}

/// <summary>요약 화면의 최근 세션 한 줄.</summary>
public sealed class RecentSessionViewModel
{
    public RecentSessionViewModel(SessionInfo session) => Session = session;

    public SessionInfo Session { get; }

    /// <summary>도구 한 글자.</summary>
    public string ToolInitial => Session.Tool == ToolKind.Claude ? "C" : "X";

    /// <summary>도구 색.</summary>
    public Brush ToolBrush => new SolidColorBrush(Session.Tool == ToolKind.Claude
        ? Color.FromArgb(255, 122, 90, 248)
        : Color.FromArgb(255, 96, 104, 120));

    /// <summary>프로젝트 이름. 경로 전체는 ToolTip에 둔다.</summary>
    public string ProjectText => Formats.FolderName(Session.ProjectPath);

    /// <summary>전체 경로.</summary>
    public string PathText => Session.ProjectPath ?? string.Empty;

    /// <summary>시각과 용량.</summary>
    public string MetaText =>
        $"{Session.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {DashboardViewModel.FormatSize(Session.SizeBytes)}";
}

/// <summary>도구 하나의 카드 상태. 토큰 값은 어떤 속성에도 담지 않는다. (ARCHITECTURE §7.1)</summary>
public sealed partial class ToolCardViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isInstalled;

    [ObservableProperty]
    private AuthState state = AuthState.Missing;

    [ObservableProperty]
    private string? accountLabel;

    [ObservableProperty]
    private string? email;

    [ObservableProperty]
    private DateTimeOffset? sessionExpiresAt;

    public ToolCardViewModel(ToolKind kind) => Kind = kind;

    public ToolKind Kind { get; }

    /// <summary>카드 제목.</summary>
    public string Title => Kind == ToolKind.Claude ? "Claude Code" : "Codex CLI";

    /// <summary>부가 정보. MCP 커넥터 이름, 구독 등급 등.</summary>
    public ObservableCollection<string> Extras { get; } = [];

    /// <summary>상태 점 색. 초록·주황·빨강.</summary>
    public Brush StateBrush => new SolidColorBrush(State switch
    {
        AuthState.LoggedIn => Color.FromArgb(255, 16, 137, 62),
        AuthState.ExpiringSoon => Color.FromArgb(255, 191, 122, 0),
        _ => Color.FromArgb(255, 196, 43, 28),
    });

    /// <summary>상태 설명.</summary>
    public string StateText => State switch
    {
        AuthState.LoggedIn => UiStrings.Get("Auth_LoggedIn"),
        AuthState.ExpiringSoon => UiStrings.Get("Auth_ExpiringSoon"),
        AuthState.Expired => UiStrings.Get("Auth_Expired"),
        _ => UiStrings.Get("Auth_NoInfo"),
    };

    /// <summary>설치 여부 문구.</summary>
    public string InstalledText =>
        UiStrings.Get(IsInstalled ? "Auth_Installed" : "Auth_NotInstalled");

    /// <summary>재로그인까지 남은 기간. 날짜와 남은 일수를 함께 보여준다.</summary>
    public string ExpiresText => Formats.Expiry(SessionExpiresAt);

    /// <summary>부가 정보가 있을 때만 접이식 영역을 보여준다.</summary>
    public bool HasExtras => Extras.Count > 0;

    /// <summary>버튼 문구. 이미 로그인돼 있으면 "다시 로그인".</summary>
    public string LoginLabel =>
        UiStrings.Get(State == AuthState.Missing ? "Dashboard_Login" : "Auth_ReLogin");

    /// <summary>로그인이 필요한 상태인지. 버튼 강조에 쓴다.</summary>
    public bool NeedsLogin => State is AuthState.Missing or AuthState.Expired or AuthState.ExpiringSoon;

    internal void Apply(bool installed, AuthStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        IsInstalled = installed;
        State = status.State;
        AccountLabel = status.AccountLabel;
        Email = status.Email;
        SessionExpiresAt = status.SessionExpiresAt;

        Extras.Clear();

        foreach (var extra in status.Extras)
        {
            Extras.Add(extra);
        }

        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(InstalledText));
        OnPropertyChanged(nameof(ExpiresText));
        OnPropertyChanged(nameof(NeedsLogin));
        OnPropertyChanged(nameof(HasExtras));
        OnPropertyChanged(nameof(LoginLabel));
    }
}
