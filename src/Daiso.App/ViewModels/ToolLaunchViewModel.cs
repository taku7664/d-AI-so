using CommunityToolkit.Mvvm.ComponentModel;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;

namespace Daiso.App.ViewModels;

/// <summary>모델 칸 한 줄. <see cref="Option"/> 이 null 이면 "도구 설정대로" — <c>--model</c> 을 붙이지 않는다.</summary>
public sealed class ModelChoiceViewModel
{
    public ModelChoiceViewModel(ModelOption? option) => Option = option;

    public ModelOption? Option { get; }

    /// <summary><c>--model</c> 뒤에 붙는 값. "도구 설정대로"는 null.</summary>
    public string? Id => Option?.Id;

    /// <summary>
    /// 이름과 실제로 붙는 값을 함께 보인다. 같은 "Fable" 이라도 별칭(<c>fable</c>)과 계정 전용판(<c>claude-fable-5-1[1m]</c>)이 다르다.
    /// </summary>
    public string Name => Option is null
        ? UiStrings.Get("Terminal_ModelDefault")
        : string.Equals(Option.Name, Option.Id, StringComparison.OrdinalIgnoreCase) ? Option.Id : $"{Option.Name}  ·  {Option.Id}";

    /// <summary>목록에 그리는 글이자 검색칸이 거르는 글 (SearchablePicker 규칙).</summary>
    public override string ToString() => Name;
}

/// <summary>
/// 터미널 화면의 도구 한 줄. 설치돼 있으면 `{도구} 열기`, 아니면 `{도구} 설치`가 된다. (REQUIREMENTS §4, ARCHITECTURE §5.3)
/// 설치를 눌러 새 터미널에서 설치가 돌기 시작하면 `설치 중…`으로 잠기고, 실행 파일이 보이는 순간 `열기`로 돌아온다.
/// </summary>
public sealed partial class ToolLaunchViewModel : ObservableObject
{
    public ToolLaunchViewModel(IProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Provider = provider;
        SelectedModel = ModelChoices[0];
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

    /// <summary>UI 자동화 식별자.</summary>
    public string AutomationId => $"Launch{Kind}Button";

    // ── 모델 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 모델 칸의 목록. 첫 줄은 늘 "도구 설정대로"(<c>--model</c> 을 붙이지 않음)이고, 뒤는 도구가 알려 준 모델이다.
    /// <para>
    /// "도구 설정대로" 줄은 도구마다 따로 만든다. 탭을 바꾸는 사이 콤보가 옛 탭의 줄을 알려 와도
    /// <see cref="PickModel"/> 이 자기 목록에 없는 줄로 알아보고 무시하게 하려는 것이다.
    /// </para>
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<ModelChoiceViewModel> ModelChoices { get; } = [new ModelChoiceViewModel(null)];

    /// <summary>인자 칸에 적힌 모델에 맞는 줄. 목록에 없는 모델을 직접 적었으면 null(칸이 비어 보인다 — 틀린 줄을 고른 것처럼 보이지 않게).</summary>
    [ObservableProperty]
    private ModelChoiceViewModel? selectedModel;

    /// <summary>모델 목록을 읽고 있는가. <c>agy models</c> 는 서버에 물어 몇 초 걸린다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelStatusText))]
    [NotifyPropertyChangedFor(nameof(HasModelStatus))]
    private bool isLoadingModels;

    private bool _modelsLoaded;

    /// <summary>모델 칸 밑 한 줄. 읽는 중이거나, 읽었는데 도구가 아무것도 안 줬을 때만.</summary>
    public string ModelStatusText =>
        IsLoadingModels ? UiStrings.Get("Terminal_ModelLoading")
        : _modelsLoaded && ModelChoices.Count <= 1 ? UiStrings.Get("Terminal_ModelNone")
        : string.Empty;

    public bool HasModelStatus => ModelStatusText.Length > 0;

    /// <summary>도구에게 모델 목록을 한 번 묻는다. 실패하면 다음 화면 열기에 다시 묻는다.</summary>
    public async Task LoadModelsAsync(CancellationToken ct = default)
    {
        if (_modelsLoaded || IsLoadingModels)
        {
            return;
        }

        IsLoadingModels = true;

        try
        {
            foreach (var model in await Provider.ListModelsAsync(ct).ConfigureAwait(true))
            {
                ModelChoices.Add(new ModelChoiceViewModel(model));
            }

            _modelsLoaded = true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            // 목록이 없어도 인자 칸에 직접 적을 수 있다
        }
        finally
        {
            IsLoadingModels = false;
            SyncModelFromArguments();
        }
    }

    /// <summary>
    /// 사람이 모델 칸에서 고른 줄을 인자 칸에 반영한다. 칸이 곧 실행될 인자다 (<see cref="ModelArgument"/>).
    /// 이미 그 모델이 적혀 있으면 손대지 않는다 — 콤보가 선택을 되돌려 알릴 때 사람이 적은 순서를 흩뜨리지 않게.
    /// </summary>
    public void PickModel(ModelChoiceViewModel? choice)
    {
        if (choice is null || !ModelChoices.Contains(choice)
            || string.Equals(ModelArgument.Read(Arguments), choice.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Arguments = ModelArgument.Apply(Arguments, choice.Id);
    }

    partial void OnArgumentsChanged(string value) => SyncModelFromArguments();

    private void SyncModelFromArguments()
    {
        var id = ModelArgument.Read(Arguments);

        SelectedModel = ModelChoices.FirstOrDefault(choice => string.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase));
    }

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
                ? (Arguments.Trim().Length > 0 ? $"{Provider.LaunchTarget} {Arguments.Trim()}" : Provider.LaunchTarget)
                : InstallOpensPage ? Provider.InstallUri! : Provider.InstallCommand;

            return $"{Label}  ▸  {command}";
        }
    }

    public string ButtonText => IsInstalling
        ? UiStrings.Format("Terminal_Installing", Label)
        : UiStrings.Format(IsInstalled ? "Terminal_Open" : InstallOpensPage ? "Terminal_InstallPage" : "Terminal_Install", Label);

    /// <summary>기본 버튼(앱 안 터미널) 글. 설치 전이면 설치, 설치 중이면 잠금 문구. 외부 열기 버튼의 툴팁은 <see cref="ButtonText"/>.</summary>
    public string EmbeddedButtonText => IsInstalling
        ? UiStrings.Format("Terminal_Installing", Label)
        : IsInstalled
            ? UiStrings.Get("Terminal_OpenTerminalRoom")
            : UiStrings.Format(InstallOpensPage ? "Terminal_InstallPage" : "Terminal_Install", Label);

    /// <summary>열기는 터미널 아이콘, 설치는 내려받기 아이콘.</summary>
    public string Glyph => IsInstalled ? "" : "";

    public bool CanPress => !IsInstalling && (!IsInstalled || HasFolder);

    /// <summary>설치가 셸 명령이 아니라 안내 페이지인가. 버튼이 하는 일이 달라진다.</summary>
    public bool InstallOpensPage => Provider.InstallUri is { Length: > 0 };

    /// <summary>
    /// 미설치일 때 버튼 아래 한 줄. 안내 페이지를 여는 도구는 <b>앱이 스크립트를 돌리지 않는다는 것</b>까지 말한다 —
    /// 버튼을 눌렀는데 터미널이 안 뜨면 고장으로 읽히고, 백신에 걸린 명령을 그대로 보여 주기만 하면 막힌 길을 안내하는 셈이다.
    /// </summary>
    public string Hint => IsInstalled
        ? string.Empty
        : InstallOpensPage
            ? UiStrings.Format("Terminal_InstallPageHint", Provider.InstallCommand)
            : UiStrings.Format("Terminal_InstallHint", Provider.InstallCommand);

    public bool HasHint => !IsInstalled;

    /// <summary>
    /// 설치 명령의 실행 파일과 인자. "npm install -g x" → ("npm", "install -g x").
    /// <see cref="InstallOpensPage"/> 인 도구에는 쓰지 않는다.
    /// </summary>
    public (string Executable, string Arguments) InstallParts()
    {
        var command = Provider.InstallCommand.Trim();
        var space = command.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? (command, string.Empty) : (command[..space], command[(space + 1)..]);
    }
}
