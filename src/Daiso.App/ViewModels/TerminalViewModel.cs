using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;
using Daiso.Core.Prompts;
using Daiso.Core.Sessions;

namespace Daiso.App.ViewModels;

/// <summary>새 터미널 카드의 단계. 한 번에 하나만 펼친다. (docs/TERMINAL_CARD_PLAN.md §4)</summary>
public enum TerminalStep
{
    /// <summary>셋 다 접혀 있다.</summary>
    None = -1,

    /// <summary>어떤 AI로 시작할까요?</summary>
    Tool = 0,

    /// <summary>어느 폴더에서 일할까요?</summary>
    Folder = 1,

    /// <summary>무엇부터 할까요?</summary>
    Session = 2,
}

/// <summary>
/// 새 터미널 카드의 뷰모델. 도구 → 프로젝트 폴더 → 세션(새 / 기존 이어서) → 시작할 때(프롬프트·규칙) → 옵션 인자 → 실행.
/// 내장 터미널이 기본이고 새 창은 보조. (REQUIREMENTS §4, ARCHITECTURE §5.3)
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject
{
    private readonly IReadOnlyList<IProvider> _providers;
    private readonly ITerminalLauncher _launcher;
    private readonly ISettingsStore _settings;
    private readonly KnownProjects _knownProjects;
    private readonly IndexService _index;
    private readonly IPromptLibrary _prompts;

    private readonly IUriOpener _uriOpener;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLaunch))]
    [NotifyPropertyChangedFor(nameof(HasRules))]
    [NotifyPropertyChangedFor(nameof(HasNoRules))]
    [NotifyPropertyChangedFor(nameof(RulesStatusText))]
    private string? workingDirectory;

    [ObservableProperty]
    private string? lastCommand;

    public TerminalViewModel(
        IEnumerable<IProvider> providers,
        ITerminalLauncher launcher,
        ISettingsStore settings,
        KnownProjects knownProjects,
        IndexService index,
        IPromptLibrary prompts,
        IUriOpener uriOpener)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(knownProjects);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(uriOpener);

        _providers = providers.ToList();
        _launcher = launcher;
        _settings = settings;
        _knownProjects = knownProjects;
        _index = index;
        _prompts = prompts;
        _uriOpener = uriOpener;

        Tools = new ObservableCollection<ToolLaunchViewModel>(
            ToolLook.InDisplayOrder(_providers, provider => provider.Kind)
                .Select(provider => new ToolLaunchViewModel(provider, PresetsFor(provider.Kind))));

        foreach (var tool in Tools)
        {
            tool.LaunchCommand = LaunchCommand;
            tool.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ToolLaunchViewModel.Arguments) or nameof(ToolLaunchViewModel.IsInstalled))
                {
                    OnPropertyChanged(nameof(Preview));
                    RefreshSteps(); // 설치 상태가 바뀌면 요약 줄과 시작 버튼이 같이 바뀐다
                }
            };
        }

        RecentFolders = new ObservableCollection<string>(_settings.Current.RecentFolders);
        RebuildFolderChoices();
        WorkingDirectory = RecentFolders.FirstOrDefault();
        RefreshPreviews();
        LoadPromptChoices();
    }

    // ── 도구 ──────────────────────────────────────────────────────────────

    /// <summary>도구별 실행 줄. 탭 하나가 줄 하나다. 순서는 ToolLook.DisplayOrder.</summary>
    public ObservableCollection<ToolLaunchViewModel> Tools { get; }

    /// <summary>지금 보이는 탭.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTool))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private int selectedToolIndex;

    /// <summary>지금 탭의 도구 줄. 인자·프리셋·버튼이 여기서 나온다.</summary>
    public ToolLaunchViewModel SelectedTool => Tools[Math.Clamp(SelectedToolIndex, 0, Tools.Count - 1)];

    /// <summary>도구의 탭을 앞으로 가져온다.</summary>
    public void SelectTool(ToolKind kind)
    {
        var index = Tools.ToList().FindIndex(tool => tool.Kind == kind);

        if (index >= 0)
        {
            SelectedToolIndex = index;
        }
    }

    partial void OnSelectedToolIndexChanged(int value) => _ = LoadResumeCandidatesAsync();

    /// <summary>실행 파일이 PATH에 있는지 다시 본다. 화면이 열릴 때와 설치를 돌린 뒤에 부른다.</summary>
    public async Task RefreshInstalledAsync(CancellationToken ct = default)
    {
        foreach (var tool in Tools)
        {
            tool.IsInstalled = await tool.Provider.IsInstalledAsync(ct).ConfigureAwait(true);
        }

        RefreshPreviews();

        // 모델 목록은 깔린 도구에만 한 번 묻는다. agy 는 서버에 물어 몇 초 걸리므로 화면을 붙잡지 않는다
        foreach (var tool in Tools.Where(tool => tool.IsInstalled))
        {
            _ = tool.LoadModelsAsync(ct);
        }
    }

    /// <summary>
    /// 도구별 옵션 프리셋. <b>resume·continue 계열은 넣지 않는다</b> — 3단계 "하던 대화 이어서"가 같은 일을 하고,
    /// 둘 다 있으면 어느 쪽이 이기는지 알 수 없다. (docs/TERMINAL_CARD_PLAN.md §2-B4)
    /// <para>
    /// <c>--model</c> 도 넣지 않는다 — 모델 칸이 도구가 알려 준 목록으로 같은 일을 한다(<see cref="ToolLaunchViewModel.ModelChoices"/>).
    /// </para>
    /// <para>
    /// <b>값이 필요한 플래그는 값까지 담는다.</b> 예전에는 <c>--permission-mode </c> 처럼 플래그만 붙이고 값은 사람이 적게 했는데,
    /// 일반인은 무엇을 적을지 모르고 그대로 열면 CLI 가 <c>argument missing</c> 으로 뜨지도 않는다. 되돌리기 어려운 값
    /// (<c>bypassPermissions</c> · <c>danger-full-access</c>)은 넣지 않는다 — 필요한 사람은 인자 칸에 직접 적는다.
    /// 값의 목록은 <c>claude --help</c> 2.1.266 · <c>codex --help</c> 0.153.4 로 확인했다(2026-09-10).
    /// </para>
    /// </summary>
    private static IReadOnlyList<ArgumentPreset> PresetsFor(ToolKind kind) => kind switch
    {
        ToolKind.Claude =>
        [
            new("Terminal_PresetAcceptEdits", "--permission-mode acceptEdits"),
            new("Terminal_PresetPlan", "--permission-mode plan"),
        ],
        ToolKind.Codex =>
        [
            new("Terminal_PresetReadOnly", "--sandbox read-only"),
            new("Terminal_PresetWorkspaceWrite", "--sandbox workspace-write"),
        ],
        // `agy --help` (1.1.28) 로 확인한 플래그만 둔다. `--sandbox` 는 값이 없는 스위치라 뒤에 빈칸을 붙이지 않는다
        ToolKind.Antigravity =>
        [
            new("Terminal_PresetSandbox", "--sandbox"),
        ],
        _ => [],
    };

    // ── 프로젝트 폴더 ─────────────────────────────────────────────────────

    /// <summary>최근에 연 폴더. 최신 것이 앞. 폴더 콤보박스의 목록.</summary>
    public ObservableCollection<string> RecentFolders { get; }

    /// <summary>앱이 이미 아는 프로젝트 폴더(세션 인덱스·최근 폴더).</summary>
    public ObservableCollection<string> ProjectChoices { get; } = [];

    /// <summary>
    /// 폴더 칸 하나가 보여 주는 목록 전부. 최근 연 폴더가 앞, 그 뒤에 앱이 아는 프로젝트.
    /// <para>
    /// 전에는 "프로젝트 폴더" 콤보와 "아는 프로젝트" 드롭다운이 나란히 있었다. 둘 다 폴더를 고르는 것처럼
    /// 보이는데 무엇이 다른지 라벨만으로는 알 수 없었다. 하나로 합치고, 새 폴더는 `찾아보기`로만 간다.
    /// (docs/TERMINAL_CARD_PLAN.md §2-B6 · §5)
    /// </para>
    /// </summary>
    public ObservableCollection<string> FolderChoices { get; } = [];

    /// <summary>인덱스·최근 폴더에서 프로젝트 목록을 다시 읽고 폴더 칸 목록을 새로 만든다.</summary>
    public async Task LoadProjectChoicesAsync(CancellationToken ct = default)
    {
        var known = await _knownProjects.ListAsync(ct).ConfigureAwait(true);

        ProjectChoices.Clear();

        foreach (var path in known)
        {
            ProjectChoices.Add(path);
        }

        RebuildFolderChoices();
    }

    /// <summary>최근 폴더 + 아는 프로젝트를 겹치지 않게 한 목록으로 만든다.</summary>
    private void RebuildFolderChoices()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keep = WorkingDirectory;

        FolderChoices.Clear();

        foreach (var path in RecentFolders.Concat(ProjectChoices))
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
            {
                FolderChoices.Add(path);
            }
        }

        // 목록을 비우는 동안 편집형 콤보의 글이 지워질 수 있다
        WorkingDirectory = keep;
        OnPropertyChanged(nameof(WorkingDirectory));
    }

    /// <summary>경로를 적었는데 그런 폴더가 없다. 다음 단계로 못 넘어가는 이유를 그 자리에서 말한다.</summary>
    public bool FolderMissing => !string.IsNullOrWhiteSpace(WorkingDirectory) && !Directory.Exists(WorkingDirectory);

    /// <summary>폴더가 정해졌고 실제로 있는가. 열기 버튼 활성에 쓴다.</summary>
    public bool CanLaunch => !string.IsNullOrWhiteSpace(WorkingDirectory) && Directory.Exists(WorkingDirectory);

    /// <summary>폴더 선택 결과를 받아 히스토리에 넣는다.</summary>
    public void SetFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // 사람이 고른 것이다. 기억해 둔 기본값과 구분한다
        FolderChosen = true;
        WorkingDirectory = path;
        Remember(path);

        // 고른 값이 이미 들어 있던 값과 같으면 WorkingDirectory 가 안 바뀌어 알림이 오지 않는다.
        // 그때도 답한 것이므로 여기서 직접 넘긴다
        AdvanceFromFolder();
    }

    /// <summary>미리 채워 둔 폴더를 그대로 쓰겠다고 확인한다.</summary>
    [RelayCommand]
    public void ConfirmFolder() => SetFolder(WorkingDirectory ?? string.Empty);

    /// <summary>폴더 답이 끝났으면 바로 아래 다음 단계를 연다. 폴더 단계는 접지 않는다.</summary>
    private void AdvanceFromFolder()
    {
        if (FolderDone && FolderExpanded)
        {
            OpenStep(TerminalStep.Session);
        }

        RefreshSteps();
    }

    partial void OnWorkingDirectoryChanged(string? value)
    {
        RefreshPreviews();
        _ = LoadResumeCandidatesAsync();

        // 폴더가 바뀌면 그 폴더의 세션 목록이 통째로 달라진다. 고른 세션은 못 쓴다
        InvalidateSession("Terminal_StepInvalidByFolder");

        AdvanceFromFolder();
    }

    /// <summary>폴더 유무를 도구 줄에 알린다. 열기 버튼은 폴더가 있어야 눌린다.</summary>
    private void RefreshPreviews()
    {
        foreach (var tool in Tools)
        {
            tool.HasFolder = CanLaunch;
        }

        OnPropertyChanged(nameof(Preview));
    }

    private void Remember(string path)
    {
        AppSettings.Remember(_settings.Current.RecentFolders, path);
        _settings.Save();

        // 콤보박스 목록을 설정과 맞춘다. 지금 고른 값은 그대로 두어 텍스트가 튀지 않게 한다
        var keep = WorkingDirectory;
        RecentFolders.Clear();

        foreach (var folder in _settings.Current.RecentFolders)
        {
            RecentFolders.Add(folder);
        }

        WorkingDirectory = keep;
        OnPropertyChanged(nameof(WorkingDirectory)); // 값이 같아도 콤보박스 글을 다시 맞춘다(목록을 비우는 동안 글이 지워질 수 있다)

        RebuildFolderChoices();
    }

    // ── 세션: 새 세션 / 기존 세션 이어서 ────────────────────────────────────

    /// <summary>0 = 새 세션, 1 = 기존 세션 이어서. RadioButtons가 이 값에 바로 묶인다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewSession))]
    [NotifyPropertyChangedFor(nameof(IsResume))]
    [NotifyPropertyChangedFor(nameof(StartButtonText))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private int sessionModeIndex;

    public bool IsNewSession => SessionModeIndex == 0;

    public bool IsResume => SessionModeIndex == 1;

    /// <summary>
    /// 이 폴더·도구의 지난 세션. 최근 것이 앞. "하던 대화 이어서"를 골랐을 때 대화 칸(콤보박스)에 보인다.
    /// <para>
    /// 예전에는 검색칸 + 날짜로 묶은 목록이었다. 답한 단계가 펼친 채로 쌓이게 되면서 목록이 카드 높이를 먹었고,
    /// 폴더 칸처럼 콤보박스 하나로 바꿨다(2026-09-10 사람의 요청). 최근 30개만 담으므로 검색 없이 훑을 수 있다.
    /// </para>
    /// </summary>
    public ObservableCollection<ResumeCandidateViewModel> ResumeCandidates { get; } = [];

    /// <summary>"이 폴더의 대화 N개". 세어 봤다는 것을 보여 주면 앱이 내 폴더를 안다는 신호가 된다.</summary>
    public string ResumeCountText => UiStrings.Format("Terminal_ResumeCount", ResumeCandidates.Count);

    /// <summary>이 폴더에 이어서 열 대화가 하나라도 있는가. 없으면 "하던 대화 이어서" 카드를 잠근다.</summary>
    public bool HasAnyResume => ResumeCandidates.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private ResumeCandidateViewModel? selectedResume;

    /// <summary>목록이 비었는가(안내 문구).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoResumeCandidatesText))]
    private bool hasNoResumeCandidates;

    public string NoResumeCandidatesText => UiStrings.Format("Terminal_NoSessionsHere", SelectedTool.Label);

    /// <summary>세션·요약 "이어서 열기"가 넘긴 resume 인자. 목록에서 같은 세션을 찾으면 그것을 고르고, 못 찾아도 이 인자로 연다.</summary>
    private string? _preparedResumeArguments;

    private int _resumeLoadVersion;

    /// <summary>지금 도구·폴더의 세션을 인덱스에서 읽어 목록을 채운다. 폴더나 탭이 바뀔 때마다.</summary>
    public async Task LoadResumeCandidatesAsync(CancellationToken ct = default)
    {
        var version = ++_resumeLoadVersion;
        var tool = SelectedTool;
        var directory = WorkingDirectory;

        ResumeCandidates.Clear();
        SelectedResume = null;
        HasNoResumeCandidates = false;
        OnPropertyChanged(nameof(NoResumeCandidatesText));
        OnPropertyChanged(nameof(ResumeCountText));
        OnPropertyChanged(nameof(HasAnyResume));

        if (string.IsNullOrWhiteSpace(directory))
        {
            HasNoResumeCandidates = true;
            return;
        }

        IReadOnlyList<SessionInfo> sessions;

        try
        {
            sessions = await _index.Index
                .ListAsync(new SessionFilter(tool.Kind, directory, null, null, false, null, IncludeArchived: false), ct)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            sessions = [];
        }

        if (version != _resumeLoadVersion)
        {
            return; // 그 사이 폴더·탭이 또 바뀌었다
        }

        foreach (var session in sessions.OrderByDescending(session => session.ModifiedAt).Take(30))
        {
            ResumeCandidates.Add(new ResumeCandidateViewModel(session, tool.Provider.BuildResumeArguments(session)));
        }

        HasNoResumeCandidates = ResumeCandidates.Count == 0;
        OnPropertyChanged(nameof(ResumeCountText));
        OnPropertyChanged(nameof(HasAnyResume));

        if (_preparedResumeArguments is { } prepared)
        {
            SelectedResume = ResumeCandidates.FirstOrDefault(candidate => candidate.ResumeArguments == prepared);
        }
        else if (IsResume)
        {
            SelectedResume = ResumeCandidates.FirstOrDefault();
        }
    }

    partial void OnSelectedResumeChanged(ResumeCandidateViewModel? value)
    {
        if (value is not null)
        {
            _preparedResumeArguments = null; // 사람이 직접 골랐다
        }

        RefreshSteps();
    }

    partial void OnSessionModeIndexChanged(int value)
    {
        if (value == 1 && SelectedResume is null)
        {
            SelectedResume = ResumeCandidates.FirstOrDefault();
        }
    }

    /// <summary>이어서 열 때 붙는 인자. 새 세션이면 빈 문자열.</summary>
    private string ResumeArguments => IsResume
        ? (SelectedResume?.ResumeArguments ?? _preparedResumeArguments ?? string.Empty)
        : string.Empty;

    // ── 시작할 때: 프롬프트·규칙 (새 세션) ───────────────────────────────────

    /// <summary>고를 수 있는 프롬프트. 첫 항목은 "프롬프트 없음".</summary>
    public ObservableCollection<PromptChoiceViewModel> PromptChoices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private PromptChoiceViewModel? selectedPrompt;

    /// <summary>기본 제공 + 내 보관함을 이름순으로. 같은 id면 내 것이 이긴다.</summary>
    public void LoadPromptChoices()
    {
        var current = SelectedPrompt?.Preset?.Id;

        PromptChoices.Clear();
        PromptChoices.Add(PromptChoiceViewModel.None);

        foreach (var preset in BuiltInPrompts.List()
            .Concat(_prompts.List())
            .GroupBy(prompt => prompt.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(prompt => prompt.Name, StringComparer.CurrentCulture))
        {
            PromptChoices.Add(new PromptChoiceViewModel(preset));
        }

        SelectedPrompt = PromptChoices.FirstOrDefault(choice => choice.Preset?.Id == current) ?? PromptChoices[0];
    }

    /// <summary>프로젝트 폴더에 규칙 파일(PROJECT_RULES.daiso)이 있는가.</summary>
    public bool HasRules => CanLaunch && File.Exists(Path.Combine(WorkingDirectory!, InstructionTemplate.DefaultRulesFileName));

    /// <summary>규칙 파일이 없다. 아이콘을 갈라 그리려고 따로 둔다(XAML은 부정을 못 쓴다).</summary>
    public bool HasNoRules => !HasRules;

    public string RulesStatusText => UiStrings.Get(HasRules ? "Terminal_RulesPresent" : "Terminal_RulesAbsent");

    // ── 단계 ──────────────────────────────────────────────────────────────
    //
    // 이 화면에서 사람이 정하는 것은 셋뿐이다: 어떤 AI로 · 어느 폴더에서 · 새로/이어서.
    // 단계는 답을 따라서만 열린다. 필수 답이 채워지면 바로 아래 다음 단계가 열리고, 화면이 그 단계로 부드럽게 굴러가 답할 칸에 포커스가 옮겨 간다.
    // 한 번 열린 단계는 닫히지 않는다. 사람이 머리를 눌러 접거나 펴지 않는다 — 답한 단계가 사라지면 흐름을 잃는다.
    // (2026-09-10 사람의 요청으로 "한 번에 한 단계만 펼치는" 아코디언을 걷어냈다 — docs/TERMINAL_CARD_PLAN.md 끝)

    /// <summary>1단계(어떤 AI로)가 펼쳐져 있는가. 처음에는 이것만 열려 있다.</summary>
    [ObservableProperty]
    private bool toolOpen = true;

    /// <summary>2단계(어느 폴더에서)가 펼쳐져 있는가.</summary>
    [ObservableProperty]
    private bool folderOpen;

    /// <summary>3단계(무엇부터)가 펼쳐져 있는가.</summary>
    [ObservableProperty]
    private bool sessionOpen;

    /// <summary>
    /// 단계 하나가 열렸으니 그 자리로 굴려 달라. 화면이 받아 부드럽게 굴리고 그 단계 머리에 포커스를 둔다.
    /// 뷰모델은 스크롤을 모른다 — 무엇이 열렸는지만 알린다.
    /// </summary>
    public event EventHandler<TerminalStep>? StepRevealRequested;

    /// <summary>사람이 AI를 골랐는가. 처음에는 아무것도 안 고른 상태로 연다.</summary>
    [ObservableProperty]
    private bool toolChosen;

    /// <summary>
    /// 사람이 폴더를 골랐는가. <see cref="WorkingDirectory"/>는 <b>마지막에 쓴 폴더로 미리 채워지므로</b>
    /// "값이 있다"와 "사람이 답했다"가 다르다 — 그걸 구분하지 않아서, AI만 고르면 폴더 단계가
    /// 답이 된 것처럼 통째로 건너뛰어졌다. <see cref="ToolChosen"/>·<see cref="SessionModeChosen"/>과 같은 이유다.
    /// </summary>
    [ObservableProperty]
    private bool folderChosen;

    /// <summary>사람이 새로/이어서를 골랐는가. <see cref="SessionModeIndex"/>는 기본값이 있어 이것과 따로 본다.</summary>
    [ObservableProperty]
    private bool sessionModeChosen;

    /// <summary>앞 단계를 바꿔 뒷 단계 답이 무효가 된 이유. 조용히 지우면 "왜 없어졌지"가 된다.</summary>
    [ObservableProperty]
    private string? invalidationNotice;

    public bool HasInvalidationNotice => !string.IsNullOrEmpty(InvalidationNotice);

    public bool ToolDone => ToolChosen;

    public bool FolderDone => ToolDone && FolderChosen && CanLaunch;

    /// <summary>미리 채워 둔 폴더가 있는데 아직 확인받지 못했다. 그 자리에 확인 단추를 낸다.</summary>
    public bool FolderNeedsConfirm => FolderExpanded && CanLaunch && !FolderChosen;

    public bool SessionDone => FolderDone && SessionModeChosen && (IsNewSession || SelectedResume is not null);

    public bool ToolExpanded => ToolOpen;

    public bool FolderExpanded => FolderOpen;

    public bool SessionExpanded => SessionOpen;

    /// <summary>아직 오지 않은 단계는 흐리게 두고 누를 수 없게 한다. 숨기지는 않는다 — 몇 개 남았는지 보여야 안심한다.</summary>
    public bool FolderReachable => ToolDone;

    public bool SessionReachable => FolderDone;

    // 머리 왼쪽 동그라미. 셋 중 하나만 참이다: 끝남(✓) · 지금 답하는 중(●) · 아직(○).
    // 답한 단계도 펼친 채로 두므로 "끝남"은 펼침과 상관없이 답이 됐는가만 본다
    public bool ToolMarkCheck => ToolDone;

    public bool ToolMarkCurrent => !ToolDone && ToolExpanded;

    public bool ToolMarkPending => !ToolDone && !ToolExpanded;

    public bool FolderMarkCheck => FolderDone;

    public bool FolderMarkCurrent => !FolderDone && FolderExpanded;

    public bool FolderMarkPending => !FolderDone && !FolderExpanded;

    public bool SessionMarkCheck => SessionDone;

    public bool SessionMarkCurrent => !SessionDone && SessionExpanded;

    public bool SessionMarkPending => !SessionDone && !SessionExpanded;

    // 4단계 (선택) 세부 설정: 답할 것이 없어 ✓ 가 없다. 3단계와 함께 열리면 ●, 그 전에는 ○.
    // 시작 버튼(CanStart)은 이 단계를 보지 않는다 — 아무것도 안 골라도 열 수 있어야 한다
    public bool AdvancedMarkCurrent => SessionExpanded;

    public bool AdvancedMarkPending => !SessionExpanded;
    /// <summary>라디오에 물리는 값. 아직 안 골랐으면 −1이라 아무것도 켜지지 않는다.</summary>
    public int SessionModeSelection => SessionModeChosen ? SessionModeIndex : -1;

    /// <summary>AI 카드 목록에 물리는 값. 아직 안 골랐으면 −1이라 아무 카드도 켜지지 않는다.</summary>
    public int ToolSelection => ToolChosen ? SelectedToolIndex : -1;

    // 라디오가 아무것도 안 켜져 있는데 "새로 시작"의 하위 내용(프롬프트)이 보이면,
    // 고르지도 않은 것의 속을 미리 펼쳐 놓은 셈이라 모순이다. 고른 뒤에만 보인다
    public bool ShowNewSessionOptions => SessionModeChosen && IsNewSession;

    public bool ShowResumeList => SessionModeChosen && IsResume;

    // 아직 못 가는 단계는 흐리게 + 누를 수 없게 한다. IsEnabled 를 쓰면 WinUI 가 비활성 배경을 칠해
    // "흐린 줄"이 아니라 "회색 덩어리"가 되어 오히려 눈에 띈다
    public double FolderHeaderOpacity => FolderReachable ? 1.0 : 0.4;

    public double SessionHeaderOpacity => SessionReachable ? 1.0 : 0.4;

    /// <summary>
    /// 기본 버튼 글. "새로 시작할지 이어서 할지 안 골랐어요"라고 해 놓고 버튼이 `터미널로 열기`면 말이 어긋난다.
    /// 무엇을 하는 버튼인지 그대로 적는다. 설치가 안 됐으면 설치 흐름의 글을 그대로 쓴다.
    /// </summary>
    public string StartButtonText
    {
        get
        {
            var tool = SelectedTool;

            if (!tool.IsInstalled || tool.IsInstalling)
            {
                return tool.EmbeddedButtonText;
            }

            // 무효화로 선택이 풀려도 SessionModeIndex 는 1로 남는다. "안 골랐다"고 하면서 버튼이
            // `이어서 열기`면 또 말이 어긋난다 — 고른 적이 있을 때만 이어서로 부른다
            return UiStrings.Get(ShowResumeList ? "Terminal_ResumeHere" : "Terminal_StartHere");
        }
    }

    /// <summary>실행될 명령을 보여도 되는가. 도구를 고르기 전에는 남의 명령을 보여 주는 셈이라 감춘다.</summary>
    public bool ShowPreview => ToolDone;

    /// <summary>실행 줄에 띄우는 "무엇이 남았나" 한 문장. 다 채워졌으면 빈 문자열.</summary>
    public string RemainingHint
    {
        get
        {
            if (!ToolDone)
            {
                return UiStrings.Get("Terminal_StepNeedTool");
            }

            // 안 깔린 도구는 다음 할 일이 "설치"다. 남은 단계를 재촉하면 설치 버튼 위에서 딴 말을 하는 셈이다 —
            // 무엇을 해야 하는지는 아래 설치 안내 한 줄이 말한다
            if (!SelectedTool.IsInstalled)
            {
                return string.Empty;
            }

            if (!FolderDone)
            {
                return UiStrings.Get("Terminal_StepNeedFolder");
            }

            if (!SessionModeChosen)
            {
                return UiStrings.Get("Terminal_StepNeedMode");
            }

            return SessionDone ? string.Empty : UiStrings.Get("Terminal_StepNeedSession");
        }
    }

    public bool HasRemainingHint => RemainingHint.Length > 0;

    /// <summary>필수 셋이 다 찼고 도구도 누를 수 있는가.</summary>
    /// <summary>
    /// 시작 버튼을 누를 수 있는가. <b>안 깔린 도구는 이 버튼이 설치 버튼</b>이므로 3단계(무엇부터 할까요?)를 묻지 않는다 —
    /// 설치에는 세션 선택이 필요 없는데 잠가 두면, 깔려고 온 사람이 쓸 수 없는 단계부터 답해야 한다.
    /// </summary>
    public bool CanStart => SelectedTool.CanPress && (SessionDone || !SelectedTool.IsInstalled);

    /// <summary>AI를 고른다. 탭·카드 어느 쪽에서 부르든 여기로 온다.</summary>
    public void ChooseTool(int index)
    {
        var changed = SelectedToolIndex != index;
        SelectedToolIndex = index;
        ToolChosen = true;

        if (changed)
        {
            InvalidateSession("Terminal_StepInvalidByTool");
        }

        // 1단계는 접지 않는다. 폴더가 이미 답해져 있으면 그다음 단계로 바로 간다
        OpenStep(FolderDone ? TerminalStep.Session : TerminalStep.Folder);
        RefreshSteps();
    }

    /// <summary>새로 시작 / 이어서를 고른다. 3단계는 이미 열려 있고, 몸이 한 줄(프롬프트·대화 칸)만 늘어서 굴리지 않는다.</summary>
    public void ChooseSessionMode(int mode)
    {
        SessionModeIndex = mode;
        SessionModeChosen = true;
        InvalidationNotice = null;
        RefreshSteps();
    }

    /// <summary>
    /// 단계를 열고, 화면에 그 자리로 굴려 달라고 알린다.
    /// 이미 열려 있어도 알린다 — 앞 답을 바꿔 다음 단계를 다시 봐야 할 때가 있다.
    /// </summary>
    private void OpenStep(TerminalStep step)
    {
        switch (step)
        {
            case TerminalStep.Tool:
                ToolOpen = true;
                break;
            case TerminalStep.Folder:
                FolderOpen = true;
                break;
            case TerminalStep.Session:
                SessionOpen = true;
                break;
            default:
                return;
        }

        StepRevealRequested?.Invoke(this, step);
    }

    /// <summary>앞 단계가 바뀌어 세션 선택이 못 쓰게 됐다. 이유를 남기고 그 단계를 다시 연다.</summary>
    private void InvalidateSession(string reasonKey)
    {
        if (!SessionModeChosen && SelectedResume is null)
        {
            return;
        }

        SessionModeChosen = false;
        SelectedResume = null;
        InvalidationNotice = UiStrings.Get(reasonKey);
    }

    /// <summary>단계에서 나오는 값들을 한꺼번에 다시 읽게 한다. 갈래가 많아 개별 특성으로 엮지 않는다.</summary>
    private void RefreshSteps()
    {
        foreach (var name in StepDependentProperties)
        {
            OnPropertyChanged(name);
        }
    }

    private static readonly string[] StepDependentProperties =
    [
        nameof(ToolDone), nameof(FolderDone), nameof(SessionDone), nameof(FolderNeedsConfirm),
        nameof(ToolExpanded), nameof(FolderExpanded), nameof(SessionExpanded),
        nameof(FolderReachable), nameof(SessionReachable),
        nameof(ToolMarkCheck), nameof(ToolMarkCurrent), nameof(ToolMarkPending),
        nameof(FolderMarkCheck), nameof(FolderMarkCurrent), nameof(FolderMarkPending),
        nameof(SessionMarkCheck), nameof(SessionMarkCurrent), nameof(SessionMarkPending),
        nameof(AdvancedMarkCurrent), nameof(AdvancedMarkPending),
        nameof(SessionModeSelection), nameof(ToolSelection),
        nameof(ShowNewSessionOptions), nameof(ShowResumeList),
        nameof(FolderHeaderOpacity), nameof(SessionHeaderOpacity),
        nameof(ShowPreview), nameof(StartButtonText),
        nameof(Preview), nameof(PreviewSentence), nameof(HasPreviewSentence),
        nameof(RemainingHint), nameof(HasRemainingHint), nameof(CanStart),
        nameof(HasInvalidationNotice), nameof(HasRules), nameof(HasNoRules), nameof(RulesStatusText),
        nameof(FolderMissing),
    ];

    partial void OnToolOpenChanged(bool value) => RefreshSteps();

    partial void OnFolderOpenChanged(bool value) => RefreshSteps();

    partial void OnSessionOpenChanged(bool value) => RefreshSteps();

    partial void OnToolChosenChanged(bool value) => RefreshSteps();

    partial void OnSessionModeChosenChanged(bool value) => RefreshSteps();

    partial void OnInvalidationNoticeChanged(string? value) => OnPropertyChanged(nameof(HasInvalidationNotice));

    // ── 실행 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 실제로 실행될 명령 한 줄. resume 인자 + 사용자 인자 + 프롬프트 시작 메시지.
    /// <para>
    /// <b>도구 이름 접두어를 붙이지 않는다.</b> 이 값은 복사 버튼이 그대로 클립보드에 넣는 것이라,
    /// `Claude ▸ ` 같은 장식이 붙으면 붙여넣어도 실행되지 않는다. 무엇을 여는지는
    /// <see cref="PreviewSentence"/>가 사람 말로 따로 말한다. (docs/TERMINAL_CARD_PLAN.md §2-D1)
    /// </para>
    /// </summary>
    public string Preview
    {
        get
        {
            var tool = SelectedTool;

            if (!tool.IsInstalled)
            {
                return tool.InstallOpensPage ? tool.Provider.InstallUri! : tool.Provider.InstallCommand;
            }

            var arguments = ComposeArguments(tool, writePrompt: false);
            return $"{tool.Provider.LaunchTarget}{(arguments.Length > 0 ? " " + arguments : string.Empty)}";
        }
    }

    /// <summary>
    /// 무슨 일이 일어나는지 사람 말 한 문장. 명령보다 크게, 명령보다 먼저 읽힌다.
    /// 지금까지 이 화면에서 유일하게 "무엇이 실행되는가"를 알려 주는 곳은 버튼 옆 회색 잔글씨였다.
    /// </summary>
    public string PreviewSentence
    {
        get
        {
            var tool = SelectedTool;

            if (!tool.IsInstalled)
            {
                return UiStrings.Format(
                    tool.InstallOpensPage ? "Terminal_SentenceInstallPage" : "Terminal_SentenceInstall",
                    tool.Label);
            }

            if (!SessionDone)
            {
                return string.Empty;
            }

            var folder = Path.GetFileName((WorkingDirectory ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar));

            return IsResume
                ? UiStrings.Format("Terminal_SentenceResume", tool.Label, folder, SelectedResume!.When)
                : UiStrings.Format("Terminal_SentenceNew", tool.Label, folder);
        }
    }

    public bool HasPreviewSentence => PreviewSentence.Length > 0;

    /// <summary>
    /// 이번 실행의 인자를 만든다. <paramref name="writePrompt"/>가 참이면 고른 프롬프트를 프로젝트 docs/prompts에 써 넣고
    /// 시작 메시지를 첫 메시지 인자로 붙인다(Claude·Codex는 위치 인자, Antigravity는 -i). 미리보기는 쓰지 않고 모양만 본다.
    /// </summary>
    public string ComposeArguments(ToolLaunchViewModel tool, bool writePrompt)
    {
        var parts = new List<string>();

        if (ResumeArguments is { Length: > 0 } resume)
        {
            parts.Add(resume.Trim());
        }

        if (tool.Arguments.Trim() is { Length: > 0 } user)
        {
            parts.Add(user);
        }

        if (IsNewSession && SelectedPrompt?.Preset is { } preset && CanLaunch)
        {
            if (writePrompt)
            {
                _prompts.WriteIntoProject(preset, WorkingDirectory!);
            }

            var message = PromptPresetSerializer.StarterMessage(preset).Replace('"', '\'');

            // Antigravity 는 `-i`(= `--prompt-interactive`): 첫 프롬프트를 던지고 <b>대화를 이어 간다</b>.
            // `-p`(= `--print`)로 착각하기 쉬운데 그것은 한 번 답하고 끝나는 비대화 모드라, 방을 열어 두는 이 화면과 맞지 않는다.
            // `agy --help` (1.1.28) 로 확인했다
            parts.Add(tool.Kind == ToolKind.Antigravity ? $"-i \"{message}\"" : $"\"{message}\"");
        }

        return string.Join(' ', parts);
    }

    /// <summary>마지막 실행 기록이 있는가.</summary>
    public bool HasLastCommand => !string.IsNullOrWhiteSpace(LastCommand);

    partial void OnLastCommandChanged(string? value) => OnPropertyChanged(nameof(HasLastCommand));

    /// <summary>세션 "이어서 열기"가 방을 바로 열어 달라고 요청했는가. TerminalPage가 한 번 소비한다.</summary>
    private bool _autoOpen;

    private ToolKind? _autoOpenTool;

    /// <summary>세션 "이어서 열기"에서 넘어온 도구·폴더·resume 인자를 심고 "기존 세션 이어서"로 둔다.</summary>
    /// <param name="autoOpen">true면 화면이 뜨는 즉시 방(내장 터미널)을 연다.</param>
    public void PrepareResume(ToolKind tool, string workingDir, string resumeArguments, bool autoOpen = false)
    {
        _preparedResumeArguments = resumeArguments;
        SelectTool(tool);
        SetFolder(workingDir);
        SessionModeIndex = 1;
        _autoOpen = autoOpen;
        _autoOpenTool = tool;

        // 세 단계가 이미 채워진 채로 도착한다. 단계는 사람이 접고 펴지 않으므로 셋 다 열어 두고 3단계로 굴린다 —
        // 앞 두 단계를 접어 두면 거기로 돌아갈 길이 없다
        ToolChosen = true;
        SessionModeChosen = true;
        InvalidationNotice = null;
        ToolOpen = true;
        FolderOpen = true;
        OpenStep(TerminalStep.Session);

        OnPropertyChanged(nameof(Preview));
        RefreshSteps();
    }

    /// <summary>자동 열기 요청을 한 번만 꺼내 온다(다시 부르면 false). 화면 로드 때 탭 기본 선택이 도구를 덮어쓴 것을 되돌린다.</summary>
    public bool ConsumeAutoOpen()
    {
        var value = _autoOpen;
        _autoOpen = false;

        if (value && _autoOpenTool is { } tool)
        {
            SelectTool(tool);
            SessionModeIndex = 1;
        }

        return value;
    }

    /// <summary>프리셋을 지금 탭의 인자 뒤에 붙인다.</summary>
    [RelayCommand]
    public void AppendPreset(string? preset) => SelectedTool.AppendPreset(preset ?? string.Empty);

    /// <summary>새 창(외부 터미널)으로 연다. 설치돼 있지 않으면 설치 명령을 돌린다.</summary>
    [RelayCommand]
    public async Task LaunchAsync(ToolLaunchViewModel? tool)
    {
        if (tool is null)
        {
            return;
        }

        if (!tool.IsInstalled)
        {
            await InstallAsync(tool).ConfigureAwait(true);
            return;
        }

        if (!CanLaunch)
        {
            LastCommand = UiStrings.Get("Terminal_PickFolderFirst");
            return;
        }

        var directory = WorkingDirectory!;
        Remember(directory);
        var arguments = ComposeArguments(tool, writePrompt: true);
        LastCommand = $"{directory} > {tool.Provider.LaunchTarget} {arguments}".TrimEnd();

        await _launcher.LaunchAsync(directory, tool.Provider.LaunchTarget, arguments).ConfigureAwait(true);
    }

    /// <summary>설치가 끝난 것으로 볼 때까지 실행 파일을 몇 초마다 다시 찾는 최대 시간.</summary>
    private static readonly TimeSpan InstallWatch = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 설치를 시작하고, 실행 파일이 PATH에 나타날 때까지 지켜본다.
    /// 나타나면 버튼이 저절로 열기로 바뀐다. 사람이 창을 닫아도 앱은 알 수 없으니 시간이 지나면 지켜보기를 멈춘다.
    /// <para>
    /// 시작하는 방법은 도구마다 다르다. npm 도구는 새 터미널에서 설치 명령을 돌리고,
    /// <see cref="ToolLaunchViewModel.InstallOpensPage"/> 인 도구(Antigravity)는 공식 안내 페이지를 브라우저로 연다 —
    /// 앱이 원격 스크립트를 대신 돌리지 않는다(<see cref="IProvider.InstallUri"/>).
    /// 어느 쪽이든 지켜보기는 같다. 사람이 딴 데서 깔아도 버튼이 따라온다.
    /// </para>
    /// </summary>
    public async Task InstallAsync(ToolLaunchViewModel tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        tool.IsInstalling = true;
        LastCommand = UiStrings.Format(
            tool.InstallOpensPage ? "Terminal_InstallPageOpened" : "Terminal_InstallStarted",
            tool.Label);

        try
        {
            if (tool.InstallOpensPage)
            {
                _uriOpener.Open(tool.Provider.InstallUri!);
            }
            else
            {
                var (executable, arguments) = tool.InstallParts();
                var directory = WorkingDirectory is { Length: > 0 } chosen && Directory.Exists(chosen)
                    ? chosen
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                await _launcher.LaunchAsync(directory, executable, arguments).ConfigureAwait(true);
            }

            var deadline = DateTimeOffset.UtcNow + InstallWatch;

            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

                if (await tool.Provider.IsInstalledAsync(default).ConfigureAwait(true))
                {
                    tool.IsInstalled = true;
                    RefreshPreviews();
                    LastCommand = UiStrings.Format("Terminal_InstallDetected", tool.Label);
                    return;
                }
            }

            LastCommand = UiStrings.Format("Terminal_InstallTimeout", tool.Label);
        }
        finally
        {
            tool.IsInstalling = false;
        }
    }
}

/// <summary>기존 세션 목록의 한 줄. 시각 · 첫 프롬프트.</summary>
public sealed class ResumeCandidateViewModel
{
    public ResumeCandidateViewModel(SessionInfo session, string resumeArguments)
    {
        Session = session;
        ResumeArguments = resumeArguments;
    }

    public SessionInfo Session { get; }

    /// <summary>이 세션을 이어서 열 때 붙는 인자(예: --resume id).</summary>
    public string ResumeArguments { get; }

    /// <summary>
    /// 읽히는 시각. <c>09-09 15:02</c>·<c>09-09 15:00</c> 처럼 절대 시각만 보여 주면
    /// 2분 차이 나는 두 세션 중 어느 것이 최근인지 눈으로 못 가린다. 갈래는 <see cref="SessionMoment"/>가 정한다.
    /// </summary>
    public string When
    {
        get
        {
            var local = Session.ModifiedAt.ToLocalTime();
            var moment = SessionMoment.Of(local, DateTimeOffset.Now);

            return moment.Kind switch
            {
                SessionMomentKind.JustNow => UiStrings.Get("Time_JustNow"),
                SessionMomentKind.MinutesAgo => UiStrings.Format("Time_MinutesAgo", moment.Value),
                SessionMomentKind.HoursAgo => UiStrings.Format("Time_HoursAgo", moment.Value),
                SessionMomentKind.Yesterday => UiStrings.Format("Time_Yesterday", Format(local, "HH:mm")),
                _ => Format(local, "MM-dd HH:mm"),
            };
        }
    }

    /// <summary>
    /// 목록에 보이는 제목. 정제 규칙은 <see cref="SessionTitle"/>가 정본이다 — 세션 화면도 같은 것을 쓴다.
    /// 첫 메시지가 없으면 세션 id 대신 문구를 보여 준다. uuid 는 사람이 세션을 가리지 못한다
    /// </summary>
    public string Summary
    {
        get
        {
            var cleaned = SessionTitle.Clean(Session.FirstPrompt);

            if (cleaned.Length == 0)
            {
                return UiStrings.Get("Common_NoPrompt");
            }

            return cleaned.Length > 80 ? cleaned[..80] + "…" : cleaned;
        }
    }

    /// <summary>주고받은 횟수 합. 내역은 <see cref="CountsTip"/>에 둔다.</summary>
    public string Counts => UiStrings.Format(
        "Terminal_SessionCounts",
        Session.UserMessageCount + Session.AssistantMessageCount);

    public string CountsTip => UiStrings.Format(
        "Terminal_SessionCountsTip",
        Session.UserMessageCount,
        Session.AssistantMessageCount);

    private static string Format(DateTimeOffset value, string pattern) =>
        value.ToString(pattern, System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>프롬프트 선택 한 줄. <see cref="Preset"/>이 null이면 "프롬프트 없음".</summary>
public sealed class PromptChoiceViewModel
{
    public static readonly PromptChoiceViewModel None = new(null);

    public PromptChoiceViewModel(PromptPreset? preset) => Preset = preset;

    public PromptPreset? Preset { get; }

    public string Name => Preset?.Name ?? UiStrings.Get("Terminal_PromptNone");
}
