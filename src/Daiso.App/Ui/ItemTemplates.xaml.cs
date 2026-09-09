using Microsoft.UI.Xaml;

namespace Daiso.App.Ui;

/// <summary>
/// 목록 한 줄의 생김새를 모아 둔 사전. <c>x:Class</c> 가 있어야 <c>{x:Bind}</c> 가 산다.
/// <c>App.xaml</c> 이 병합한다.
/// </summary>
public sealed partial class ItemTemplates : ResourceDictionary
{
    public ItemTemplates() => InitializeComponent();
}
