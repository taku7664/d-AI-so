using CommunityToolkit.Mvvm.ComponentModel;
using Daiso.App.Strings;
using Microsoft.UI.Xaml;

namespace Daiso.App.Controls;

/// <summary>
/// 페이지가 셸에 알리는 대제목과 부제. 셸이 <c>NavigationView.Header</c>에 그린다 (UI_REFACTOR_PLAN §6 규칙 1).
/// 페이지는 대제목을 직접 그리지 않는다. 그래야 "대제목 옆에 뭘 두는" 실수가 구조적으로 불가능해진다.
/// </summary>
public sealed partial class PageHeader : ObservableObject
{
    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleVisibility))]
    private string? _subtitle;

    /// <param name="titleKey">문구 키. 왼쪽 메뉴 항목과 같은 말이어야 한다 (ARCHITECTURE §6.2).</param>
    /// <param name="subtitle">고정 설명 한 줄. 상태값(건수·기간)은 부제가 아니라 본문·푸터에 둔다 (UI_REFACTOR_PLAN Stage 6).</param>
    public PageHeader(string titleKey, string? subtitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);

        _title = UiStrings.Get(titleKey);
        _subtitle = subtitle;
    }

    public Visibility SubtitleVisibility => string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>셸이 제목을 그릴 수 있게 페이지가 구현한다. 일곱 페이지 전부.</summary>
public interface IPageHeaderSource
{
    PageHeader Header { get; }
}
