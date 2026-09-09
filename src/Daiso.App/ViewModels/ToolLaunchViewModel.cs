using CommunityToolkit.Mvvm.ComponentModel;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;

namespace Daiso.App.ViewModels;

/// <summary>
/// 터미널 화면의 도구 한 줄. 설치돼 있으면 `{도구} 열기`, 아니면 `{도구} 설치`가 된다. (REQUIREMENTS §4, ARCHITECTURE §5.3)
/// 설치를 눌러 새 터미널에서 설치가 돌기 시작하면 `설치 중…`으로 잠기고, 실행 파일이 보이는 순간 `열기`로 돌아온다.
/// </summary>
public sealed partial class ToolLaunchViewModel : ObservableObject
{
    public ToolLaunchViewModel(IProvider provider, IReadOnlyList<string> presets)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(presets);

        Provider = provider;
        Presets = presets;
    }

    public IProvider Provider { get; }

    /// <summary>화면 뷰모델의 실행 명령. 버튼이 이 줄을 인자로 넘긴다.</summary>
    [ObservableProperty]
    private System.Windows.Input.ICommand? launchCommand;

    public ToolKind Kind => Provider.Kind;

    /// <summary>버튼·미리보기에 쓰는 짧은 이름.</summary>
    public string Label => ToolLook.Short(Kind);

    /// <summary>카드에 쓰는 정식 이름.</summary>
    public string Title => ToolLook.Title(Kind);

    /// <summary>카드에 쓰는 제작사 이름.</summary>
    public string Vendor => ToolLook.Vendor(Kind);

    /// <summary>카드에 붙는 설치 상태 한 줄. 고르기 전에 알아야 헛걸음을 안 한다.</summary>
    public string InstallStateText => UiStrings.Get(IsInstalled ? "Terminal_ToolReady" : "Terminal_ToolMissing");

    /// <summary>인자 프리셋.</summary>
    public IReadOnlyList<string> Presets { get; }

    /// <summary>프리셋 묶음 제목.</summary>
    public string PresetsTitle => UiStrings.Format("Terminal_PresetsFor", Label);

    /// <summary>UI 자동화 식별자.</summary>
    public string AutomationId => $"Launch{Kind}Button";

    /// <summary>실행 파일이 PATH에 있는가. 없으면 버튼이 설치로 바뀐다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    [NotifyPropertyChangedFor(nameof(EmbeddedButtonText))]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(CanPress))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(InstallStateText))]
    private bool isInstalled;

    /// <summary>설치를 눌러 새 터미널이 돌고 있는가. 확인될 때까지 버튼을 잠근다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    [NotifyPropertyChangedFor(nameof(EmbeddedButtonText))]
    [NotifyPropertyChangedFor(nameof(CanPress))]
    private bool isInstalling;

    /// <summary>작업 폴더가 정해졌는가. 열기는 폴더가 있어야 하고, 설치는 폴더가 없어도 된다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPress))]
    private bool hasFolder;

    /// <summary>이 도구에 붙일 인자. 탭마다 따로 기억한다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string arguments = string.Empty;

    /// <summary>버튼을 누르면 실제로 실행될 명령. 설치 전이면 설치 명령이 보인다. 셸 감싸기는 실행 시점에 붙는다.</summary>
    public string Preview
    {
        get
        {
            var command = IsInstalled
                ? (Arguments.Trim().Length > 0 ? $"{Provider.ExecutableName} {Arguments.Trim()}" : Provider.ExecutableName)
                : Provider.InstallCommand;

            return $"{Label}  ▸  {command}";
        }
    }

    /// <summary>프리셋을 인자 뒤에 붙인다.</summary>
    public void AppendPreset(string preset)
    {
        if (string.IsNullOrEmpty(preset))
        {
            return;
        }

        Arguments = Arguments.Length == 0 ? preset : $"{Arguments.TrimEnd()} {preset}";
    }

    public string ButtonText => IsInstalling
        ? UiStrings.Format("Terminal_Installing", Label)
        : UiStrings.Format(IsInstalled ? "Terminal_Open" : "Terminal_Install", Label);

    /// <summary>기본 버튼(앱 안 터미널) 글. 설치 전이면 설치, 설치 중이면 잠금 문구. 외부 열기 버튼의 툴팁은 <see cref="ButtonText"/>.</summary>
    public string EmbeddedButtonText => IsInstalling
        ? UiStrings.Format("Terminal_Installing", Label)
        : IsInstalled ? UiStrings.Get("Terminal_OpenTerminalRoom") : UiStrings.Format("Terminal_Install", Label);

    /// <summary>열기는 터미널 아이콘, 설치는 내려받기 아이콘.</summary>
    public string Glyph => IsInstalled ? "" : "";

    public bool CanPress => !IsInstalling && (!IsInstalled || HasFolder);

    /// <summary>미설치일 때 버튼 아래 한 줄.</summary>
    public string Hint => IsInstalled ? string.Empty : UiStrings.Format("Terminal_InstallHint", Provider.InstallCommand);

    public bool HasHint => !IsInstalled;

    /// <summary>설치 명령의 실행 파일과 인자. "npm install -g x" → ("npm", "install -g x").</summary>
    public (string Executable, string Arguments) InstallParts()
    {
        var command = Provider.InstallCommand.Trim();
        var space = command.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? (command, string.Empty) : (command[..space], command[(space + 1)..]);
    }
}
