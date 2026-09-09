using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.Core;
using Daiso.App.Strings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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
            ToolLook.InDisplayOrder(_providers, provider => provider.Kind)
                .Select(provider => new ToolCardViewModel(provider.Kind)));
        VisibleTools = new ObservableCollection<ToolCardViewModel>(Tools);
    }

    /// <summary>탭. 0은 전체, 그 뒤는 <see cref="ToolLook.DisplayOrder"/> 순서의 도구 하나.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTool))]
    private int selectedTabIndex;

    /// <summary>지금 탭의 도구. 전체면 null.</summary>
    public ToolKind? SelectedTool =>
        SelectedTabIndex >= 1 && SelectedTabIndex <= ToolLook.DisplayOrder.Count
            ? ToolLook.DisplayOrder[SelectedTabIndex - 1]
            : null;

    /// <summary>지금 탭에 보이는 도구 카드. 전체면 셋, 도구 탭이면 그 하나.</summary>
    public ObservableCollection<ToolCardViewModel> VisibleTools { get; }

    /// <summary>마지막으로 읽은 세션 전부. 탭이 바뀌면 여기서 다시 거른다.</summary>
    private IReadOnlyList<SessionInfo> _allSessions = [];

    partial void OnSelectedTabIndexChanged(int value) => _ = ApplyTabAsync(default);

    /// <summary>탭에 맞게 카드·최근 세션·통계를 다시 계산한다. 파일은 다시 읽지 않고 인덱스만 묻는다.</summary>
    private async Task ApplyTabAsync(CancellationToken ct)
    {
        var tool = SelectedTool;

        VisibleTools.Clear();
        foreach (var card in Tools.Where(card => tool is null || card.Kind == tool))
        {
            VisibleTools.Add(card);
        }

        var sessions = tool is null ? _allSessions : _allSessions.Where(session => session.Tool == tool).ToList();

        SessionCount = sessions.Count;
        TotalSizeBytes = sessions.Sum(session => session.SizeBytes);

        RecentSessions.Clear();
        foreach (var session in sessions.OrderByDescending(session => session.ModifiedAt).Take(RecentSessionCount))
        {
            RecentSessions.Add(new RecentSessionViewModel(session));
        }

        OnPropertyChanged(nameof(HasRecentSessions));
        OnPropertyChanged(nameof(HasNoRecentSessions));

        try
        {
            var to = DateOnly.FromDateTime(DateTime.UtcNow);
            var usage = await _indexService.Index
                .GetUsageAsync(to.AddDays(-(UsageDays - 1)), to, tool, ct)
                .ConfigureAwait(true);

            RecentUsage = usage.Days.Aggregate(TokenUsage.Zero, (total, day) => total.Add(day.Usage));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            RecentUsage = TokenUsage.Zero;
        }

        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(RecentUsageText));
        OnPropertyChanged(nameof(RecentUsageExact));
        OnPropertyChanged(nameof(SessionSummaryText));
        OnPropertyChanged(nameof(SessionCountText));
        OnPropertyChanged(nameof(RecentTokensText));
        OnPropertyChanged(nameof(RecentTokensExact));
    }

    /// <summary>요약에 보여줄 최근 세션 수.</summary>
    private const int RecentSessionCount = 5;

    /// <summary>가장 최근에 손댄 세션 몇 건. 바로 이어서 열 수 있다.</summary>
    public ObservableCollection<RecentSessionViewModel> RecentSessions { get; } = [];

    /// <summary>최근 세션이 있는가.</summary>
    public bool HasRecentSessions => RecentSessions.Count > 0;

    /// <summary>최근 세션이 없는가. 첫 실행 안내에 쓴다.</summary>
    public bool HasNoRecentSessions => RecentSessions.Count == 0;

    /// <summary>도구 카드 전부. 순서는 ToolLook.DisplayOrder.</summary>
    public ObservableCollection<ToolCardViewModel> Tools { get; }

    /// <summary>최근 며칠을 요약하는지.</summary>
    public int UsageDayCount => UsageDays;

    /// <summary>사람이 읽는 총 용량.</summary>
    public string TotalSizeText => Formats.Size(TotalSizeBytes);

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
        var embedded = Daiso.App.Terminal.TerminalHost.IsRuntimeAvailable();

        terminal.PrepareResume(
            session.Tool,
            session.ProjectPath ?? string.Empty,
            provider.BuildResumeArguments(session),
            autoOpen: embedded);
    }

    /// <summary>도구 상태와 세션·사용량 요약을 다시 읽는다.</summary>
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
            foreach (var provider in _providers)
            {
                var card = Tools.First(tool => tool.Kind == provider.Kind);
                card.Apply(
                    await provider.IsInstalledAsync(ct).ConfigureAwait(true),
                    await provider.GetAuthStatusAsync(ct).ConfigureAwait(true));
            }

            _allSessions = await _indexService.Index
                .ListAsync(SessionFilter.All, ct)
                .ConfigureAwait(true);

            await ApplyTabAsync(ct).ConfigureAwait(true);
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

        await _launcher.LaunchAsync(home, provider.LaunchTarget, arguments).ConfigureAwait(true);
    }

}

/// <summary>요약 화면의 최근 세션 한 줄.</summary>
public sealed class RecentSessionViewModel
{
    public RecentSessionViewModel(SessionInfo session) => Session = session;

    public SessionInfo Session { get; }

    /// <summary>도구 한 글자.</summary>
    public string ToolInitial => ToolLook.Initial(Session.Tool);

    /// <summary>도구 원에 마우스를 올리면 이름이 보인다. 머리글자만으로는 처음엔 모른다.</summary>
    public string ToolName => ToolLook.Title(Session.Tool);

    /// <summary>도구 색.</summary>
    public Brush ToolBrush => ToolLook.Brush(Session.Tool);

    /// <summary>프로젝트 이름. 경로 전체는 ToolTip에 둔다.</summary>
    public string ProjectText => Formats.FolderName(Session.ProjectPath);

    /// <summary>전체 경로.</summary>
    public string PathText => Session.ProjectPath ?? string.Empty;

    /// <summary>시각과 용량.</summary>
    public string MetaText =>
        $"{Session.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {Formats.Size(Session.SizeBytes)}";
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
    public string Title => ToolLook.Title(Kind);

    /// <summary>부가 정보. MCP 커넥터 이름, 구독 등급 등.</summary>
    public ObservableCollection<string> Extras { get; } = [];

    /// <summary>이 도구의 로그인 프로필. 요약 화면이 <see cref="AuthProfileViewModel"/>에서 골라 채운다.</summary>
    public ObservableCollection<AuthProfileRowViewModel> Profiles { get; } = [];

    /// <summary>보관 중인 프로필이 있는가.</summary>
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>보관 중인 프로필이 없는가. 안내 문구를 띄운다.</summary>
    public bool HasNoProfiles => Profiles.Count == 0;

    /// <summary>계정 버튼 글자. 보관 개수가 있으면 붙인다.</summary>
    public string AccountsButtonText => Profiles.Count == 0
        ? UiStrings.Get("AuthProfile_Accounts")
        : UiStrings.Format("AuthProfile_AccountsCount", Profiles.Count);

    /// <summary>프로필 저장·전환·삭제 결과 한 줄.</summary>
    [ObservableProperty]
    private string? profileStatus;

    /// <summary>이 도구의 프로필만 골라 다시 채운다.</summary>
    public void SetProfiles(IEnumerable<AuthProfileRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Profiles.Clear();

        foreach (var row in rows.Where(row => row.Profile.Tool == Kind))
        {
            Profiles.Add(row);
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasNoProfiles));
        OnPropertyChanged(nameof(AccountsButtonText));
    }

    /// <summary>상태 점 색. 값은 App.xaml 이 정한다 — 색을 바꾸려면 그 한 곳만 고친다.</summary>
    public Brush StateBrush => (Brush)Application.Current.Resources[State switch
    {
        AuthState.LoggedIn => "AuthOkBrush",
        AuthState.ExpiringSoon => "AuthWarnBrush",
        _ => "AuthErrorBrush",
    }];

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
