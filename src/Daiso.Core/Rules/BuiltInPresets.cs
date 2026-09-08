using System.Reflection;

namespace Daiso.Core;

/// <summary>기본 제공 프리셋의 갈래. 갤러리에서 묶어 보여주는 데 쓴다.</summary>
public enum PresetCategory
{
    /// <summary>특정 언어·플랫폼의 코딩 습관.</summary>
    Language,

    /// <summary>언어와 무관한 작업 방식. Git, 테스트, 리뷰 등.</summary>
    Workflow,

    /// <summary>응답 언어·설명 방식.</summary>
    Communication,
}

/// <summary>기본 제공 프리셋 한 개의 카탈로그 항목. 본문은 <see cref="BuiltInPresets.Read"/>로 읽는다.</summary>
/// <param name="Id">리소스 파일 이름(확장자 제외). 화면에는 쓰지 않는다.</param>
/// <param name="Category">갈래.</param>
/// <param name="Name">프리셋 이름. 파일 안의 <c>name</c>과 같다.</param>
/// <param name="Description">한 줄 설명. 파일 안의 <c>description</c>과 같다.</param>
public sealed record BuiltInPreset(string Id, PresetCategory Category, string Name, string? Description);

/// <summary>
/// 앱에 묻어 있는 기본 프리셋. (ARCHITECTURE §5.2)
/// 사용자가 처음 규칙을 만들 때 빈 화면 대신 고를 수 있는 출발점이다.
/// 파일은 <c>Resources/Presets/*.daiso</c>에 임베디드 리소스로 들어 있고, 형식은 사용자 파일과 완전히 같다.
/// </summary>
public static class BuiltInPresets
{
    private const string ResourcePrefix = "Daiso.Core.Resources.Presets.";
    private const string Extension = ".daiso";

    /// <summary>갤러리에 보이는 순서. 갈래별로 묶고, 갈래 안에서는 자주 쓰는 것을 앞에 둔다.</summary>
    private static readonly (string Id, PresetCategory Category)[] Catalog =
    [
        ("git", PresetCategory.Workflow),
        ("testing", PresetCategory.Workflow),
        ("review", PresetCategory.Workflow),
        ("refactor", PresetCategory.Workflow),
        ("security", PresetCategory.Workflow),
        ("docs", PresetCategory.Workflow),
        ("api", PresetCategory.Workflow),
        ("csharp", PresetCategory.Language),
        ("cpp", PresetCategory.Language),
        ("python", PresetCategory.Language),
        ("typescript", PresetCategory.Language),
        ("rust", PresetCategory.Language),
        ("unity", PresetCategory.Language),
        ("communication", PresetCategory.Communication),
    ];

    /// <summary>카탈로그 순서대로 항목을 돌려준다. 이름과 설명은 파일에서 읽는다.</summary>
    /// <param name="serializer">파일을 파싱할 직렬화기. 이름·설명을 뽑는 데만 쓴다.</param>
    public static IReadOnlyList<BuiltInPreset> List(IRulePresetSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        var items = new List<BuiltInPreset>(Catalog.Length);

        foreach (var (id, category) in Catalog)
        {
            var preset = serializer.Parse(Read(id));
            items.Add(new BuiltInPreset(id, category, preset.Name, preset.Description));
        }

        return items;
    }

    /// <summary>프리셋 파일 본문(.daiso YAML)을 그대로 돌려준다.</summary>
    /// <exception cref="InvalidOperationException">카탈로그에 없는 id.</exception>
    public static string Read(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var name = ResourcePrefix + id + Extension;

        using var stream = typeof(BuiltInPresets).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"기본 프리셋이 없다: {id}");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>카탈로그에 있는 id 목록. 테스트와 CLI가 쓴다.</summary>
    public static IReadOnlyList<string> Ids => [.. Catalog.Select(entry => entry.Id)];

    /// <summary>임베디드 리소스에 실제로 들어 있는 프리셋 파일 이름. 카탈로그와 어긋나면 테스트가 잡는다.</summary>
    public static IReadOnlyList<string> EmbeddedIds =>
    [
        .. typeof(BuiltInPresets).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(Extension, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..^Extension.Length])
            .OrderBy(name => name, StringComparer.Ordinal),
    ];
}
