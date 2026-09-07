using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;

namespace Daiso.App.ViewModels;

/// <summary>세션 목록·검색·상세·정리. (REQUIREMENTS §5.1, §5.2)</summary>
public sealed partial class SessionsViewModel : ObservableObject
{
    private const int SearchMinimumLength = 2;

    /// <summary>타임라인에 그리는 최대 메시지 수.</summary>
    private const int TimelineLimit = 500;

    /// <summary>프로젝트 경로를 모를 때 쓰는 표식. 묶음·비교에 쓰므로 번역하지 않는다.</summary>
    internal const string UnknownProject = "(알 수 없음)";

    private readonly IndexService _indexService;
    private readonly IReadOnlyList<IProvider> _providers;
    private readonly IFileDisposer _disposer;
    private readonly ISessionExporter _exporter;
    private readonly ITerminalLauncher _launcher;
    private readonly ISettingsStore _settings;

    private List<SessionInfo> _allSessions = [];

    [ObservableProperty]
    private bool isBusy;

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
    public ObservableCollection<MessageViewModel> Timeline { get; } = [];

    /// <summary>도구 필터 항목.</summary>
    public IReadOnlyList<string> ToolFilters { get; } = [UiStrings.All, "Claude", "Codex"];

    /// <summary>기간 필터 항목.</summary>
    public IReadOnlyList<string> PeriodFilters { get; } =
    [
        UiStrings.All,
        UiStrings.Get("Sessions_Period7"),
        UiStrings.Get("Sessions_Period30"),
        UiStrings.Get("Sessions_Period90"),
    ];

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
            StatusText = UiStrings.Format("Sessions_Count", _allSessions.Count);
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

            foreach (var byProject in hits.GroupBy(hit => hit.Session.ProjectPath ?? UnknownProject))
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
        NotifySearchVisibility();
    }

    private void NotifySearchVisibility()
    {
        OnPropertyChanged(nameof(HasSearchResults));
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

    /// <summary>선택한 세션의 메시지를 읽어온다. 도구 호출은 접힌 항목으로 남긴다.</summary>
    public async Task LoadTimelineAsync(SessionInfo session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        Timeline.Clear();

        var provider = _providers.FirstOrDefault(p => p.Kind == session.Tool);
        if (provider is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var count = 0;

            await foreach (var message in provider
                .ReadMessagesAsync(session.FilePath, 0, ct)
                .ConfigureAwait(true))
            {
                Timeline.Add(new MessageViewModel(message));

                if (++count >= TimelineLimit)
                {
                    StatusText = UiStrings.Format("Sessions_TimelineLimit", TimelineLimit);
                    break;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
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
            .LaunchAsync(directory, provider.ExecutableName, provider.BuildResumeArguments(row.Session))
            .ConfigureAwait(true);

        StatusText = UiStrings.Format(
            "Sessions_ResumeStatus",
            provider.ExecutableName,
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

        ToolKind? tool = ToolFilterIndex switch
        {
            1 => ToolKind.Claude,
            2 => ToolKind.Codex,
            _ => null,
        };

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
            .GroupBy(session => session.ProjectPath ?? UnknownProject)
            .OrderByDescending(group => group.Max(session => session.ModifiedAt)))
        {
            Projects.Add(new ProjectGroupViewModel(group.Key, [.. group]));
        }

        SelectedProject = Projects.FirstOrDefault(project =>
            string.Equals(project.Path, previous, StringComparison.OrdinalIgnoreCase))
            ?? Projects.FirstOrDefault();
    }

    private void ApplyProjectSelection()
    {
        Sessions.Clear();

        if (SelectedProject is null)
        {
            return;
        }

        foreach (var session in SelectedProject.Sessions.OrderByDescending(session => session.ModifiedAt))
        {
            Sessions.Add(new SessionRowViewModel(session));
        }
    }

    partial void OnSelectedProjectChanged(ProjectGroupViewModel? value) => ApplyProjectSelection();
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

    public string DisplayName => Path == SessionsViewModel.UnknownProject
        ? UiStrings.Get("Common_Unknown")
        : System.IO.Path.GetFileName(Path.TrimEnd('\\'));

    public string Summary =>
        UiStrings.Format("Sessions_ProjectSummary", Count, DashboardViewModel.FormatSize(TotalBytes))
        + (IsOrphan ? UiStrings.Get("Sessions_OrphanSuffix") : string.Empty);
}

/// <summary>세션 목록 한 줄. (REQUIREMENTS §5.1 열)</summary>
public sealed partial class SessionRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isChecked;

    public SessionRowViewModel(SessionInfo session) => Session = session;

    public SessionInfo Session { get; }

    public string ToolIcon => Session.Tool == ToolKind.Claude ? "🟣" : "⚫";

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

    public bool IsOrphan => Session.ProjectPath is null || !Directory.Exists(Session.ProjectPath);

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
public sealed class MessageViewModel
{
    public MessageViewModel(SessionMessage message) => Message = message;

    public SessionMessage Message { get; }

    public string RoleText => Message.Role switch
    {
        MessageRole.User => "User",
        MessageRole.Assistant => "Assistant",
        MessageRole.System => "System",
        _ => "Tool",
    };

    public string TimeText => $"{Message.At.ToLocalTime():HH:mm:ss}";

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

    public string Summary => UiStrings.Format("Sessions_HitProject", Path, Sessions.Count);
}

/// <summary>검색 결과 트리의 세션 노드.</summary>
public sealed class SearchSessionViewModel
{
    public SearchSessionViewModel(SessionInfo session) => Session = session;

    public SessionInfo Session { get; }

    public ObservableCollection<SearchMatchViewModel> Matches { get; } = [];

    public string Summary =>
        UiStrings.Format(
            "Sessions_HitSession",
            Session.Tool,
            Session.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
            Matches.Count);
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
}
