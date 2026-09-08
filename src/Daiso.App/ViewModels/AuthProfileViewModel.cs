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
            Profiles.Add(new AuthProfileRowViewModel(profile));
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

/// <summary>목록 한 줄.</summary>
public sealed class AuthProfileRowViewModel
{
    public AuthProfileRowViewModel(AuthProfile profile) => Profile = profile;

    public AuthProfile Profile { get; }

    /// <summary>도구 한 글자.</summary>
    public string ToolInitial => Profile.Tool == ToolKind.Claude ? "C" : "X";

    /// <summary>도구 색.</summary>
    public Microsoft.UI.Xaml.Media.Brush ToolBrush =>
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Profile.Tool == ToolKind.Claude
            ? Windows.UI.Color.FromArgb(255, 122, 90, 248)
            : Windows.UI.Color.FromArgb(255, 96, 104, 120));

    public string Name => Profile.Name;

    /// <summary>계정과 저장 시각. 토큰 값은 없다.</summary>
    public string Detail => UiStrings.Format(
        "AuthProfile_Detail",
        Profile.AccountLabel ?? Profile.Email ?? UiStrings.Get("Auth_NoInfo"),
        Profile.SavedAt.ToLocalTime());

    /// <summary>저장 시점 기준 재로그인 시각.</summary>
    public string ExpiresText => Formats.Expiry(Profile.SessionExpiresAt);
}
