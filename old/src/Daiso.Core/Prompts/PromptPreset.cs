using System.Text;

namespace Daiso.Core;

/// <summary>프롬프트의 갈래. 갤러리에서 묶어 보여준다.</summary>
public enum PromptCategory
{
    /// <summary>무엇을 만들지 정하는 인터뷰·기획.</summary>
    Planning,

    /// <summary>이미 있는 코드를 파악하는 절차.</summary>
    Understanding,

    /// <summary>버그·리팩터링처럼 고치는 절차.</summary>
    Fixing,

    /// <summary>배포·점검 절차.</summary>
    Release,

    /// <summary>넷 중 어디에도 안 들어가는 것. 맨 뒤에 붙인다 — 값이 파일에 글자로 적히므로 순서를 바꾸지 않는다.</summary>
    Other,
}

/// <summary>
/// 세션의 첫 메시지로 넣어 한 번 실행하는 절차형 프롬프트. (ARCHITECTURE §5.8)
/// 규칙(.daiso)과 달리 매 세션 붙는 제약이 아니라, 필요할 때 한 번 돌리는 인터뷰·점검 절차다.
/// </summary>
/// <param name="Id">파일 이름(확장자 제외). 프로젝트에 넣을 때 파일 이름으로도 쓴다.</param>
/// <param name="Name">사람이 읽는 이름.</param>
/// <param name="Description">한 줄 설명.</param>
/// <param name="Category">갈래.</param>
/// <param name="Output">절차가 끝나면 만들어지는 파일. 없으면 null.</param>
/// <param name="Body">프롬프트 본문(Markdown). 앞머리(front matter)는 빼고 본문만.</param>
public sealed record PromptPreset(
    string Id,
    string Name,
    string Description,
    PromptCategory Category,
    string? Output,
    string Body);

/// <summary>
/// 프롬프트 파일 형식. 맨 위 <c>---</c> 사이에 <c>key: value</c> 앞머리, 그 아래가 본문이다.
/// 앞머리 키는 name, description, category, output 넷뿐이다. 모르는 키는 오류다.
/// </summary>
public static class PromptPresetSerializer
{
    private const string Fence = "---";

    /// <summary>파일 내용을 모델로 읽는다.</summary>
    /// <param name="id">파일 이름(확장자 제외).</param>
    /// <param name="text">파일 내용.</param>
    /// <exception cref="FormatException">앞머리가 없거나 필수 키가 빠졌거나 모르는 키가 있다.</exception>
    public static PromptPreset Parse(string id, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != Fence)
        {
            throw new FormatException("프롬프트 파일은 '---' 앞머리로 시작해야 한다");
        }

        var end = Array.FindIndex(lines, 1, line => line.Trim() == Fence);

        if (end < 0)
        {
            throw new FormatException("앞머리를 닫는 '---' 가 없다");
        }

        string? name = null, description = null, category = null, output = null;

        for (var i = 1; i < end; i++)
        {
            var line = lines[i];

            if (line.Trim().Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                throw new FormatException($"앞머리 {i + 1}행이 'key: value' 꼴이 아니다");
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "name": name = value; break;
                case "description": description = value; break;
                case "category": category = value; break;
                case "output": output = value.Length == 0 ? null : value; break;
                default: throw new FormatException($"앞머리에 모르는 키가 있다: {key}");
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FormatException("앞머리에 name 이 없다");
        }

        // TryParse 는 "3" 같은 숫자 문자열도 받아 정의에 없는 값(PromptCategory)99 를 만든다. IsDefined 로 막는다
        if (!Enum.TryParse<PromptCategory>(category, ignoreCase: true, out var parsedCategory)
            || !Enum.IsDefined(parsedCategory))
        {
            throw new FormatException($"category 가 planning, understanding, fixing, release, other 중 하나가 아니다: {category}");
        }

        var body = string.Join('\n', lines.Skip(end + 1)).Trim('\n');

        if (body.Length == 0)
        {
            throw new FormatException("본문이 비어 있다");
        }

        return new PromptPreset(id, name, description ?? string.Empty, parsedCategory, output, body);
    }

    /// <summary>모델을 파일 내용으로 만든다. 줄바꿈은 LF.</summary>
    public static string Serialize(PromptPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var builder = new StringBuilder();
        builder.Append(Fence).Append('\n');
        builder.Append("name: ").Append(preset.Name.Trim()).Append('\n');
        builder.Append("description: ").Append(preset.Description.Trim()).Append('\n');
        builder.Append("category: ").Append(preset.Category.ToString().ToLowerInvariant()).Append('\n');

        if (preset.Output is { Length: > 0 } output)
        {
            builder.Append("output: ").Append(output.Trim()).Append('\n');
        }

        builder.Append(Fence).Append('\n').Append('\n');
        builder.Append(preset.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n')).Append('\n');

        return builder.ToString();
    }

    /// <summary>프로젝트 안에서 프롬프트 파일이 놓이는 상대 경로. 슬래시로 쓴다. 두 도구 모두 그대로 읽는다.</summary>
    public static string ProjectRelativePath(PromptPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return "docs/prompts/" + preset.Id + ".md";
    }

    /// <summary>
    /// 세션을 열 때 넣는 첫 메시지. 본문을 통째로 인자로 넘기지 않고 파일을 읽으라고 시킨다.
    /// 25KB짜리 프롬프트를 명령줄에 넣으면 길이 한도와 인용에 걸린다.
    /// </summary>
    public static string StarterMessage(PromptPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var message = $"{ProjectRelativePath(preset)} 파일을 읽고 그 절차대로 진행해 주세요.";

        return preset.Output is { Length: > 0 } output
            ? message + $" 결과는 {output} 에 씁니다."
            : message;
    }
}

/// <summary>
/// 앱에 묻어 있는 절차형 프롬프트. 파일은 <c>Resources/Prompts/*.md</c> 임베디드 리소스이고
/// 형식은 사용자 프롬프트 파일과 완전히 같다.
/// </summary>
public static class BuiltInPrompts
{
    private const string ResourcePrefix = "Daiso.Core.Resources.Prompts.";
    private const string Extension = ".md";

    /// <summary>갤러리 순서. 기획 → 파악 → 수정 → 배포.</summary>
    private static readonly string[] Catalog =
    [
        "planning-interview",
        "feature-plan",
        "feature-proposal",
        "api-spec",
        "codebase-tour",
        "code-review",
        "bug-repro",
        "refactor-plan",
        "test-suite",
        "performance-tune",
        "release-check",
        "retro",
    ];

    /// <summary>카탈로그 순서대로 읽는다.</summary>
    public static IReadOnlyList<PromptPreset> List() =>
        [.. Catalog.Select(id => PromptPresetSerializer.Parse(id, Read(id)))];

    /// <summary>카탈로그의 id 목록.</summary>
    public static IReadOnlyList<string> Ids => Catalog;

    /// <summary>임베디드 리소스에 실제로 들어 있는 파일 이름. 카탈로그와 어긋나면 테스트가 잡는다.</summary>
    public static IReadOnlyList<string> EmbeddedIds =>
    [
        .. typeof(BuiltInPrompts).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(Extension, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..^Extension.Length])
            .OrderBy(name => name, StringComparer.Ordinal),
    ];

    /// <summary>파일 내용을 그대로 돌려준다.</summary>
    public static string Read(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var name = ResourcePrefix + id + Extension;

        using var stream = typeof(BuiltInPrompts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"기본 프롬프트가 없다: {id}");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
