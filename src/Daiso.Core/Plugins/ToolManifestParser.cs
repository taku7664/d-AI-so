using YamlDotNet.RepresentationModel;

namespace Daiso.Core.Plugins;

/// <summary>
/// 매니페스트 글을 <see cref="ToolManifest"/> 로 읽는다 (docs/PLUGIN_PLAN.md Stage 4).
/// <para>
/// <b>던지지 않는다.</b> 틀린 것은 <see cref="ToolManifestResult.Errors"/> 에 사람 말로 모아 돌려준다 —
/// 파일 하나가 깨져도 앱이 뜨고 나머지 도구가 살아야 한다.
/// </para>
/// <para>
/// <b>모르는 키는 오류다.</b> 조용히 무시하면 오타(<c>excutable</c>)를 넣고 왜 안 되는지 못 찾는다.
/// 프롬프트 파일 형식(<c>PromptPresetSerializer</c>)이 같은 이유로 같은 규칙을 쓴다.
/// </para>
/// </summary>
public static class ToolManifestParser
{
    private static readonly string[] TopLevelKeys =
    [
        "schema", "id", "name", "short", "vendor", "initial", "color", "logoPath", "order",
        "executable", "install", "sessionsRoot", "appendOnly", "rules", "context",
        "resume", "imagePasteKeys", "auth", "models", "adapter",
    ];

    /// <summary>글 한 장을 읽는다.</summary>
    /// <param name="text">파일 내용(YAML).</param>
    public static ToolManifestResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ToolManifestResult.Fail("파일이 비어 있다");
        }

        YamlMappingNode root;

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));

            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                return ToolManifestResult.Fail("맨 바깥이 `키: 값` 꼴이 아니다");
            }

            root = mapping;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return ToolManifestResult.Fail($"YAML 을 읽지 못했다 ({ex.Start.Line}행): {ex.Message}");
        }

        var errors = new List<string>();

        foreach (var key in root.Children.Keys.OfType<YamlScalarNode>().Select(node => node.Value))
        {
            if (key is not null && !TopLevelKeys.Contains(key, StringComparer.Ordinal))
            {
                errors.Add($"모르는 키: `{key}`");
            }
        }

        var schema = Int(root, "schema") ?? ToolManifest.CurrentSchema;

        if (schema > ToolManifest.CurrentSchema)
        {
            errors.Add($"이 앱이 모르는 매니페스트 판이다: schema {schema} (이 앱은 {ToolManifest.CurrentSchema} 까지)");
        }

        var rawId = Text(root, "id");

        if (!ToolKind.TryParse(rawId, out var kind))
        {
            errors.Add(rawId is null
                ? "id 가 없다"
                : $"id 로 쓸 수 없다: `{rawId}` (소문자·숫자·- 로 2~32 글자)");
        }
        else if (ToolKind.BuiltIn.Contains(kind))
        {
            errors.Add($"`{kind.Id}` 는 앱에 묻어 있는 도구의 id 라 쓸 수 없다");
        }

        var required = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["name"] = Text(root, "name"),
            ["executable"] = Text(root, "executable"),
            ["sessionsRoot"] = Text(root, "sessionsRoot"),
        };

        foreach (var (key, value) in required.Where(entry => string.IsNullOrWhiteSpace(entry.Value)))
        {
            _ = value;
            errors.Add($"{key} 이(가) 없다");
        }

        var install = Map(root, "install");
        var installCommand = Text(install, "command");
        var installUri = Text(install, "uri");

        if (installCommand is null && installUri is null)
        {
            errors.Add("install.command 이나 install.uri 중 하나는 있어야 한다");
        }

        var colors = List(root, "color");
        var colorStops = colors.Count > 0 ? colors : [Text(root, "color") ?? "#808080"];

        foreach (var stop in colorStops.Where(stop => !IsHexColour(stop)))
        {
            errors.Add($"색은 #RRGGBB 꼴이어야 한다: `{stop}`");
        }

        if (errors.Count > 0)
        {
            return new ToolManifestResult(null, errors);
        }

        var name = required["name"]!;
        var rules = Map(root, "rules");
        var auth = Map(root, "auth");

        return ToolManifestResult.Success(new ToolManifest(
            Schema: schema,
            Kind: kind,
            Display: new ToolDisplay(
                Title: name,
                Vendor: Text(root, "vendor") ?? string.Empty,
                Short: Text(root, "short") ?? name,
                Initial: Text(root, "initial") ?? name[..1].ToUpperInvariant(),
                ColorStops: colorStops,
                LogoPath: Text(root, "logoPath") ?? string.Empty,
                Order: Int(root, "order") ?? ToolDisplay.DefaultOrder),
            Executable: required["executable"]!,
            InstallCommand: installCommand ?? string.Empty,
            InstallUri: installUri,
            SessionsRoot: required["sessionsRoot"]!,
            AppendOnlySessions: Bool(root, "appendOnly") ?? true,
            RulesFileName: Text(rules, "fileName") ?? "AGENTS.md",
            SupportsInstructionImports: Bool(rules, "supportsImports") ?? false,
            ContextPatterns: List(root, "context"),
            ResumeFormat: Text(root, "resume") ?? string.Empty,
            LoginArguments: Text(root, "loginArguments") ?? string.Empty,
            FirstPromptFlag: Text(root, "firstPromptFlag") ?? string.Empty,
            ImagePasteKeys: Text(root, "imagePasteKeys") ?? "\x16",
            LoginLivesInFiles: Bool(auth, "livesInFiles") ?? true,
            AuthFiles: AuthFiles(auth),
            Models: Models(root),
            AdapterCommand: Text(Map(root, "adapter"), "command")));
    }

    private static IReadOnlyList<ManifestAuthFile> AuthFiles(YamlMappingNode? auth)
    {
        if (auth is null || !auth.Children.TryGetValue(new YamlScalarNode("files"), out var node)
            || node is not YamlSequenceNode sequence)
        {
            return [];
        }

        return [.. sequence.Children
            .OfType<YamlMappingNode>()
            .Select(entry => (Path: Text(entry, "path"), Required: Bool(entry, "required") ?? true))
            .Where(entry => entry.Path is { Length: > 0 })
            .Select(entry => new ManifestAuthFile(entry.Path!, entry.Required))];
    }

    /// <summary>
    /// 모델은 <b>고정 목록만</b> 읽는다. 명령을 돌려 얻는 길은 어댑터가 할 일이다 —
    /// 매니페스트를 읽는 것만으로 남의 명령이 돌아가면 안 된다.
    /// </summary>
    private static IReadOnlyList<ModelOption> Models(YamlMappingNode root)
    {
        var models = Map(root, "models");

        if (models is null || !models.Children.TryGetValue(new YamlScalarNode("list"), out var node)
            || node is not YamlSequenceNode sequence)
        {
            return [];
        }

        return [.. sequence.Children
            .OfType<YamlMappingNode>()
            .Select(entry => (Id: Text(entry, "id"), Name: Text(entry, "name")))
            .Where(entry => entry.Id is { Length: > 0 })
            .Select(entry => new ModelOption(entry.Id!, entry.Name ?? entry.Id!))];
    }

    private static bool IsHexColour(string value) =>
        value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit);

    private static YamlMappingNode? Map(YamlMappingNode? parent, string key) =>
        parent is not null && parent.Children.TryGetValue(new YamlScalarNode(key), out var node)
            ? node as YamlMappingNode
            : null;

    private static string? Text(YamlMappingNode? parent, string key) =>
        parent is not null && parent.Children.TryGetValue(new YamlScalarNode(key), out var node)
            && node is YamlScalarNode { Value: { Length: > 0 } value }
            ? value
            : null;

    private static int? Int(YamlMappingNode? parent, string key) =>
        int.TryParse(Text(parent, key), out var value) ? value : null;

    private static bool? Bool(YamlMappingNode? parent, string key) =>
        bool.TryParse(Text(parent, key), out var value) ? value : null;

    /// <summary>값이 목록이면 그 줄들, 글 하나면 빈 목록. 부르는 쪽이 글 하나인 경우를 따로 본다.</summary>
    private static IReadOnlyList<string> List(YamlMappingNode? parent, string key)
    {
        if (parent is null || !parent.Children.TryGetValue(new YamlScalarNode(key), out var node)
            || node is not YamlSequenceNode sequence)
        {
            return [];
        }

        return [.. sequence.Children
            .OfType<YamlScalarNode>()
            .Select(scalar => scalar.Value)
            .Where(value => value is { Length: > 0 })
            .Select(value => value!)];
    }
}
