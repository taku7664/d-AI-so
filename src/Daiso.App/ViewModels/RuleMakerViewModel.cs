using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.Core;
using Daiso.App.Strings;

namespace Daiso.App.ViewModels;

/// <summary>.daiso 편집기. (REQUIREMENTS §6, GOAL Step 12)</summary>
public sealed partial class RuleMakerViewModel : ObservableObject
{
    private readonly IRuleFileService _ruleFiles;
    private readonly IRulePresetSerializer _serializer;
    private readonly IMarkdownRuleRenderer _renderer;
    private readonly IReadOnlyList<IProvider> _providers;
    private readonly ISettingsStore _settings;
    private readonly KnownProjects _knownProjects;

    [ObservableProperty]
    private string presetName = UiStrings.Get("RuleMaker_NewPresetName");

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private string markdownPreview = string.Empty;

    [ObservableProperty]
    private string? currentPath;

    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private RuleEditViewModel? selectedRule;

    /// <summary>연동할 프로젝트 폴더. 폴더 선택 대화상자 대신 경로를 직접 붙여넣을 때 쓴다.</summary>
    [ObservableProperty]
    private string? projectDirectory;

    public RuleMakerViewModel(
        IRuleFileService ruleFiles,
        IRulePresetSerializer serializer,
        IMarkdownRuleRenderer renderer,
        IEnumerable<IProvider> providers,
        ISettingsStore settings,
        KnownProjects knownProjects)
    {
        ArgumentNullException.ThrowIfNull(knownProjects);

        ArgumentNullException.ThrowIfNull(ruleFiles);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(settings);

        _ruleFiles = ruleFiles;
        _serializer = serializer;
        _renderer = renderer;
        _providers = providers.ToList();
        _settings = settings;

        _knownProjects = knownProjects;

        RecentFiles = new ObservableCollection<string>(_settings.Current.RecentRuleFiles);
        Refresh();
    }

    /// <summary>조건 없이 항상 적용되는 행동.</summary>
    public ObservableCollection<ActionEditViewModel> Global { get; } = [];

    /// <summary>조건이 붙은 규칙.</summary>
    public ObservableCollection<RuleEditViewModel> Rules { get; } = [];

    /// <summary>최근에 연 .daiso 파일.</summary>
    public ObservableCollection<string> RecentFiles { get; }

    /// <summary>프리셋 라이브러리 폴더.</summary>
    public string PresetLibraryDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "d-AI-so",
        "presets");

    /// <summary>우선순위 콤보 항목.</summary>
    public IReadOnlyList<string> Priorities { get; } = ["MUST", "SHOULD", "MAY"];

    /// <summary>라이브러리에 저장된 프리셋 파일.</summary>
    public ObservableCollection<string> LibraryPresets { get; } = [];

    /// <summary>프리셋 갤러리. 기본 제공 프리셋 뒤에 내 라이브러리 파일이 온다. (ARCHITECTURE §5.2)</summary>
    public ObservableCollection<PresetGalleryItemViewModel> Gallery { get; } = [];

    /// <summary>갤러리에서 고른 항목. 오른쪽 미리보기와 버튼 활성이 따라간다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGallerySelection))]
    [NotifyPropertyChangedFor(nameof(HasNoGallerySelection))]
    [NotifyPropertyChangedFor(nameof(GalleryPreview))]
    [NotifyPropertyChangedFor(nameof(GalleryPreviewTitle))]
    private PresetGalleryItemViewModel? selectedGalleryItem;

    /// <summary>고른 프리셋의 렌더된 Markdown. 목록을 고르면 미리보기 카드의 두 번째 탭에 보인다.</summary>
    public string GalleryPreview => SelectedGalleryItem?.Preview ?? string.Empty;

    /// <summary>고른 프리셋의 이름과 규칙 수.</summary>
    public string GalleryPreviewTitle => SelectedGalleryItem is { } item
        ? UiStrings.Format("RuleMaker_GalleryPreviewTitle", item.Name, item.Preset.Global.Count, item.Preset.Rules.Count)
        : string.Empty;

    /// <summary>갤러리에서 무언가 골랐는가.</summary>
    public bool HasGallerySelection => SelectedGalleryItem is not null;

    /// <summary>아직 고르지 않았는가. 안내 문구를 띄운다.</summary>
    public bool HasNoGallerySelection => SelectedGalleryItem is null;

    /// <summary>갤러리를 다시 채운다. 기본 제공은 항상 같고, 내 라이브러리는 폴더를 다시 읽는다.</summary>
    [RelayCommand]
    public void RefreshGallery()
    {
        var keep = SelectedGalleryItem?.Key;

        Gallery.Clear();

        foreach (var item in BuiltInPresets.List(_serializer))
        {
            Gallery.Add(PresetGalleryItemViewModel.FromBuiltIn(item, _serializer, _renderer));
        }

        RefreshLibrary();

        foreach (var path in LibraryPresets)
        {
            try
            {
                Gallery.Add(PresetGalleryItemViewModel.FromFile(path, _ruleFiles.Load(path), _renderer));
            }
            catch (Exception ex) when (ex is RuleParseException or IOException or UnauthorizedAccessException)
            {
                // 깨진 라이브러리 파일은 갤러리에서 빼고 넘어간다. 열기 메뉴로는 여전히 열 수 있고 거기서 오류를 본다.
            }
        }

        SelectedGalleryItem = Gallery.FirstOrDefault(item => item.Key == keep) ?? Gallery.FirstOrDefault();
    }

    /// <summary>갤러리 항목을 편집기에 연다. 기본 제공은 내 파일이 아니므로 경로 없이 열리고, 저장할 때 파일이 된다.</summary>
    public void OpenFromGallery(PresetGalleryItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.FilePath is { } path)
        {
            Open(path);
            return;
        }

        Load(item.Preset);
        CurrentPath = null;
        StatusText = UiStrings.Format("RuleMaker_GalleryOpened", item.Name);
    }

    /// <summary>
    /// 갤러리 항목의 규칙을 지금 편집 중인 규칙 뒤에 붙인다. 같은 문장의 전역 행동은 한 번만 남긴다.
    /// 새 문서의 빈 자리표시자 줄은 치운다. 이름이 아직 기본값이면 프리셋 이름을 가져온다.
    /// </summary>
    /// <returns>추가된 전역 행동과 규칙의 수.</returns>
    public int MergeFromGallery(PresetGalleryItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var preset = item.Preset;
        var added = 0;

        foreach (var blank in Global.Where(action => !HasText(action)).ToList())
        {
            Global.Remove(blank);
        }

        foreach (var action in preset.Global)
        {
            if (Global.Any(existing => string.Equals(existing.Text.Trim(), action.Action, StringComparison.Ordinal)))
            {
                continue;
            }

            Global.Add(Track(ActionEditViewModel.From(action)));
            added++;
        }

        foreach (var blank in Rules.Where(rule => rule.ConditionPreview.Length == 0 && !rule.Actions.Any(HasText)).ToList())
        {
            blank.Changed -= OnChildChanged;
            Rules.Remove(blank);
        }

        foreach (var rule in preset.Rules)
        {
            var editable = RuleEditViewModel.From(rule, _renderer);

            // 같은 조건에 같은 행동이면 이미 있는 규칙이다. 같은 프리셋을 두 번 합쳐도 두 배가 되지 않는다.
            if (Rules.Any(existing => SameRule(existing, editable)))
            {
                continue;
            }

            editable.Changed += OnChildChanged;

            foreach (var action in editable.Actions)
            {
                Track(action);
            }

            Rules.Add(editable);
            added++;
        }

        if (string.IsNullOrWhiteSpace(PresetName) || PresetName == UiStrings.Get("RuleMaker_NewPresetName"))
        {
            PresetName = preset.Name;
        }

        SelectedRule ??= Rules.FirstOrDefault();
        Refresh();
        StatusText = UiStrings.Format("RuleMaker_GalleryMerged", item.Name, added);

        return added;
    }

    // ── 파일 ─────────────────────────────────────────────────────────────

    /// <summary>빈 프리셋으로 시작한다.</summary>
    [RelayCommand]
    public void New()
    {
        PresetName = UiStrings.Get("RuleMaker_NewPresetName");
        Description = null;
        CurrentPath = null;
        Global.Clear();
        Rules.Clear();
        AddGlobalAction();
        AddRule();
        StatusText = UiStrings.Get("RuleMaker_Created");
        Refresh();
    }

    /// <summary>파일을 읽어 편집기에 채운다.</summary>
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Load(_ruleFiles.Load(path));
        CurrentPath = path;
        RememberRecent(path);
        StatusText = UiStrings.Format("RuleMaker_Opened", path);
    }

    /// <summary>현재 편집 내용을 파일로 쓴다.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // 저장 직전에 스키마 검증을 통과해야 한다. 실패하면 RuleParseException이 line/column을 들고 올라간다.
        var preset = BuildValidatedPreset();

        _ruleFiles.Save(preset, path);
        CurrentPath = path;
        RememberRecent(path);
        StatusText = UiStrings.Format("RuleMaker_Saved", path);
    }

    /// <summary>프리셋 라이브러리 목록을 다시 읽는다.</summary>
    [RelayCommand]
    public void RefreshLibrary()
    {
        LibraryPresets.Clear();

        if (!Directory.Exists(PresetLibraryDirectory))
        {
            return;
        }

        foreach (var file in Directory
            .EnumerateFiles(PresetLibraryDirectory, "*.daiso", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            LibraryPresets.Add(file);
        }
    }

    /// <summary>라이브러리에 현재 프리셋을 저장한다.</summary>
    [RelayCommand]
    public void SaveToLibrary()
    {
        Directory.CreateDirectory(PresetLibraryDirectory);

        var safeName = string.Join("_", PresetName.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(PresetLibraryDirectory, $"{safeName}.daiso");

        Save(path);
        RefreshLibrary();
    }

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

    /// <summary>미리보기에 보여줄 내용이 아직 없는가. 빈 껍데기 대신 안내를 띄운다.</summary>
    public bool PreviewIsEmpty =>
        Global.All(action => string.IsNullOrWhiteSpace(action.Text))
        && Rules.All(rule => rule.Actions.All(action => string.IsNullOrWhiteSpace(action.Text)));

    /// <summary>미리보기에 보여줄 내용이 있는가.</summary>
    public bool PreviewHasContent => !PreviewIsEmpty;

    /// <summary>입력란의 경로로 연동한다.</summary>
    [RelayCommand]
    public void LinkToTypedProject()
    {
        if (ProjectDirectory is not { Length: > 0 } directory)
        {
            StatusText = UiStrings.Get("RuleMaker_NeedProjectPath");
            return;
        }

        LinkToProject(directory);
    }

    /// <summary>프로젝트 폴더에 .daiso와 지시문 블록을 넣는다.</summary>
    public void LinkToProject(string projectDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var path = Path.Combine(projectDir, InstructionTemplate.DefaultRulesFileName);
        Save(path);
        _ruleFiles.EnsureInstruction(projectDir, _providers);

        ProjectDirectory = projectDir;
        StatusText = UiStrings.Format("RuleMaker_Linked", projectDir);
    }

    // ── Global 행동 ──────────────────────────────────────────────────────

    [RelayCommand]
    public void AddGlobalAction()
    {
        Global.Add(Track(new ActionEditViewModel()));
        Refresh();
    }

    [RelayCommand]
    public void RemoveGlobalAction(ActionEditViewModel? action)
    {
        if (action is not null && Global.Remove(action))
        {
            Refresh();
        }
    }

    [RelayCommand]
    public void MoveGlobalActionUp(ActionEditViewModel? action) => Move(Global, action, -1);

    [RelayCommand]
    public void MoveGlobalActionDown(ActionEditViewModel? action) => Move(Global, action, +1);

    // ── 규칙 ─────────────────────────────────────────────────────────────

    [RelayCommand]
    public void AddRule()
    {
        var rule = new RuleEditViewModel(_renderer);
        rule.Changed += OnChildChanged;
        rule.Actions.Add(Track(new ActionEditViewModel()));

        Rules.Add(rule);
        SelectedRule = rule;
        Refresh();
    }

    [RelayCommand]
    public void RemoveRule(RuleEditViewModel? rule)
    {
        if (rule is null || !Rules.Remove(rule))
        {
            return;
        }

        rule.Changed -= OnChildChanged;
        SelectedRule = Rules.FirstOrDefault();
        Refresh();
    }

    [RelayCommand]
    public void MoveRuleUp(RuleEditViewModel? rule) => Move(Rules, rule, -1);

    [RelayCommand]
    public void MoveRuleDown(RuleEditViewModel? rule) => Move(Rules, rule, +1);

    /// <summary>미리보기를 다시 만든다. 검증에 걸리면 사유를 보여준다.</summary>
    public void Refresh()
    {
        try
        {
            MarkdownPreview = _renderer.Render(BuildPreset());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            MarkdownPreview = UiStrings.Format("RuleMaker_PreviewFailed", ex.Message);
        }

        OnPropertyChanged(nameof(PreviewIsEmpty));
        OnPropertyChanged(nameof(PreviewHasContent));
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// 편집 내용을 Core 모델로 만든다. 스키마 검증은 하지 않는다.
    /// 사람이 남겨 둔 **빈 줄은 버린다**. 빈 줄 하나 때문에 저장이 막히면 이유를 알기 어렵다.
    /// </summary>
    public RulePreset BuildPreset() => new(
        RuleValidator.SupportedSchemaVersion,
        PresetName,
        string.IsNullOrWhiteSpace(Description) ? null : Description,
        [.. Global.Where(HasText).Select(action => action.ToAction())],
        [.. Rules.Where(CanSaveRule).Select(rule => rule.ToRule())]);

    /// <summary>저장할 수 있는 상태인가. 이름과 행동이 하나는 있어야 한다.</summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(PresetName)
        && (Global.Any(HasText) || Rules.Any(CanSaveRule));

    private static bool HasText(ActionEditViewModel action) => !string.IsNullOrWhiteSpace(action.Text);

    /// <summary>조건식과 행동 문장이 모두 같은가. 합칠 때 중복을 거르는 기준.</summary>
    private static bool SameRule(RuleEditViewModel a, RuleEditViewModel b) =>
        string.Equals(a.ConditionPreview, b.ConditionPreview, StringComparison.Ordinal)
        && a.Actions.Where(HasText).Select(action => action.Text.Trim())
            .SequenceEqual(b.Actions.Where(HasText).Select(action => action.Text.Trim()), StringComparer.Ordinal);

    /// <summary>조건과 행동이 모두 채워진 규칙만 저장한다.</summary>
    private static bool CanSaveRule(RuleEditViewModel rule) =>
        rule.Actions.Any(HasText) && rule.ConditionPreview.Length > 0;

    /// <summary>직렬화 → 파싱을 거쳐 스키마 검증까지 통과한 모델.</summary>
    /// <exception cref="RuleParseException">검증 실패. line/column을 담고 있다.</exception>
    public RulePreset BuildValidatedPreset() => _serializer.Parse(_serializer.Serialize(BuildPreset()));

    private void Load(RulePreset preset)
    {
        PresetName = preset.Name;
        Description = preset.Description;

        Global.Clear();
        foreach (var action in preset.Global)
        {
            Global.Add(Track(ActionEditViewModel.From(action)));
        }

        foreach (var rule in Rules)
        {
            rule.Changed -= OnChildChanged;
        }

        Rules.Clear();
        foreach (var rule in preset.Rules)
        {
            var editable = RuleEditViewModel.From(rule, _renderer);
            editable.Changed += OnChildChanged;

            foreach (var action in editable.Actions)
            {
                Track(action);
            }

            Rules.Add(editable);
        }

        SelectedRule = Rules.FirstOrDefault();
        Refresh();
    }

    private ActionEditViewModel Track(ActionEditViewModel action)
    {
        action.PropertyChanged += OnChildChanged;
        return action;
    }

    private void OnChildChanged(object? sender, EventArgs e) => Refresh();

    private void Move<T>(ObservableCollection<T> list, T? item, int delta)
    {
        if (item is null)
        {
            return;
        }

        var index = list.IndexOf(item);
        var target = index + delta;

        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }

        list.Move(index, target);
        Refresh();
    }

    private void RememberRecent(string path)
    {
        AppSettings.Remember(_settings.Current.RecentRuleFiles, path);
        _settings.Save();

        RecentFiles.Clear();

        foreach (var file in _settings.Current.RecentRuleFiles)
        {
            RecentFiles.Add(file);
        }
    }

    partial void OnPresetNameChanged(string value) => Refresh();

    partial void OnDescriptionChanged(string? value) => Refresh();
}

/// <summary>행동 한 줄 편집.</summary>
public sealed partial class ActionEditViewModel : ObservableObject
{
    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private int priorityIndex = 1;

    /// <summary>Core 모델에서 만든다.</summary>
    public static ActionEditViewModel From(RuleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new ActionEditViewModel
        {
            Text = action.Action,
            PriorityIndex = action.Priority switch
            {
                Priority.Must => 0,
                Priority.May => 2,
                _ => 1,
            },
        };
    }

    /// <summary>Core 모델로 되돌린다.</summary>
    public RuleAction ToAction() => new(
        Text,
        PriorityIndex switch
        {
            0 => Priority.Must,
            2 => Priority.May,
            _ => Priority.Should,
        });
}

/// <summary>규칙 하나. 조건 트리와 행동 목록.</summary>
public sealed partial class RuleEditViewModel : ObservableObject
{
    private readonly IMarkdownRuleRenderer _renderer;

    public RuleEditViewModel(IMarkdownRuleRenderer renderer, ConditionNodeViewModel? root = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        _renderer = renderer;
        Root = root ?? ConditionNodeViewModel.Leaf(string.Empty);
        Root.Changed += OnRootChanged;
        Roots.Add(Root);
    }

    /// <summary>조건 트리나 행동이 바뀌었다.</summary>
    public event EventHandler? Changed;

    /// <summary>조건 트리 루트.</summary>
    public ConditionNodeViewModel Root { get; private set; }

    /// <summary>TreeView가 요구하는 루트 컬렉션. 항상 <see cref="Root"/> 하나만 담는다.</summary>
    public ObservableCollection<ConditionNodeViewModel> Roots { get; } = [];

    /// <summary>이 규칙의 행동.</summary>
    public ObservableCollection<ActionEditViewModel> Actions { get; } = [];

    /// <summary>트리 아래 한 줄 미리보기. `A &amp; (B | C)` 형태.</summary>
    public string ConditionPreview =>
        _renderer is MarkdownRuleRenderer markdown && Root.ToCondition() is { } condition
            ? markdown.Describe(condition)
            : string.Empty;

    /// <summary>조건식 상자에 보여줄 줄. 조건이 없으면 무엇을 하면 되는지 알려준다.</summary>
    public string ConditionPreviewLine =>
        ConditionPreview.Length > 0
            ? UiStrings.Format("RuleMaker_ConditionLine", ConditionPreview)
            : UiStrings.Get("RuleMaker_ConditionLineEmpty");

    /// <summary>목록에 보여줄 요약.</summary>
    public string Summary => UiStrings.Format(
        "RuleMaker_RuleSummary",
        ConditionPreview.Length > 0 ? ConditionPreview : UiStrings.Get("RuleMaker_EmptyCondition"),
        Actions.Count);

    /// <summary>Core 모델에서 만든다.</summary>
    public static RuleEditViewModel From(Rule rule, IMarkdownRuleRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var editable = new RuleEditViewModel(renderer, ConditionNodeViewModel.FromCondition(rule.When));
        editable.Actions.Clear();

        foreach (var action in rule.Then)
        {
            editable.Actions.Add(ActionEditViewModel.From(action));
        }

        return editable;
    }

    /// <summary>루트를 갈아끼운다. 그룹으로 감싸기에서 쓴다.</summary>
    public void ReplaceRoot(ConditionNodeViewModel root)
    {
        ArgumentNullException.ThrowIfNull(root);

        Root.Changed -= OnRootChanged;
        Root = root;
        Root.Changed += OnRootChanged;
        Roots.Clear();
        Roots.Add(root);
        NotifyChanged();
    }

    /// <summary>조건·행동이 바뀌었음을 알린다.</summary>
    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(ConditionPreview));
        OnPropertyChanged(nameof(ConditionPreviewLine));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Core 모델로 되돌린다. 조건이 비어 있으면 저장할 수 없는 규칙이라
    /// <see cref="InvalidOperationException"/>을 던진다. (저장 쪽에서 미리 걸러낸다)
    /// </summary>
    public Rule ToRule()
    {
        var condition = Root.ToCondition()
            ?? throw new InvalidOperationException("조건이 비어 있는 규칙은 저장할 수 없다");

        return new Rule(
            condition,
            [.. Actions.Where(action => !string.IsNullOrWhiteSpace(action.Text)).Select(action => action.ToAction())]);
    }

    private void OnRootChanged(object? sender, EventArgs e) => NotifyChanged();
}

/// <summary>프리셋 갤러리 한 줄. 기본 제공이면 <see cref="FilePath"/>가 null이다.</summary>
public sealed class PresetGalleryItemViewModel
{
    private PresetGalleryItemViewModel(
        string key,
        string category,
        string source,
        RulePreset preset,
        string? filePath,
        IMarkdownRuleRenderer renderer)
    {
        Key = key;
        Category = category;
        Source = source;
        Preset = preset;
        FilePath = filePath;
        Preview = renderer.Render(preset);
    }

    /// <summary>렌더된 Markdown. 고르면 오른쪽 미리보기 탭에 보인다.</summary>
    public string Preview { get; }

    /// <summary>다시 채워도 선택을 유지하기 위한 식별자.</summary>
    public string Key { get; }

    /// <summary>갈래 표시 문구. 언어 · 작업 방식 · 소통 · 내 라이브러리.</summary>
    public string Category { get; }

    /// <summary>어디서 왔는지. 기본 제공 또는 내 라이브러리.</summary>
    public string Source { get; }

    public RulePreset Preset { get; }

    /// <summary>내 라이브러리 파일이면 경로. 기본 제공이면 null.</summary>
    public string? FilePath { get; }

    public string Name => Preset.Name;

    public string Description => Preset.Description ?? string.Empty;

    /// <summary>갈래와 출처를 한 줄로.</summary>
    public string Badge => UiStrings.Format("RuleMaker_GalleryBadge", Category, Source);

    /// <summary>목록 항목의 접근성 이름. 스크린 리더와 UI 자동화가 이 값을 읽는다.</summary>
    public override string ToString() => Name;

    public static PresetGalleryItemViewModel FromBuiltIn(
        BuiltInPreset item,
        IRulePresetSerializer serializer,
        IMarkdownRuleRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(serializer);

        return new PresetGalleryItemViewModel(
            "builtin:" + item.Id,
            UiStrings.Get("PresetCategory_" + item.Category),
            UiStrings.Get("RuleMaker_GalleryBuiltIn"),
            serializer.Parse(BuiltInPresets.Read(item.Id)),
            filePath: null,
            renderer);
    }

    public static PresetGalleryItemViewModel FromFile(string path, RulePreset preset, IMarkdownRuleRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new PresetGalleryItemViewModel(
            "file:" + path,
            UiStrings.Get("RuleMaker_GalleryMine"),
            UiStrings.Get("RuleMaker_GalleryMine"),
            preset,
            path,
            renderer);
    }
}
