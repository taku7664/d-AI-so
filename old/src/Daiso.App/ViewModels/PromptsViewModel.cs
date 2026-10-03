using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;
using Daiso.Infrastructure;

namespace Daiso.App.ViewModels;

/// <summary>
/// 내 프롬프트. 세션 첫 메시지로 한 번 실행하는 절차를 모아 두고, 고쳐 쓰고, 프로젝트에 넣는다. (ARCHITECTURE §5.8)
/// 왼쪽 목록은 기본 제공 + 내 보관함, 오른쪽은 편집기다. 기본 제공을 고치면 저장할 때 내 것으로 사본이 생긴다.
/// </summary>
public sealed partial class PromptsViewModel : ObservableObject
{
    private readonly IPromptLibrary _library;
    private readonly KnownProjects _knownProjects;

    private bool _loadingSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PromptGalleryItemViewModel? selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string editName = string.Empty;

    [ObservableProperty]
    private string editDescription = string.Empty;

    [ObservableProperty]
    private string editOutput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string editBody = string.Empty;

    [ObservableProperty]
    private int categoryIndex;

    /// <summary>0 = 전체, 1 = 내 프롬프트, 2 = 기본 제공. 바꾸면 목록을 다시 채운다.</summary>
    [ObservableProperty]
    private int sourceFilterIndex;

    /// <summary>프롬프트를 넣을 프로젝트 폴더.</summary>
    [ObservableProperty]
    private string? projectDirectory;

    [ObservableProperty]
    private string? statusText;

    /// <summary>마지막으로 프로젝트에 넣은 뒤 만들어진 시작 메시지. 복사 버튼이 쓴다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStarter))]
    private string? starterMessage;

    public PromptsViewModel(IPromptLibrary library, KnownProjects knownProjects)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(knownProjects);

        _library = library;
        _knownProjects = knownProjects;

        RefreshGallery();
        MarkClean();
    }

    /// <summary>기본 제공 뒤에 내 것.</summary>
    public ObservableCollection<PromptGalleryItemViewModel> Gallery { get; } = [];

    /// <summary>앱이 아는 프로젝트 폴더.</summary>
    public ObservableCollection<string> ProjectChoices { get; } = [];

    /// <summary>갈래 콤보 항목. <see cref="PromptCategory"/> 순서와 같다.</summary>
    public IReadOnlyList<string> Categories { get; } =
    [
        UiStrings.Get("PromptCategory_Planning"),
        UiStrings.Get("PromptCategory_Understanding"),
        UiStrings.Get("PromptCategory_Fixing"),
        UiStrings.Get("PromptCategory_Release"),
        UiStrings.Get("PromptCategory_Other"),
    ];

    /// <summary>
    /// 목록 위 출처 거르개. 기본 제공이 열둘이라 내 것이 늘 그 아래에 파묻혔다 (2026-09-11 사람의 지적).
    /// 순서는 <see cref="SourceFilterIndex"/> 가 정한다 — 전체 · 내 프롬프트 · 기본 제공 프롬프트.
    /// </summary>
    public IReadOnlyList<string> SourceFilters { get; } =
    [
        UiStrings.Get("Common_All"),
        UiStrings.Get("Prompts_Mine"),
        UiStrings.Get("Prompts_BuiltIn"),
    ];

    public bool HasSelection => SelectedItem is not null;

    /// <summary>내 것만 지울 수 있다.</summary>
    public bool CanRemove => SelectedItem is { IsMine: true };

    /// <summary>이름과 본문이 있어야 저장한다.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(EditName) && !string.IsNullOrWhiteSpace(EditBody);

    public bool HasStarter => !string.IsNullOrWhiteSpace(StarterMessage);

    /// <summary>마지막으로 실었거나 저장하거나 새로 만든 시점의 편집 내용.</summary>
    private string _cleanSnapshot = string.Empty;

    /// <summary>저장하지 않은 편집이 있는가. 다른 항목을 고르거나 새로 만들기 전에 화면이 묻는다.</summary>
    public bool IsDirty => Snapshot() != _cleanSnapshot;

    private string Snapshot() => string.Join("\u001f", EditName, EditDescription, EditOutput, CategoryIndex, EditBody);

    private void MarkClean() => _cleanSnapshot = Snapshot();

    /// <summary>목록 항목을 편집기에 싹 싣는다. 화면이 확인을 거친 뒤 부른다.</summary>
    public void LoadItem(PromptGalleryItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _loadingSelection = true;

        try
        {
            EditName = item.Preset.Name;
            EditDescription = item.Preset.Description;
            EditOutput = item.Preset.Output ?? string.Empty;
            EditBody = item.Preset.Body;
            CategoryIndex = (int)item.Preset.Category;
            StarterMessage = null;
        }
        finally
        {
            _loadingSelection = false;
        }

        MarkClean();
    }

    /// <summary>
    /// 편집기 내용으로 모델을 만든다. id는 고른 항목의 것을 그대로 쓴다(기본 제공이면 planning-interview 같은 영문 슬러그).
    /// 새로 만든 것만 이름에서 id를 만든다. 그래야 프로젝트에 넣은 파일 이름이 안정된다.
    /// </summary>
    public PromptPreset BuildPreset()
    {
        var id = SelectedItem is { } selected
            ? selected.Preset.Id
            : PromptLibraryStore.SafeId(EditName);

        return new PromptPreset(
            id,
            EditName.Trim(),
            EditDescription.Trim(),
            (PromptCategory)Math.Clamp(CategoryIndex, 0, Categories.Count - 1),
            string.IsNullOrWhiteSpace(EditOutput) ? null : EditOutput.Trim(),
            EditBody);
    }

    /// <summary>
    /// 목록을 다시 채운다. 선택은 유지한다.
    /// <para>내 것이 앞, 기본 제공이 뒤다 — 내가 만든 것을 열둘 아래에서 찾게 두지 않는다.</para>
    /// </summary>
    [RelayCommand]
    public void RefreshGallery()
    {
        var keep = SelectedItem?.Key;

        Gallery.Clear();

        if (SourceFilterIndex != 2)
        {
            foreach (var prompt in _library.List())
            {
                Gallery.Add(new PromptGalleryItemViewModel(prompt, isMine: true));
            }
        }

        if (SourceFilterIndex != 1)
        {
            foreach (var prompt in BuiltInPrompts.List())
            {
                Gallery.Add(new PromptGalleryItemViewModel(prompt, isMine: false));
            }
        }

        SelectedItem = Gallery.FirstOrDefault(item => item.Key == keep);
    }

    /// <summary>거르개를 바꾸면 목록만 다시 채운다. 편집 중인 내용은 건드리지 않는다.</summary>
    partial void OnSourceFilterIndexChanged(int value) => RefreshGallery();

    /// <summary>빈 편집기로 시작한다.</summary>
    [RelayCommand]
    public void New()
    {
        SelectedItem = null;
        EditName = UiStrings.Get("Prompts_NewName");
        EditDescription = string.Empty;
        EditOutput = string.Empty;
        EditBody = string.Empty;
        CategoryIndex = 0;
        StarterMessage = null;
        StatusText = UiStrings.Get("Prompts_Created");
        MarkClean();
    }

    /// <summary>편집기 내용을 내 보관함에 저장한다.</summary>
    [RelayCommand]
    public void SaveToLibrary()
    {
        if (!CanSave)
        {
            StatusText = UiStrings.Get("Prompts_NeedName");
            return;
        }

        var preset = BuildPreset();
        _library.Save(preset);

        RefreshGallery();
        SelectedItem = Gallery.FirstOrDefault(item => item.IsMine && item.Preset.Id == preset.Id) ?? SelectedItem;
        MarkClean();
        StatusText = UiStrings.Format("Prompts_Saved", preset.Name);
    }

    /// <summary>고른 내 프롬프트를 지운다. 확인은 화면이 받는다.</summary>
    public void RemoveSelected()
    {
        if (SelectedItem is not { IsMine: true } item)
        {
            return;
        }

        _library.Remove(item.Preset.Id);
        SelectedItem = null;
        RefreshGallery();
        StatusText = UiStrings.Format("Prompts_Removed", item.Name);
    }

    /// <summary>편집기 내용을 프로젝트의 docs/prompts 에 쓰고 시작 메시지를 만든다.</summary>
    [RelayCommand]
    public void WriteIntoProject()
    {
        if (string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            StatusText = UiStrings.Get("Prompts_NeedProject");
            return;
        }

        if (!CanSave)
        {
            StatusText = UiStrings.Get("Prompts_NeedName");
            return;
        }

        var preset = BuildPreset();
        var path = _library.WriteIntoProject(preset, ProjectDirectory.Trim());

        StarterMessage = PromptPresetSerializer.StarterMessage(preset);
        StatusText = UiStrings.Format("Prompts_Written", path);
    }

    /// <summary>앱이 아는 프로젝트를 다시 읽는다.</summary>
    public async Task LoadProjectChoicesAsync(CancellationToken ct = default)
    {
        var known = await _knownProjects.ListAsync(ct).ConfigureAwait(true);

        ProjectChoices.Clear();

        foreach (var path in known)
        {
            ProjectChoices.Add(path);
        }
    }

    /// <summary>기본 제공을 고르면 왜 삭제가 잠기는지 상태 줄이 말한다. 잠긴 버튼은 스스로 설명하지 못한다.</summary>
    partial void OnSelectedItemChanged(PromptGalleryItemViewModel? value)
    {
        if (value is { IsMine: false })
        {
            StatusText = UiStrings.Get("Prompts_BuiltInHint");
        }
    }

    partial void OnEditBodyChanged(string value)
    {
        if (!_loadingSelection)
        {
            StarterMessage = null;
        }
    }
}

/// <summary>목록 한 줄.</summary>
public sealed class PromptGalleryItemViewModel
{
    public PromptGalleryItemViewModel(PromptPreset preset, bool isMine)
    {
        Preset = preset;
        IsMine = isMine;
    }

    public PromptPreset Preset { get; }

    /// <summary>내 보관함의 것인가. 기본 제공은 지우거나 덮어쓸 수 없다.</summary>
    public bool IsMine { get; }

    public string Key => (IsMine ? "mine:" : "builtin:") + Preset.Id;

    public string Name => Preset.Name;

    public string Description => Preset.Description;

    /// <summary>갈래와 출처.</summary>
    public string Badge => UiStrings.Format(
        "Prompts_Badge",
        UiStrings.Get("PromptCategory_" + Preset.Category),
        UiStrings.Get(IsMine ? "Prompts_Mine" : "Prompts_BuiltIn"));

    /// <summary>접근성 이름.</summary>
    public override string ToString() => Name;
}
