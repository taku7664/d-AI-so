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
    ];

    public bool HasSelection => SelectedItem is not null;

    /// <summary>내 것만 지울 수 있다.</summary>
    public bool CanRemove => SelectedItem is { IsMine: true };

    /// <summary>이름과 본문이 있어야 저장한다.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(EditName) && !string.IsNullOrWhiteSpace(EditBody);

    public bool HasStarter => !string.IsNullOrWhiteSpace(StarterMessage);

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

    /// <summary>목록을 다시 채운다. 선택은 유지한다.</summary>
    [RelayCommand]
    public void RefreshGallery()
    {
        var keep = SelectedItem?.Key;

        Gallery.Clear();

        foreach (var prompt in BuiltInPrompts.List())
        {
            Gallery.Add(new PromptGalleryItemViewModel(prompt, isMine: false));
        }

        foreach (var prompt in _library.List())
        {
            Gallery.Add(new PromptGalleryItemViewModel(prompt, isMine: true));
        }

        SelectedItem = Gallery.FirstOrDefault(item => item.Key == keep) ?? Gallery.FirstOrDefault();
    }

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

    partial void OnSelectedItemChanged(PromptGalleryItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        _loadingSelection = true;

        try
        {
            EditName = value.Preset.Name;
            EditDescription = value.Preset.Description;
            EditOutput = value.Preset.Output ?? string.Empty;
            EditBody = value.Preset.Body;
            CategoryIndex = (int)value.Preset.Category;
            StarterMessage = null;
        }
        finally
        {
            _loadingSelection = false;
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
