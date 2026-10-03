using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daiso.Core;
using Daiso.App.Services;
using Daiso.App.Strings;

namespace Daiso.App.ViewModels;

/// <summary>
/// 도구 사이 지시문 마이그레이션 화면 상태. (REQUIREMENTS §7, ARCHITECTURE §5.6)
/// 검사는 읽기만 하고, 실제 쓰기는 방향을 고른 뒤에만 한다.
///
/// <para>
/// 방향은 더 이상 둘이 아니다. 도구가 셋이면 최대 여섯 방향이고, 그 목록은 <see cref="Directions"/> 가 만든다
/// (docs/REVIEW_BACKLOG.md A5).
/// </para>
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

    /// <summary>고를 수 있는 방향이 하나라도 있는가.</summary>
    [ObservableProperty]
    private bool canMigrate;

    public MigrationViewModel(IInstructionMigrationService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
    }

    /// <summary>좌우 비교 결과.</summary>
    public ObservableCollection<MigrationDiffLineViewModel> Diff { get; } = [];

    /// <summary>지금 고를 수 있는 방향들. 파일이 있는 도구에서 나머지 도구로.</summary>
    public ObservableCollection<MigrationDirectionViewModel> Directions { get; } = [];

    /// <summary>diff 왼쪽 도구의 파일 이름.</summary>
    [ObservableProperty]
    private string leftFileName = string.Empty;

    /// <summary>diff 오른쪽 도구의 파일 이름.</summary>
    [ObservableProperty]
    private string rightFileName = string.Empty;

    /// <summary>폴더의 지시문 파일들을 읽어 비교한다. 파일을 쓰지 않는다.</summary>
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

        Directions.Clear();

        foreach (var direction in plan.Directions)
        {
            Directions.Add(new MigrationDirectionViewModel(
                direction,
                plan.FileNameOf(direction.From),
                plan.FileNameOf(direction.To)));
        }

        LeftFileName = plan.FileNameOf(plan.Left);
        RightFileName = plan.FileNameOf(plan.Right);
        CanMigrate = Directions.Count > 0;

        // Core 는 문구 키만 준다. 사람이 읽는 말로 바꾸는 것은 여기 몫이다 (ARCHITECTURE §6.1)
        Notes = string.Join('\n', plan.Notes.Select(Render));

        StatusText = plan.CanMigrate
            ? UiStrings.Format("Migration_DiffCount", Diff.Count(line => line.Kind != DiffKind.Same))
            : UiStrings.Get("Migration_BothMissing");
    }

    /// <summary>안내·경고 한 줄을 사람이 읽는 말로.</summary>
    internal static string Render(MigrationNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return (note.Argument, note.Argument2) switch
        {
            (null, _) => UiStrings.Get(note.Key),
            ({ } one, null) => UiStrings.Format(note.Key, one),
            ({ } one, { } two) => UiStrings.Format(note.Key, one, two),
        };
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

/// <summary>고를 수 있는 방향 하나. 목록에 "CLAUDE.md → AGENTS.md" 처럼 보인다.</summary>
public sealed class MigrationDirectionViewModel
{
    public MigrationDirectionViewModel(MigrationDirection direction, string fromFileName, string toFileName)
    {
        Direction = direction;
        FromFileName = fromFileName;
        ToFileName = toFileName;
    }

    public MigrationDirection Direction { get; }

    public string FromFileName { get; }

    public string ToFileName { get; }

    /// <summary>목록 한 줄. 화살표 하나로 방향을 말한다.</summary>
    public string Label => UiStrings.Format("Migration_DirectionLabel", FromFileName, ToFileName);

    /// <inheritdoc />
    public override string ToString() => Label;
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
