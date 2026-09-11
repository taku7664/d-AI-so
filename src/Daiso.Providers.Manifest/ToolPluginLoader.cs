using Daiso.Core;
using Daiso.Core.Plugins;
using Daiso.Providers.Common;

namespace Daiso.Providers.Manifest;

/// <summary>플러그인 하나를 읽은 결과. 실패해도 어느 파일이 왜 안 됐는지 남긴다.</summary>
/// <param name="FilePath">읽은 매니페스트 파일.</param>
/// <param name="Provider">성공이면 앱이 쓸 도구. 실패면 null.</param>
/// <param name="Errors">사람이 읽는 이유. 성공이면 비어 있다.</param>
public sealed record ToolPluginLoad(string FilePath, ManifestProvider? Provider, IReadOnlyList<string> Errors)
{
    public bool Ok => Provider is not null;

    /// <summary>목록·오류 화면에 쓰는 이름. 읽지도 못했으면 파일 이름이 이름 노릇을 한다.</summary>
    public string Label => Provider?.Display.Title ?? Path.GetFileName(FilePath);
}

/// <summary>
/// 플러그인 폴더를 읽어 도구를 만든다 (docs/PLUGIN_PLAN.md Stage 4).
/// <para>
/// <b>하나가 깨져도 나머지를 싣는다.</b> 매니페스트 하나 때문에 앱이 안 뜨면 고칠 길이 없다 —
/// 실패는 목록에 담아 설정 화면이 보여 준다(Stage 6).
/// </para>
/// <para>
/// <b>같은 id 가 둘이면 둘 다 안 싣는다.</b> 먼저 읽은 것이 이기게 하면 파일 이름 순서에 따라 앱이 달라진다 —
/// 조용히 달라지는 쪽이라 아예 막고 사람에게 말한다 (docs/PLUGIN_PLAN.md §8).
/// </para>
/// </summary>
public sealed class ToolPluginLoader
{
    private readonly string _directory;
    private readonly ProviderHome _home;

    /// <param name="directory">매니페스트를 두는 폴더. 없으면 플러그인이 없는 것이다.</param>
    /// <param name="home"><c>{USERPROFILE}</c> 가 가리키는 곳.</param>
    public ToolPluginLoader(string directory, ProviderHome home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(home);

        _directory = directory;
        _home = home;
    }

    /// <summary>기본 자리: <c>%USERPROFILE%\.daiso\tools</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".daiso",
            "tools");

    /// <summary>폴더를 읽는다. 파일 이름 순으로 훑되, 결과 순서는 표시 순서가 따로 정한다.</summary>
    public IReadOnlyList<ToolPluginLoad> Load()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var loads = new List<ToolPluginLoad>();

        foreach (var path in Files())
        {
            loads.Add(ReadOne(path));
        }

        return RejectDuplicateIds(loads);
    }

    /// <summary><c>.yaml</c> 과 <c>.yml</c> 둘 다 받는다. 둘 중 어느 쪽인지로 사람을 시험하지 않는다.</summary>
    private IEnumerable<string> Files()
    {
        IEnumerable<string> found;

        try
        {
            found = Directory
                .EnumerateFiles(_directory)
                .Where(path => Path.GetExtension(path) is ".yaml" or ".yml");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return found.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private ToolPluginLoad ReadOne(string path)
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ToolPluginLoad(path, null, [$"파일을 열지 못했다: {ex.Message}"]);
        }

        var result = ToolManifestParser.Parse(text);

        return result.Manifest is { } manifest
            ? new ToolPluginLoad(path, new ManifestProvider(manifest, _directory, _home), [])
            : new ToolPluginLoad(path, null, result.Errors);
    }

    /// <summary>
    /// 같은 id 를 쓴 것들은 전부 오류로 내린다. 앱에 묻어 있는 도구 id 는 파서가 이미 막았다.
    /// </summary>
    private static IReadOnlyList<ToolPluginLoad> RejectDuplicateIds(List<ToolPluginLoad> loads)
    {
        var clashes = loads
            .Where(load => load.Ok)
            .GroupBy(load => load.Provider!.Kind)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();

        if (clashes.Count == 0)
        {
            return loads;
        }

        return [.. loads.Select(load => clashes.Contains(load)
            ? load with
            {
                Provider = null,
                Errors = [$"id `{load.Provider!.Kind.Id}` 를 쓰는 매니페스트가 둘 이상이다. 어느 것도 싣지 않는다"],
            }
            : load)];
    }
}
