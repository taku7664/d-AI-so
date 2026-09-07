using Microsoft.UI.Xaml.Markup;

namespace Daiso.App.Strings;

/// <summary>
/// XAML에서 문구를 키로 가져오는 마크업 확장. (ARCHITECTURE §6.1)
/// <c>Text="{loc:Str Key=Common_Close}"</c> 처럼 쓴다.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class StrExtension : MarkupExtension
{
    /// <summary>`Resources.resw`의 항목 이름.</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override object ProvideValue() =>
        Key.Length == 0 ? string.Empty : UiStrings.Get(Key);
}
