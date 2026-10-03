using Microsoft.UI.Xaml;

namespace Daiso.App.Ui;

/// <summary>
/// 목록 한 줄의 생김새를 모아 둔 사전. <c>x:Class</c> 가 있어야 <c>{x:Bind}</c> 가 산다.
/// <c>App.xaml</c> 이 병합한다.
/// <para>
/// 여기에는 <b>생성자뿐</b>이다. 처리기가 필요한 템플릿은 페이지에 남긴다 —
/// 호버 칠처럼 생김새에 속하는 것은 처리기가 아니라 <c>Style</c> 의 상태로 푼다
/// (<c>ListRowButton</c>).
/// </para>
/// </summary>
public sealed partial class ItemTemplates : ResourceDictionary
{
    public ItemTemplates() => InitializeComponent();
}
