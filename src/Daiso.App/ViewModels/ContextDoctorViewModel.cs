using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.Core;

namespace Daiso.App.ViewModels;

/// <summary>폴더가 AI에게 넘기는 컨텍스트를 도구별로 보여준다. (REQUIREMENTS §5.3, ARCHITECTURE §5.4)</summary>
public sealed partial class ContextDoctorViewModel : ObservableObject
{
    private readonly IContextInspector _inspector;
    private readonly IProjectFactsReader _factsReader;

    [ObservableProperty]
    private string? projectPath;

    [ObservableProperty]
    private int toolIndex;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private int totalChars;

    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private ContextFileViewModel? selectedFile;

    public ContextDoctorViewModel(IContextInspector inspector, IProjectFactsReader factsReader)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(factsReader);

        _inspector = inspector;
        _factsReader = factsReader;
    }

    /// <summary>도구 토글 항목.</summary>
    public IReadOnlyList<string> Tools { get; } = ["Claude", "Codex"];

    /// <summary>로드 순서대로 정렬된 컨텍스트 파일.</summary>
    public ObservableCollection<ContextFileViewModel> Files { get; } = [];

    /// <summary>두 개 이상 파일에 같은 내용으로 나오는 줄.</summary>
    public ObservableCollection<string> Duplicates { get; } = [];

    /// <summary>상반되는 지시로 보이는 짝.</summary>
    public ObservableCollection<string> Conflicts { get; } = [];

    /// <summary>REQ 5.3 부가 정보 줄.</summary>
    public ObservableCollection<string> Facts { get; } = [];

    /// <summary>선택한 파일 내용.</summary>
    public string SelectedFileContent => SelectedFile?.File.Content ?? string.Empty;

    /// <summary>총 글자 수 표시 문구.</summary>
    public string TotalCharsText => $"총 {TotalChars:N0}자";

    /// <summary>프로젝트를 바꾸고 다시 검사한다.</summary>
    public Task SetProjectAsync(string? projectDir)
    {
        ProjectPath = projectDir;
        return InspectCommand.ExecuteAsync(null);
    }

    /// <summary>현재 폴더와 도구로 리포트를 만든다.</summary>
    [RelayCommand]
    public async Task InspectAsync(CancellationToken ct)
    {
        Files.Clear();
        Duplicates.Clear();
        Conflicts.Clear();
        Facts.Clear();
        SelectedFile = null;
        TotalChars = 0;

        if (ProjectPath is not { Length: > 0 } directory)
        {
            StatusText = "프로젝트를 먼저 고르세요";
            NotifyDerived();
            return;
        }

        IsBusy = true;

        try
        {
            var tool = ToolIndex == 1 ? ToolKind.Codex : ToolKind.Claude;
            var report = await _inspector.InspectAsync(tool, directory, ct).ConfigureAwait(true);

            foreach (var file in report.Files)
            {
                Files.Add(new ContextFileViewModel(file));
            }

            TotalChars = report.TotalChars;

            foreach (var duplicate in report.Duplicates)
            {
                var names = duplicate.Files.Select(Path.GetFileName);
                Duplicates.Add($"\"{duplicate.NormalizedText}\"  ({string.Join(", ", names)})");
            }

            foreach (var conflict in report.Conflicts)
            {
                Conflicts.Add(
                    $"{conflict.Reason}\n"
                    + $"  A {Path.GetFileName(conflict.FileA)}: {conflict.LineA}\n"
                    + $"  B {Path.GetFileName(conflict.FileB)}: {conflict.LineB}");
            }

            await LoadFactsAsync(directory, ct).ConfigureAwait(true);

            StatusText =
                $"{tool} · 파일 {Files.Count(file => file.File.Exists)}/{Files.Count} · "
                + $"중복 {Duplicates.Count} · 충돌 후보 {Conflicts.Count}";

            SelectedFile = Files.FirstOrDefault(file => file.File.Exists);
        }
        finally
        {
            IsBusy = false;
            NotifyDerived();
        }
    }

    private async Task LoadFactsAsync(string directory, CancellationToken ct)
    {
        var facts = await _factsReader.ReadAsync(directory, ct).ConfigureAwait(true);

        foreach (var line in facts.Settings)
        {
            Facts.Add(line);
        }

        Facts.Add($"skills {facts.SkillCount} · agents {facts.AgentCount} · commands {facts.CommandCount}");
        Facts.Add($".mcp.json: {(facts.HasMcpJson ? "있음" : "없음")}");

        Facts.Add(facts.GitBranch is { } branch
            ? $"git: {branch}{(facts.GitCommit is { } commit ? $" @ {commit}" : string.Empty)}"
            : "git: 없음");
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(TotalCharsText));
        OnPropertyChanged(nameof(SelectedFileContent));
    }

    partial void OnSelectedFileChanged(ContextFileViewModel? value) =>
        OnPropertyChanged(nameof(SelectedFileContent));

    partial void OnToolIndexChanged(int value) => _ = InspectCommand.ExecuteAsync(null);
}

/// <summary>컨텍스트 파일 한 줄.</summary>
public sealed class ContextFileViewModel
{
    public ContextFileViewModel(ContextFile file) => File = file;

    public ContextFile File { get; }

    public string OrderText => $"{File.Order + 1,2}.";

    public string Mark => File.Exists ? "O" : "-";

    public string Name => System.IO.Path.GetFileName(File.Path);

    public string CharsText => File.Exists ? $"{File.Content.Length:N0}자" : "-";

    public string Summary => $"{OrderText} {Mark} {CharsText}  [{File.Kind}]  {File.Path}";
}
