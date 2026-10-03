using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace Daiso.App.Strings;

/// <summary>
/// 화면 문구 조회. (ARCHITECTURE §6.1)
///
/// 값의 정본은 <c>Strings/ko-KR/Resources.resw</c> 하나다.
/// 조회 순서는 MRT → 어셈블리에 담긴 같은 `.resw` 파싱 → 키 문자열이다.
/// unpackaged 실행에서 MRT를 못 쓰더라도 문구가 비지 않게 하려는 순서다.
/// </summary>
public static class UiStrings
{
    /// <summary>어셈블리에 담긴 폴백 리소스 이름.</summary>
    private const string FallbackResourceName = "Daiso.App.Strings.ko-KR.Resources.resw";

    private static readonly Lazy<Func<string, string?>> Mrt = new(CreateMrtLookup);
    private static readonly Lazy<FrozenDictionary<string, string>> Fallback = new(LoadFallback);

    /// <summary>키에 해당하는 문구. 어디서도 못 찾으면 키를 그대로 돌려준다.</summary>
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (Mrt.Value(key) is { Length: > 0 } fromMrt)
        {
            return fromMrt;
        }

        return Fallback.Value.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>여러 곳에서 쓰는 "전체" 항목.</summary>
    public static string All => Get("Common_All");

    /// <summary>서식 문구. 자리표시자는 `{0}` 형태다.</summary>
    public static string Format(string key, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    /// <summary>
    /// MRT 조회기. Windows App SDK 리소스가 없는 환경(테스트·unpackaged 실패)에서는
    /// 항상 null을 주는 조회기를 돌려주고, 다시 시도하지 않는다.
    /// </summary>
    private static Func<string, string?> CreateMrtLookup()
    {
        try
        {
            var manager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager();

            return key =>
            {
                try
                {
                    return manager.MainResourceMap
                        .TryGetValue($"Resources/{key}")?
                        .ValueAsString;
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    return null;
                }
            };
        }
        catch (Exception ex) when (ex is TypeInitializationException
            or DllNotFoundException
            or InvalidOperationException
            or NotSupportedException)
        {
            return _ => null;
        }
    }

    /// <summary>어셈블리에 담긴 `.resw`를 읽는다. resw는 `data/@name` + `value` 구조의 XML이다.</summary>
    private static FrozenDictionary<string, string> LoadFallback()
    {
        try
        {
            using var stream = typeof(UiStrings).Assembly.GetManifestResourceStream(FallbackResourceName);

            if (stream is null)
            {
                return FrozenDictionary<string, string>.Empty;
            }

            return XDocument.Load(stream)
                .Descendants("data")
                .Select(data => (Name: data.Attribute("name")?.Value, Value: data.Element("value")?.Value))
                .Where(entry => entry.Name is { Length: > 0 } && entry.Value is not null)
                .ToFrozenDictionary(entry => entry.Name!, entry => entry.Value!, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or BadImageFormatException or IOException)
        {
            return FrozenDictionary<string, string>.Empty;
        }
    }
}
