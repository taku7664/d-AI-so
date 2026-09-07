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

    [ObservableProperty]
    private string? workingDirectory;

    [ObservableProperty]
    private string arguments = string.Empty;

    [ObservableProperty]
    private string? lastCommand;

    public TerminalViewModel(
        IEnumerable<IProvider> providers,
        ITerminalLauncher launcher,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(settings);

        _providers = providers.ToList();
        _launcher = launcher;
        _settings = settings;

        RecentFolders = new ObservableCollection<string>(_settings.Current.RecentFolders);
        WorkingDirectory = RecentFolders.FirstOrDefault();
    }

    /// <summary>최근에 연 폴더. 최신 것이 앞. 최대 10개.</summary>
    public ObservableCollection<string> RecentFolders { get; }

    /// <summary>Claude 인자 프리셋. (REQUIREMENTS §4)</summary>
    public IReadOnlyList<string> ClaudePresets { get; } =
        ["--continue", "--resume ", "--model ", "--permission-mode "];

    /// <summary>Codex 인자 프리셋.</summary>
    public IReadOnlyList<string> CodexPresets { get; } =
        ["resume ", "--model ", "--sandbox "];

    /// <summary>폴더가 정해졌는지. 버튼 활성에 쓴다.</summary>
    public bool CanLaunch => !string.IsNullOrWhiteSpace(WorkingDirectory);

    /// <summary>폴더 선택 결과를 받아 히스토리에 넣는다.</summary>
    public void SetFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        WorkingDirectory = path;
        Remember(path);
    }

    /// <summary>세션 "이어서 열기"에서 넘어온 인자를 채운다.</summary>
    public void PrepareResume(string workingDir, string resumeArguments)
    {
        SetFolder(workingDir);
        Arguments = resumeArguments;
    }

    /// <summary>프리셋을 인자 입력란 뒤에 붙인다.</summary>
    [RelayCommand]
    public void AppendPreset(string? preset)
    {
        if (string.IsNullOrEmpty(preset))
        {
            return;
        }

        Arguments = Arguments.Length == 0 ? preset : $"{Arguments.TrimEnd()} {preset}";
    }

    /// <summary>Claude를 연다.</summary>
    [RelayCommand]
    public Task LaunchClaudeAsync() => LaunchAsync(ToolKind.Claude);

    /// <summary>Codex를 연다.</summary>
    [RelayCommand]
    public Task LaunchCodexAsync() => LaunchAsync(ToolKind.Codex);

    private async Task LaunchAsync(ToolKind kind)
    {
        if (WorkingDirectory is not { Length: > 0 } directory)
        {
            LastCommand = UiStrings.Get("Terminal_PickFolderFirst");
            return;
        }

        var provider = _providers.First(p => p.Kind == kind);

        Remember(directory);
        LastCommand = $"{directory} > {provider.ExecutableName} {Arguments}".TrimEnd();

        await _launcher.LaunchAsync(directory, provider.ExecutableName, Arguments).ConfigureAwait(true);
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

    partial void OnWorkingDirectoryChanged(string? value) => OnPropertyChanged(nameof(CanLaunch));
}
