using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daiso.Core;
using Daiso.App.Strings;

namespace Daiso.App.ViewModels;

/// <summary>
/// CLAUDE.md ↔ AGENTS.md 마이그레이션 화면 상태. (REQUIREMENTS §7, ARCHITECTURE §5.6)
/// 검사는 읽기만 하고, 실제 쓰기는 방향을 고른 뒤에만 한다.
/// </summary>
public sealed partial class MigrationViewModel : ObservableObject
{
    private readonly IInstructionMigrationService _service;

    [ObservableProperty]
    private string? projectDirectory;

    [ObservableProperty]
    private string notes = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>CLAUDE.md가 있어 AGENTS.md 쪽으로 옮길 수 있는가.</summary>
    [ObservableProperty]
    private bool canMigrateToCodex;

    /// <summary>AGENTS.md가 있어 CLAUDE.md 쪽으로 옮길 수 있는가.</summary>
    [ObservableProperty]
    private bool canMigrateToClaude;

    public MigrationViewModel(IInstructionMigrationService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
    }

    /// <summary>좌우 비교 결과. 왼쪽이 CLAUDE.md, 오른쪽이 AGENTS.md다.</summary>
    public ObservableCollection<MigrationDiffLineViewModel> Diff { get; } = [];

    /// <summary>폴더의 두 파일을 읽어 비교한다. 파일을 쓰지 않는다.</summary>
    public void Inspect(string projectDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        ProjectDirectory = projectDir;
        var plan = _service.Plan(projectDir);

        Diff.Clear();

        foreach (var line in plan.Diff)
        {
            Diff.Add(new MigrationDiffLineViewModel(line));
        }

        Notes = string.Join('\n', plan.Notes);
        CanMigrateToCodex = plan.Claude.Exists;
        CanMigrateToClaude = plan.Codex.Exists;
        StatusText = plan.CanMigrate
            ? UiStrings.Format("Migration_DiffCount", Diff.Count(l => l.Kind != DiffKind.Same))
            : UiStrings.Get("Migration_BothMissing");
    }

    /// <summary>바뀐 파일 이름. 값은 제공자가 정한다 — 화면이 파일 이름을 직접 적지 않는다.</summary>
    public string TargetFileName(ToolKind tool) => _service.RulesFileName(tool);

    /// <summary>고른 방향으로 대상 파일 하나를 쓴다.</summary>
    public MigrationResult Apply(MigrationDirection direction)
    {
        if (ProjectDirectory is not { Length: > 0 } directory)
        {
            throw new InvalidOperationException(UiStrings.Get("Migration_NeedProject"));
        }

        var result = _service.Apply(directory, direction, dryRun: false);
        var target = TargetFileName(result.Target);

        StatusText = result.Warnings.Count == 0
            ? UiStrings.Format("Migration_Updated", target)
            : UiStrings.Format("Migration_UpdatedWithWarnings", target, result.Warnings.Count);

        Inspect(directory);

        return result;
    }
}

/// <summary>diff 한 줄. 목록에 그대로 보여 줄 문자열까지 만든다.</summary>
public sealed class MigrationDiffLineViewModel
{
    public MigrationDiffLineViewModel(DiffLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Kind = line.Kind;
        var sign = line.Kind switch
        {
            DiffKind.Removed => '<',
            DiffKind.Added => '>',
            _ => ' ',
        };

        Display = $"{line.LeftLine?.ToString() ?? string.Empty,4} {line.RightLine?.ToString() ?? string.Empty,4} {sign} {line.Text}";
    }

    public DiffKind Kind { get; }

    /// <summary>`왼쪽줄 오른쪽줄 기호 본문` 형태.</summary>
    public string Display { get; }
}
