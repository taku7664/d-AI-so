using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;
using Daiso.Core.Sessions;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Daiso.App.ViewModels;

/// <summary>세션 목록·검색·상세·정리. (REQUIREMENTS §5.1, §5.2)</summary>
public sealed partial class SessionsViewModel : ObservableObject
{
    private const int SearchMinimumLength = 2;

    /// <summary>타임라인에 그리는 최대 메시지 수.</summary>
    /// <summary>화면에 올리는 메시지 상한. 가상화 목록이라 그리기 비용은 보이는 만큼만 든다.</summary>
    private const int TimelineLimit = 2000;

    /// <summary>파일에서 읽어 메모리에 두는 상한. 도구 호출 토글은 이 안에서 다시 걸러 파일을 다시 읽지 않는다.</summary>
    private const int RawMessageLimit = 20000;

    /// <summary>선택한 세션의 메시지 전부(상한 안). 필터는 여기서 건다.</summary>
    private List<SessionMessage> _loadedMessages = [];

    /// <summary>진행 중인 타임라인 읽기. 다른 세션을 고르면 앞의 것을 취소한다.</summary>
    private CancellationTokenSource? _timelineCts;

    /// <summary>프로젝트 경로를 모를 때 쓰는 표식. 묶음·비교에 쓰므로 번역하지 않는다.</summary>
    internal const string UnknownProject = "(알 수 없음)";

    private readonly IndexService _indexService;
    private readonly IReadOnlyList<IProvider> _providers;
    private readonly IFileDisposer _disposer;
    private readonly ISessionExporter _exporter;
    private readonly ITerminalLauncher _launcher;
    private readonly ISettingsStore _settings;

    private List<SessionInfo> _allSessions = [];
    private int _searchHitCount;

    [ObservableProperty]
    private bool isBusy;

    /// <summary>선택한 세션의 대화를 읽고 있는가. 큰 세션은 몇 초 걸린다.</summary>
    [ObservableProperty]
    private bool isTimelineLoading;

    /// <summary>
    /// 도구 호출·시스템 메시지도 보여줄지.
    /// 기본은 끈다. 켜면 사람과 모델의 대화가 도구 호출에 밀려 안 보인다.
    /// </summary>
    [ObservableProperty]
    private bool showToolCalls;

    /// <summary>타임라인이 잘렸는가. 상세 패널에서 알려준다.</summary>
    [ObservableProperty]
    private bool isTimelineTruncated;

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private ProjectGroupViewModel? selectedProject;

    [ObservableProperty]
    private SessionRowViewModel? selectedSession;

    [ObservableProperty]
    private string? statusText;

    // 필터
    [ObservableProperty]
    private int toolFilterIndex;

    [ObservableProperty]
    private int periodFilterIndex;

    [ObservableProperty]
    private bool orphansOnly;

    [ObservableProperty]
    private double minSizeMegabytes;

    [ObservableProperty]
    private bool includeArchived = true;

    public SessionsViewModel(
        IndexService indexService,
        IEnumerable<IProvider> providers,
        IFileDisposer disposer,
        ISessionExporter exporter,
        ITerminalLauncher launcher,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(indexService);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(disposer);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(settings);

        _indexService = indexService;
        _providers = providers.ToList();
        _disposer = disposer;
        _exporter = exporter;
        _launcher = launcher;
        _settings = settings;
    }

    /// <summary>왼쪽 프로젝트 트리.</summary>
    public ObservableCollection<ProjectGroupViewModel> Projects { get; } = [];

    /// <summary>오른쪽 세션 목록.</summary>
    public ObservableCollection<SessionRowViewModel> Sessions { get; } = [];

    /// <summary>검색 결과 트리 (프로젝트 &gt; 세션 &gt; 매칭 문장).</summary>
    public ObservableCollection<SearchProjectViewModel> SearchResults { get; } = [];

    /// <summary>선택한 세션의 메시지 타임라인.</summary>
    /// <summary>화면에 보이는 메시지. 한 번에 통째로 갈아 끼운다. 한 건씩 Add하면 수백 번 다시 그린다.</summary>
    [ObservableProperty]
    private IReadOnlyList<MessageViewModel> timeline = [];

    /// <summary>기간 필터 항목.</summary>
    public IReadOnlyList<string> PeriodFilters { get; } =
    [
        UiStrings.All,
        UiStrings.Get("Sessions_Period7"),
        UiStrings.Get("Sessions_Period30"),
        UiStrings.Get("Sessions_Period90"),
    ];

    /// <summary>체크된 세션 수.</summary>
    public int CheckedCount => Sessions.Count(row => row.IsChecked);

    /// <summary>지울 대상이 있는가. 파괴적인 버튼의 활성 조건.</summary>
    public bool HasChecked => CheckedCount > 0;

    /// <summary>선택 개수 문구.</summary>
    public string CheckedText => UiStrings.Format("Sessions_CheckedCount", CheckedCount);

    /// <summary>전체 체크 상자 상태. 전부 켜졌을 때만 참.</summary>
    public bool AllChecked => Sessions.Count > 0 && CheckedCount == Sessions.Count;

    /// <summary>보이는 세션 전부를 켜거나 끈다.</summary>
    public void CheckAll(bool on)
    {
        foreach (var row in Sessions)
        {
            row.IsChecked = on;
        }

        NotifyChecked();
    }

    /// <summary>목록에 보여줄 세션이 있는가.</summary>
    public bool HasSessions => Sessions.Count > 0;

    /// <summary>목록이 비었는가. 빈 상태 안내를 띄운다.</summary>
    public bool IsListEmpty => Sessions.Count == 0;

    /// <summary>
    /// 목록 제목 옆 건수. 검색 중이면 결과 수를 보여준다.
    /// (검색 중에 프로젝트의 세션 수를 보여주면 숫자가 서로 안 맞아 보인다)
    /// </summary>
    public string SessionCountText => HasSearchResults
        ? UiStrings.Format("Sessions_HitCount", _searchHitCount)
        : UiStrings.Format("Sessions_Count", Sessions.Count);

    /// <summary>검색 중인가. 프로젝트 필터를 잠그는 데 쓴다.</summary>
    public bool IsSearching => HasSearchResults;

    /// <summary>검색 중이 아닌가. 프로젝트 필터 활성 조건.</summary>
    public bool CanPickProject => !HasSearchResults;

    /// <summary>목록 제목. 검색 중이면 검색 결과임을 밝힌다.</summary>
    public string ListHeaderText =>
        UiStrings.Get(HasSearchResults ? "Sessions_SearchTitle" : "Sessions_ListHeader");

    /// <summary>세션을 골랐는가.</summary>
    public bool HasSelectedSession => SelectedSession is not null;

    /// <summary>대화가 하나도 없는가. 다 읽은 뒤에만 안내를 띄운다.</summary>
    public bool IsTimelineEmpty => HasSelectedSession && !IsTimelineLoading && Timeline.Count == 0;

    /// <summary>대화를 보여줄 수 있는가.</summary>
    public bool HasTimeline => Timeline.Count > 0;

    /// <summary>잘렸을 때 보여줄 안내.</summary>
    public string TimelineTruncatedText =>
        UiStrings.Format("Sessions_TimelineTruncated", TimelineLimit);

    /// <summary>아무것도 고르지 않았는가. 안내 문구를 띄운다.</summary>
    public bool NoSelection => SelectedSession is null;

    /// <summary>검색 결과가 있는지.</summary>
    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>검색 결과가 없을 때만 프로젝트 목록을 보여준다.</summary>
    public Microsoft.UI.Xaml.Visibility ProjectListVisibility =>
        HasSearchResults ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    /// <summary>검색 결과가 있을 때만 트리를 보여준다.</summary>
    public Microsoft.UI.Xaml.Visibility SearchTreeVisibility =>
        HasSearchResults ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>목록을 인덱스에서 다시 읽는다.</summary>
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
            _allSessions = [.. await _indexService.Index.ListAsync(BuildFilter(), ct).ConfigureAwait(true)];
            RebuildProjects();
            ApplyProjectSelection();
            StatusText = UiStrings.Format("Sessions_Overview", Projects.Count, _allSessions.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>필터를 바꾼 뒤 다시 읽는다.</summary>
    [RelayCommand]
    public Task ApplyFiltersAsync(CancellationToken ct) => LoadAsync(ct);

    /// <summary>본문 검색. 두 글자 미만이면 결과를 비운다.</summary>
    [RelayCommand]
    public async Task SearchAsync(CancellationToken ct)
    {
        SearchResults.Clear();

        if (SearchQuery is not { Length: >= SearchMinimumLength } query)
        {
            NotifySearchVisibility();
            StatusText = UiStrings.Format("Sessions_QueryTooShort", SearchMinimumLength);
            return;
        }

        IsBusy = true;

        try
        {
            var hits = await _indexService.Index.SearchAsync(query, ct).ConfigureAwait(true);

            foreach (var byProject in hits.GroupBy(hit => hit.Session.ProjectPath ?? UnknownProject, StringComparer.OrdinalIgnoreCase))
            {
                var project = new SearchProjectViewModel(byProject.Key);

                foreach (var bySession in byProject.GroupBy(hit => hit.Session.FilePath))
                {
                    var first = bySession.First();
                    var session = new SearchSessionViewModel(first.Session);

                    foreach (var hit in bySession.Take(20))
                    {
                        session.Matches.Add(new SearchMatchViewModel(first.Session, hit.Message, hit.Snippet));
                    }

                    project.Sessions.Add(session);
                }

                SearchResults.Add(project);
            }

            _searchHitCount = hits.Count;
            StatusText = UiStrings.Format("Sessions_SearchResult", query, hits.Count);
        }
        finally
        {
            IsBusy = false;
            NotifySearchVisibility();
        }
    }

    /// <summary>검색 결과를 지운다.</summary>
    [RelayCommand]
    public void ClearSearch()
    {
        SearchQuery = null;
        SearchResults.Clear();
        _searchHitCount = 0;
        NotifySearchVisibility();

        // 검색을 지웠으면 상태 문구도 목록 기준으로 되돌린다.
        StatusText = UiStrings.Format("Sessions_Count", _allSessions.Count);
    }

    private void NotifySearchVisibility()
    {
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(ListHeaderText));
        OnPropertyChanged(nameof(SessionCountText));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(CanPickProject));
        OnPropertyChanged(nameof(ProjectListVisibility));
        OnPropertyChanged(nameof(SearchTreeVisibility));
    }

    /// <summary>검색 결과에서 세션을 골라 목록·상세를 맞춘다.</summary>
    public async Task SelectFromSearchAsync(SessionInfo session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var project = Projects.FirstOrDefault(p =>
            string.Equals(p.Path, session.ProjectPath ?? UnknownProject, StringComparison.OrdinalIgnoreCase));

        if (project is not null)
        {
            SelectedProject = project;
        }

        var row = Sessions.FirstOrDefault(r =>
            string.Equals(r.Session.FilePath, session.FilePath, StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            row = new SessionRowViewModel(session);
            Sessions.Insert(0, row);
        }

        SelectedSession = row;
        await LoadTimelineAsync(session, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// 선택한 세션의 메시지를 읽어온다. 파일 읽기와 파싱은 백그라운드에서 한 번에 끝내고,
    /// UI에는 완성된 목록을 한 번만 올린다. 다른 세션을 고르면 앞의 읽기는 취소된다.
    /// </summary>
    public async Task LoadTimelineAsync(SessionInfo session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        _timelineCts?.Cancel();
        _timelineCts?.Dispose();
        _timelineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _timelineCts.Token;

        Timeline = [];
        _loadedMessages = [];
        IsTimelineLoading = true;
        IsTimelineTruncated = false;
        NotifyTimelineState();

        var provider = _providers.FirstOrDefault(p => p.Kind == session.Tool);
        if (provider is null)
        {
            IsTimelineLoading = false;
            NotifyTimelineState();
            return;
        }

        try
        {
            var messages = await Task.Run(
                async () =>
                {
                    var list = new List<SessionMessage>();

                    await foreach (var message in provider
                        .ReadMessagesAsync(session.FilePath, 0, token)
                        .ConfigureAwait(false))
                    {
                        list.Add(message);

                        if (list.Count >= RawMessageLimit)
                        {
                            break;
                        }
                    }

                    return list;
                },
                token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            _loadedMessages = messages;
            ApplyTimelineFilter();
        }
        catch (OperationCanceledException)
        {
            // 다른 세션으로 넘어갔다. 새 읽기가 상태를 이어받는다.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // 파일이 잠겼거나 지워졌거나 깨졌다. 앱이 죽는 대신 이유를 보여준다.
            StatusText = UiStrings.Format("Sessions_TimelineFailed", ex.Message);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsTimelineLoading = false;
                NotifyTimelineState();
            }
        }
    }

    /// <summary>메모리에 있는 메시지에서 보일 것만 골라 한 번에 올린다. 파일은 다시 읽지 않는다.</summary>
    private void ApplyTimelineFilter()
    {
        // 기본은 대화만. 도구 호출·시스템 주입은 토글을 켤 때만 보여준다.
        var visible = _loadedMessages
            .Where(message => ShowToolCalls || message.Role is not (MessageRole.Tool or MessageRole.System));

        var items = new List<MessageViewModel>(Math.Min(TimelineLimit, _loadedMessages.Count));
        var truncated = false;

        foreach (var message in visible)
        {
            if (items.Count >= TimelineLimit)
            {
                truncated = true;
                break;
            }

            items.Add(new MessageViewModel(message));
        }

        IsTimelineTruncated = truncated;
        Timeline = items;
        NotifyTimelineState();
    }

    /// <summary>세션을 터미널에서 이어서 연다.</summary>
    [RelayCommand]
    public async Task ResumeAsync(SessionRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var provider = _providers.First(p => p.Kind == row.Session.Tool);
        var directory = row.Session.ProjectPath is { Length: > 0 } path && Directory.Exists(path)
            ? path
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AppSettings.Remember(_settings.Current.RecentFolders, directory);
        _settings.Save();

        await _launcher
            .LaunchAsync(directory, provider.LaunchTarget, provider.BuildResumeArguments(row.Session))
            .ConfigureAwait(true);

        StatusText = UiStrings.Format(
            "Sessions_ResumeStatus",
            provider.LaunchTarget,
            provider.BuildResumeArguments(row.Session));
    }

    /// <summary>세션을 마크다운으로 내보낸다.</summary>
    public async Task ExportAsync(SessionInfo session, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        IsBusy = true;

        try
        {
            await _exporter
                .ExportMarkdownAsync(session, outputPath, ExportOptions.Default, ct)
                .ConfigureAwait(true);

            StatusText = UiStrings.Format("Sessions_Exported", outputPath);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>필터를 기본값으로 되돌린다.</summary>
    [RelayCommand]
    public void ResetFilters()
    {
        ToolFilterIndex = 0;
        PeriodFilterIndex = 0;
        MinSizeMegabytes = 0;
        OrphansOnly = false;
        IncludeArchived = true;
    }

    /// <summary>규칙에 맞는 세션을 골라 체크한다. (N일 이상 / 고아 / N MB 초과)</summary>
    [RelayCommand]
    public void SelectByRule(string? rule)
    {
        var days = _settings.Current.CleanupOlderThanDays;
        var megabytes = _settings.Current.CleanupLargerThanMegabytes;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        foreach (var row in Sessions)
        {
            row.IsChecked = rule switch
            {
                "old" => row.Session.ModifiedAt < cutoff,
                "orphan" => row.IsOrphan,
                "large" => row.Session.SizeBytes > megabytes * 1024L * 1024,
                _ => false,
            };
        }

        StatusText = UiStrings.Format("Sessions_Selected", Sessions.Count(row => row.IsChecked));
    }

    /// <summary>체크된 세션을 지운다. 실행 중 세션은 건너뛴다.</summary>
    public async Task<DisposeResult> DeleteCheckedAsync(bool permanent)
    {
        var targets = Sessions.Where(row => row.IsChecked).Select(row => row.Session).ToList();

        if (targets.Count == 0)
        {
            return new DisposeResult([], []);
        }

        var result = permanent
            ? await _disposer.DeletePermanentlyAsync(targets).ConfigureAwait(true)
            : await _disposer.MoveToRecycleBinAsync(targets).ConfigureAwait(true);

        StatusText = UiStrings.Format(
            "Sessions_DeleteResultStatus",
            result.Deleted.Count,
            result.Skipped.Count);

        await _indexService.RefreshAsync().ConfigureAwait(true);
        await LoadAsync(default).ConfigureAwait(true);

        return result;
    }

    /// <summary>삭제 전 미리보기에 쓸 요약.</summary>
    public DeletePreview BuildDeletePreview()
    {
        var checkedRows = Sessions.Where(row => row.IsChecked).ToList();
        var active = checkedRows.Where(row => row.Session.IsActive).Select(row => row.Session.Id).ToList();

        return new DeletePreview(
            checkedRows.Count,
            checkedRows.Where(row => !row.Session.IsActive).Sum(row => row.Session.SizeBytes),
            active);
    }

    private SessionFilter BuildFilter()
    {
        DateOnly? from = PeriodFilterIndex switch
        {
            1 => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            2 => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            3 => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)),
            _ => null,
        };

        // 0은 전체, 그 뒤는 ToolLook.DisplayOrder 순서
        ToolKind? tool = ToolFilterIndex >= 1 && ToolFilterIndex <= ToolLook.DisplayOrder.Count
            ? ToolLook.DisplayOrder[ToolFilterIndex - 1]
            : null;

        return new SessionFilter(
            tool,
            null,
            from,
            null,
            OrphansOnly,
            MinSizeMegabytes > 0 ? (long)(MinSizeMegabytes * 1024 * 1024) : null,
            IncludeArchived);
    }

    private void RebuildProjects()
    {
        var previous = SelectedProject?.Path;
        Projects.Clear();

        foreach (var group in _allSessions
            .GroupBy(session => session.ProjectPath ?? UnknownProject, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Max(session => session.ModifiedAt)))
        {
            Projects.Add(new ProjectGroupViewModel(group.Key, [.. group]));
        }

        DisambiguateProjectNames();

        SelectedProject = Projects.FirstOrDefault(project =>
            string.Equals(project.Path, previous, StringComparison.OrdinalIgnoreCase))
            ?? Projects.FirstOrDefault();
    }

    /// <summary>겹치는 프로젝트 이름은 상위 폴더까지 붙여 구분한다. 규칙은 Formats에 하나만 둔다.</summary>
    private void DisambiguateProjectNames()
    {
        var labels = Formats.Labels([.. Projects.Select(project => project.Path)]);

        for (var i = 0; i < Projects.Count; i++)
        {
            Projects[i].DisambiguatedName = labels[i];
        }
    }

    private void ApplyProjectSelection()
    {
        foreach (var old in Sessions)
        {
            old.CheckedChanged -= OnRowCheckedChanged;
        }

        Sessions.Clear();

        if (SelectedProject is not null)
        {
            foreach (var session in SelectedProject.Sessions.OrderByDescending(session => session.ModifiedAt))
            {
                var row = new SessionRowViewModel(session);
                row.CheckedChanged += OnRowCheckedChanged;
                Sessions.Add(row);
            }
        }

        NotifyChecked();
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(SessionCountText));
    }

    partial void OnSelectedProjectChanged(ProjectGroupViewModel? value) => ApplyProjectSelection();

    // 필터를 바꾸면 바로 적용한다. 따로 "적용" 버튼을 누르지 않는다.
    partial void OnToolFilterIndexChanged(int value) => ReloadForFilter();

    partial void OnPeriodFilterIndexChanged(int value) => ReloadForFilter();

    partial void OnOrphansOnlyChanged(bool value) => ReloadForFilter();

    partial void OnIncludeArchivedChanged(bool value) => ReloadForFilter();

    partial void OnMinSizeMegabytesChanged(double value) => ReloadForFilter();

    partial void OnShowToolCallsChanged(bool value)
    {
        if (SelectedSession is not null && !IsTimelineLoading)
        {
            ApplyTimelineFilter();
        }
    }

    partial void OnSelectedSessionChanged(SessionRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedSession));
        OnPropertyChanged(nameof(NoSelection));
        NotifyTimelineState();
    }

    private void ReloadForFilter()
    {
        if (!IsBusy)
        {
            UiCommands.Start(ApplyFiltersCommand);
        }
    }

    private void OnRowCheckedChanged(object? sender, EventArgs e) => NotifyChecked();

    private void NotifyTimelineState()
    {
        OnPropertyChanged(nameof(IsTimelineEmpty));
        OnPropertyChanged(nameof(HasTimeline));
    }

    private void NotifyChecked()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(CheckedText));
        OnPropertyChanged(nameof(AllChecked));
    }
}

/// <summary>삭제 미리보기 요약.</summary>
/// <param name="Count">선택한 건수.</param>
/// <param name="ReclaimBytes">회수되는 용량.</param>
/// <param name="ActiveSessionIds">실행 중이라 빠지는 세션.</param>
public sealed record DeletePreview(int Count, long ReclaimBytes, IReadOnlyList<string> ActiveSessionIds);

/// <summary>프로젝트 하나. 왼쪽 트리 항목.</summary>
public sealed class ProjectGroupViewModel
{
    public ProjectGroupViewModel(string path, IReadOnlyList<SessionInfo> sessions)
    {
        Path = path;
        Sessions = sessions;
    }

    public string Path { get; }

    public IReadOnlyList<SessionInfo> Sessions { get; }

    public int Count => Sessions.Count;

    public long TotalBytes => Sessions.Sum(session => session.SizeBytes);

    /// <summary>폴더가 사라진 프로젝트.</summary>
    public bool IsOrphan =>
        Path == SessionsViewModel.UnknownProject || !Directory.Exists(Path);

    /// <summary>같은 이름이 여럿일 때 목록에서 구분하도록 상위 폴더까지 붙인 이름.</summary>
    public string? DisambiguatedName { get; set; }

    public string DisplayName => DisambiguatedName ?? DefaultDisplayName;

    private string DefaultDisplayName => Path == SessionsViewModel.UnknownProject
        ? UiStrings.Get("Common_Unknown")
        : System.IO.Path.GetFileName(Path.TrimEnd('\\'));

    public string Summary =>
        UiStrings.Format("Sessions_ProjectSummary", Count, DashboardViewModel.FormatSize(TotalBytes))
        + (IsOrphan ? UiStrings.Get("Sessions_OrphanSuffix") : string.Empty);

    /// <summary>드롭다운 한 줄. 이름과 건수를 함께 보여준다.</summary>
    public string ComboLabel => $"{DisplayName}  ·  {Summary}";
}

/// <summary>세션 목록 한 줄. (REQUIREMENTS §5.1 열)</summary>
public sealed partial class SessionRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isChecked;

    /// <summary>체크가 바뀌면 목록 쪽에서 선택 개수를 다시 센다.</summary>
    public event EventHandler? CheckedChanged;

    partial void OnIsCheckedChanged(bool value) => CheckedChanged?.Invoke(this, EventArgs.Empty);

    public SessionRowViewModel(SessionInfo session) => Session = session;

    public SessionInfo Session { get; }

    public string ToolIcon => Session.Tool switch { ToolKind.Claude => "🟣", ToolKind.Antigravity => "🔵", _ => "⚫" };

    public string ToolName => Session.Tool.ToString();

    public string StartedText => $"{Session.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}";

    public string ModifiedText => $"{Session.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}";

    public string SizeText => DashboardViewModel.FormatSize(Session.SizeBytes);

    /// <summary>목록 한 줄의 부제. 도구·시각·용량·메시지 수를 한 문구로 만든다.</summary>
    public string MetaText => UiStrings.Format(
        "Sessions_RowMeta",
        ToolName,
        ModifiedText,
        SizeText,
        Session.UserMessageCount,
        Session.AssistantMessageCount);

    public string FirstPromptText => Session.FirstPrompt?.Replace('\n', ' ') ?? string.Empty;

    /// <summary>
    /// 목록에 보이는 제목. 정제 규칙은 <see cref="SessionTitle"/>가 정본이다 — 터미널 새 세션 카드도
    /// 같은 것을 쓴다. 화면마다 규칙이 갈라지면 같은 세션이 화면마다 다르게 보인다.
    /// </summary>
    public string TitleText
    {
        get
        {
            var cleaned = SessionTitle.Clean(Session.FirstPrompt);

            return cleaned.Length > 0 ? cleaned : UiStrings.Get("Common_NoPrompt");
        }
    }

    /// <summary>도구 한 글자. 목록에서 아이콘 자리에 쓴다.</summary>
    public string ToolInitial => ToolLook.Initial(Session.Tool);

    /// <summary>도구 색. Claude 주황, Codex 초록, Antigravity 파랑.</summary>
    public Brush ToolBrush => ToolLook.Brush(Session.Tool);

    /// <summary>세션 id 앞 8자. 전체 값은 ToolTip에 둔다.</summary>
    public string IdShort => Session.Id.Length <= 8 ? Session.Id : Session.Id[..8];

    /// <summary>배지가 하나라도 있는가.</summary>
    public bool HasBadges => Badges.Length > 0;

    public bool IsOrphan => Session.ProjectPath is null || !Directory.Exists(Session.ProjectPath);

    /// <summary>`&lt;command-*&gt;` 태그를 걷어낸 본문. 세션 상세에서도 쓴다.</summary>
    internal static string CleanCommandText(string raw) => SessionTitle.Clean(raw);

    /// <summary>배지 문구. 실행 중·아카이브·고아.</summary>
    public string Badges => string.Join(
        " ",
        new[]
        {
            Session.IsActive ? UiStrings.Get("Sessions_BadgeActive") : null,
            Session.IsArchived ? UiStrings.Get("Sessions_BadgeArchived") : null,
            IsOrphan ? UiStrings.Get("Sessions_BadgeMissingFolder") : null,
        }.Where(badge => badge is not null));
}

/// <summary>타임라인 항목. 도구 호출은 접어서 보여준다.</summary>
public sealed partial class MessageViewModel : ObservableObject
{
    /// <summary>처음에 보여주는 글자 수. 도구 출력 한 덩어리가 수십만 자라 전부 펼치면 한 줄이 화면을 삼킨다.</summary>
    private const int PreviewChars = 1500;

    public MessageViewModel(SessionMessage message)
    {
        Message = message;
        FullText = message.Text.StartsWith("<command-", StringComparison.Ordinal)
            ? SessionRowViewModel.CleanCommandText(message.Text)
            : message.Text;
    }

    public SessionMessage Message { get; }

    /// <summary>태그만 걷어낸 전체 본문.</summary>
    public string FullText { get; }

    /// <summary>더 보기를 눌러 전체를 펼쳤는가.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(MoreText))]
    private bool isExpanded;

    /// <summary>미리보기 길이를 넘는가. 넘으면 더 보기 버튼이 붙는다.</summary>
    public bool IsTruncated => FullText.Length > PreviewChars;

    /// <summary>더 보기 / 접기 버튼 글자.</summary>
    public string MoreText => IsExpanded
        ? UiStrings.Get("Sessions_ShowLess")
        : UiStrings.Format("Sessions_ShowMore", FullText.Length - PreviewChars);

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    public string RoleText => UiStrings.Get(Message.Role switch
    {
        MessageRole.User => "Role_User",
        MessageRole.Assistant => "Role_Assistant",
        MessageRole.System => "Role_System",
        _ => "Role_Tool",
    });

    public string TimeText => $"{Message.At.ToLocalTime():HH:mm}";

    /// <summary>어시스턴트 말인가. 역할 알약 색을 다르게 해 한눈에 구분한다.</summary>
    public bool IsAssistant => Message.Role == MessageRole.Assistant;

    /// <summary>사람이 한 말에만 왼쪽 강조 바를 세운다. 눈으로 턴을 가르는 표시다.</summary>
    public Microsoft.UI.Xaml.Visibility UserBarVisibility => Message.Role == MessageRole.User
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>화면에 보여줄 본문. 길면 앞부분만, 더 보기를 누르면 전부.</summary>
    public string DisplayText => IsExpanded || !IsTruncated
        ? FullText
        : string.Concat(FullText.AsSpan(0, PreviewChars), " …");

    public string Text => Message.Text;

    /// <summary>도구 호출·시스템 메시지는 기본으로 접는다.</summary>
    public bool IsCollapsible => Message.Role is MessageRole.Tool or MessageRole.System;

    public bool IsPlain => !IsCollapsible;

    public Microsoft.UI.Xaml.Visibility PlainVisibility =>
        IsPlain ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility CollapsibleVisibility =>
        IsCollapsible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}

/// <summary>검색 결과 트리의 프로젝트 노드.</summary>
public sealed class SearchProjectViewModel
{
    public SearchProjectViewModel(string path) => Path = path;

    public string Path { get; }

    public ObservableCollection<SearchSessionViewModel> Sessions { get; } = [];

    /// <summary>트리에 보여줄 이름. 경로 전체는 길어서 폴더 이름만 쓴다.</summary>
    public string Summary =>
        UiStrings.Format("Sessions_HitProject", Formats.FolderName(Path), Sessions.Count);
}

/// <summary>검색 결과 트리의 세션 노드.</summary>
public sealed class SearchSessionViewModel
{
    public SearchSessionViewModel(SessionInfo session) => Session = session;

    public SessionInfo Session { get; }

    public ObservableCollection<SearchMatchViewModel> Matches { get; } = [];

    /// <summary>
    /// 트리 한 줄: 제목 · 날짜 · 건수. 도구·시각만 있던 때는 같은 프로젝트의 세션 여덟 개가
    /// "Claude · 2026-09-08 07:36" 식으로 늘어서서 어느 대화인지 알 수 없었다. 제목이 먼저다.
    /// </summary>
    public string Summary
    {
        get
        {
            var title = SessionTitle.Clean(Session.FirstPrompt);

            if (title.Length == 0)
            {
                title = UiStrings.Get("Common_NoPrompt");
            }
            else if (title.Length > 60)
            {
                title = title[..60] + "…";
            }

            return UiStrings.Format(
                "Sessions_HitSession",
                title,
                Session.ModifiedAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture),
                Matches.Count);
        }
    }

    /// <summary>TreeView 는 노드 내용을 ToString 으로 그린다. 형식 이름이 화면에 찍히지 않게 한다.</summary>
    public override string ToString() => Summary;
}

/// <summary>검색 결과 트리의 매칭 문장.</summary>
public sealed class SearchMatchViewModel
{
    public SearchMatchViewModel(SessionInfo session, SessionMessage message, string snippet)
    {
        Session = session;
        Message = message;
        Snippet = snippet.Replace('\n', ' ');
    }

    public SessionInfo Session { get; }

    public SessionMessage Message { get; }

    public string Snippet { get; }

    public string Label => $"[{Message.Role}] {Snippet}";

    /// <summary>TreeView 는 노드 내용을 ToString 으로 그린다. 형식 이름이 화면에 찍히지 않게 한다.</summary>
    public override string ToString() => Label;
}
