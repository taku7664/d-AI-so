using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daiso.App.Services;
using Daiso.App.Strings;
using Daiso.Core;

namespace Daiso.App.ViewModels;

/// <summary>
/// 로그인 프로필. 여러 계정을 오갈 때 지금 상태를 이름 붙여 두고 되돌린다. (ARCHITECTURE §5.7)
/// 토큰 값은 이 뷰모델에 들어오지 않는다.
/// </summary>
public sealed partial class AuthProfileViewModel : ObservableObject
{
    private readonly IAuthProfileStore _store;
    private readonly IReadOnlyList<IProvider> _providers;

    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private AuthProfileRowViewModel? selectedProfile;

    public AuthProfileViewModel(IAuthProfileStore store, IEnumerable<IProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(providers);

        _store = store;
        _providers = providers.ToList();

        Reload();
    }

    /// <summary>보관 중인 프로필.</summary>
    public ObservableCollection<AuthProfileRowViewModel> Profiles { get; } = [];

    /// <summary>
    /// 줄의 되돌리기·지우기가 실제로 할 일. 요약 화면 뷰모델이 한 번 채운다.
    /// <para>
    /// <b>줄을 만드는 자리에서 붙인다.</b> 밖에서 붙이면 <see cref="Reload"/> 가 줄을 새로 만들 때마다
    /// 떨어져 나가고, 한 번 빠뜨리면 단추가 눌려도 아무 일이 없다 — 조용히 틀리는 쪽이다.
    /// </para>
    /// </summary>
    public Func<AuthProfileRowViewModel, Task>? RowUseAction { get; set; }

    /// <inheritdoc cref="RowUseAction" />
    public Func<AuthProfileRowViewModel, Task>? RowRemoveAction { get; set; }

    /// <summary>프로필이 하나라도 있는가.</summary>
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>비었는가. 안내 문구를 띄운다.</summary>
    public bool IsEmpty => Profiles.Count == 0;

    /// <summary>목록을 다시 읽는다.</summary>
    [RelayCommand]
    public void Reload()
    {
        Profiles.Clear();

        foreach (var profile in _store.List())
        {
            Profiles.Add(new AuthProfileRowViewModel(profile)
            {
                UseAction = RowUseAction,
                RemoveAction = RowRemoveAction,
            });
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>지금 로그인 상태를 이름 붙여 저장한다.</summary>
    public void Save(ToolKind tool, string name, AuthStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(status);

        var provider = _providers.First(item => item.Kind == tool);
        var saved = _store.Save(name, provider, status);

        Reload();
        StatusText = UiStrings.Format("AuthProfile_Saved", saved.Name);
    }

    /// <summary>고른 프로필을 현재 로그인으로 되돌린다.</summary>
    /// <param name="current">지금 로그인 상태. "직전 상태"에 어느 계정이었는지 남기는 데 쓴다.</param>
    public void Apply(AuthProfileRowViewModel row, AuthStatus? current)
    {
        ArgumentNullException.ThrowIfNull(row);

        var provider = _providers.First(item => item.Kind == row.Profile.Tool);
        _store.Apply(row.Profile, provider, current);

        Reload();
        StatusText = UiStrings.Format("AuthProfile_Applied", row.Profile.Name);
    }

    /// <summary>프로필을 지운다.</summary>
    public void Remove(AuthProfileRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        _store.Remove(row.Profile);

        Reload();
        StatusText = UiStrings.Format("AuthProfile_Removed", row.Profile.Name);
    }
}

/// <summary>
/// 목록 한 줄. 되돌리기·지우기 명령을 스스로 들고 있다 — 그래야 이 줄의 생김새가
/// 페이지 코드비하인드 없이 설 수 있다 (docs/REVIEW_BACKLOG.md D1).
/// </summary>
public sealed partial class AuthProfileRowViewModel : ObservableObject
{
    /// <summary>이 줄에 무엇을 할지 아는 쪽. 요약 화면 뷰모델이 채운다.</summary>
    public Func<AuthProfileRowViewModel, Task>? UseAction { get; set; }

    /// <summary>지우기를 맡는 쪽.</summary>
    public Func<AuthProfileRowViewModel, Task>? RemoveAction { get; set; }

    public AuthProfileRowViewModel(AuthProfile profile) => Profile = profile;

    public AuthProfile Profile { get; }

    /// <summary>이 프로필을 현재 로그인으로 되돌린다.</summary>
    [RelayCommand]
    public Task UseAsync() => UseAction?.Invoke(this) ?? Task.CompletedTask;

    /// <summary>이 프로필을 지운다.</summary>
    [RelayCommand]
    public Task RemoveAsync() => RemoveAction?.Invoke(this) ?? Task.CompletedTask;

    /// <summary>도구 한 글자.</summary>
    public string ToolInitial => ToolLook.Initial(Profile.Tool);

    /// <summary>도구 색.</summary>
    public Microsoft.UI.Xaml.Media.Brush ToolBrush => ToolLook.Brush(Profile.Tool);

    public string Name => Profile.Name;

    /// <summary>계정과 저장 시각. 토큰 값은 없다.</summary>
    public string Detail => UiStrings.Format(
        "AuthProfile_Detail",
        Profile.AccountLabel ?? Profile.Email ?? UiStrings.Get("Auth_NoInfo"),
        Profile.SavedAt.ToLocalTime());

    /// <summary>저장 시점 기준 재로그인 시각.</summary>
    public string ExpiresText => Formats.Expiry(Profile.SessionExpiresAt);
}
