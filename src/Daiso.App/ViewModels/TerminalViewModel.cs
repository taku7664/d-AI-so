using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.Core;
using Daiso.App.Strings;

namespace Daiso.App.ViewModels;

/// <summary>선택한 폴더에서 Claude 또는 Codex 터미널을 연다. (REQUIREMENTS §4)</summary>
public sealed partial class TerminalViewModel : ObservableObject
{
    private readonly IReadOnlyList<IProvider> _providers;
    private readonly ITerminalLauncher _launcher;
    private readonly ISettingsStore _settings;
    private readonly KnownProjects _knownProjects;

    [ObservableProperty]
    private string? workingDirectory;

    [ObservableProperty]
    private string? lastCommand;

    public TerminalViewModel(
        IEnumerable<IProvider> providers,
        ITerminalLauncher launcher,
        ISettingsStore settings,
        KnownProjects knownProjects)
    {
        ArgumentNullException.ThrowIfNull(knownProjects);

        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(settings);

        _providers = providers.ToList();
        _launcher = launcher;
        _settings = settings;

        _knownProjects = knownProjects;

        RecentFolders = new ObservableCollection<string>(_settings.Current.RecentFolders);

        Tools = new ObservableCollection<ToolLaunchViewModel>(
            ToolLook.InDisplayOrder(_providers, provider => provider.Kind)
                .Select(provider => new ToolLaunchViewModel(provider, PresetsFor(provider.Kind))));

        foreach (var tool in Tools)
        {
            tool.LaunchCommand = LaunchCommand;
        }

        WorkingDirectory = RecentFolders.FirstOrDefault();
        RefreshPreviews();
    }

    /// <summary>도구별 실행 줄. 탭 하나가 줄 하나다. 순서는 ToolLook.DisplayOrder.</summary>
    public ObservableCollection<ToolLaunchViewModel> Tools { get; }

    /// <summary>지금 보이는 탭.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTool))]
    private int selectedToolIndex;

    /// <summary>지금 탭의 도구 줄. 인자·프리셋·버튼·미리보기가 여기서 나온다.</summary>
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

    /// <summary>실행 파일이 PATH에 있는지 다시 본다. 화면이 열릴 때와 설치를 돌린 뒤에 부른다.</summary>
    public async Task RefreshInstalledAsync(CancellationToken ct = default)
    {
        foreach (var tool in Tools)
        {
            tool.IsInstalled = await tool.Provider.IsInstalledAsync(ct).ConfigureAwait(true);
        }

        RefreshPreviews();
    }

    private static IReadOnlyList<string> PresetsFor(ToolKind kind) => kind switch
    {
        ToolKind.Claude => ["--continue", "--resume ", "--model ", "--permission-mode "],
        ToolKind.Codex => ["resume ", "--model ", "--sandbox "],
        ToolKind.Gemini => ["--resume ", "--model ", "--sandbox"],
        _ => [],
    };

    /// <summary>앱이 이미 아는 프로젝트 폴더. 대화상자 없이 여기서 고른다.</summary>
    public ObservableCollection<string> ProjectChoices { get; } = [];

    /// <summary>인덱스·최근 폴더에서 프로젝트 목록을 다시 읽는다.</summary>
    public async Task LoadProjectChoicesAsync(CancellationToken ct = default)
    {
        var known = await _knownProjects.ListAsync(ct).ConfigureAwait(true);

        ProjectChoices.Clear();

        foreach (var path in known)
        {
            ProjectChoices.Add(path);
        }
    }

    /// <summary>최근에 연 폴더. 최신 것이 앞. 최대 10개.</summary>
    public ObservableCollection<string> RecentFolders { get; }

    /// <summary>폴더가 정해졌는지. 버튼 활성에 쓴다.</summary>
    public bool CanLaunch => !string.IsNullOrWhiteSpace(WorkingDirectory);

    /// <summary>최근 폴더가 비었는가. 안내 문구를 띄운다.</summary>
    public bool HasNoRecentFolders => RecentFolders.Count == 0;

    /// <summary>마지막 실행 기록이 있는가.</summary>
    public bool HasLastCommand => !string.IsNullOrWhiteSpace(LastCommand);

    /// <summary>폴더 유무를 도구 줄에 알린다. 열기 버튼은 폴더가 있어야 눌린다.</summary>
    private void RefreshPreviews()
    {
        foreach (var tool in Tools)
        {
            tool.HasFolder = CanLaunch;
        }
    }

    partial void OnLastCommandChanged(string? value) => OnPropertyChanged(nameof(HasLastCommand));

    /// <summary>폴더 선택 결과를 받아 히스토리에 넣는다.</summary>
    public void SetFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        WorkingDirectory = path;
        Remember(path);
        OnPropertyChanged(nameof(HasNoRecentFolders));
    }

    /// <summary>세션 "이어서 열기"에서 넘어온 인자를 그 도구 탭에 채우고 탭을 앞으로 가져온다.</summary>
    public void PrepareResume(ToolKind tool, string workingDir, string resumeArguments)
    {
        SetFolder(workingDir);
        SelectTool(tool);
        SelectedTool.Arguments = resumeArguments;
    }

    /// <summary>프리셋을 지금 탭의 인자 뒤에 붙인다.</summary>
    [RelayCommand]
    public void AppendPreset(string? preset) => SelectedTool.AppendPreset(preset ?? string.Empty);

    /// <summary>설치돼 있으면 그 도구를 열고, 아니면 새 터미널에서 설치 명령을 돌린다.</summary>
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

        if (WorkingDirectory is not { Length: > 0 } directory)
        {
            LastCommand = UiStrings.Get("Terminal_PickFolderFirst");
            return;
        }

        Remember(directory);
        LastCommand = $"{directory} > {tool.Provider.ExecutableName} {tool.Arguments}".TrimEnd();

        await _launcher.LaunchAsync(directory, tool.Provider.ExecutableName, tool.Arguments).ConfigureAwait(true);
    }

    /// <summary>설치가 끝난 것으로 볼 때까지 실행 파일을 몇 초마다 다시 찾는 최대 시간.</summary>
    private static readonly TimeSpan InstallWatch = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 새 터미널에서 설치 명령을 돌리고, 실행 파일이 PATH에 나타날 때까지 지켜본다.
    /// 나타나면 버튼이 저절로 열기로 바뀐다. 사람이 터미널을 닫아도 앱은 알 수 없으니 시간이 지나면 지켜보기를 멈춘다.
    /// </summary>
    private async Task InstallAsync(ToolLaunchViewModel tool)
    {
        var (executable, arguments) = tool.InstallParts();
        var directory = WorkingDirectory is { Length: > 0 } chosen && Directory.Exists(chosen)
            ? chosen
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        tool.IsInstalling = true;
        LastCommand = UiStrings.Format("Terminal_InstallStarted", tool.Label);

        try
        {
            await _launcher.LaunchAsync(directory, executable, arguments).ConfigureAwait(true);

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

    private void Remember(string path)
    {
        AppSettings.Remember(_settings.Current.RecentFolders, path);
        _settings.Save();

        RecentFolders.Clear();

        foreach (var folder in _settings.Current.RecentFolders)
        {
            RecentFolders.Add(folder);
        }
    }

    partial void OnWorkingDirectoryChanged(string? value)
    {
        OnPropertyChanged(nameof(CanLaunch));
        RefreshPreviews();
    }
}
