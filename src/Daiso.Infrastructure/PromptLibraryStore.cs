using System.Text;
using Daiso.Core;

namespace Daiso.Infrastructure;

/// <summary>
/// 내 프롬프트를 <c>%LOCALAPPDATA%\d-AI-so\prompts\*.md</c>에 보관하고 프로젝트에 넣는다. (ARCHITECTURE §5.8)
/// 파일 형식은 기본 제공 프롬프트와 같다(앞머리 + 본문).
/// </summary>
public sealed class PromptLibraryStore : IPromptLibrary
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;

    public PromptLibraryStore()
        : this(DefaultRoot)
    {
    }

    public PromptLibraryStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = root;
    }

    /// <summary>기본 보관 위치.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "d-AI-so",
        "prompts");

    /// <inheritdoc />
    public IReadOnlyList<PromptPreset> List()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var items = new List<PromptPreset>();

        foreach (var file in Directory
            .EnumerateFiles(_root, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                items.Add(PromptPresetSerializer.Parse(
                    Path.GetFileNameWithoutExtension(file),
                    File.ReadAllText(file, Encoding.UTF8)));
            }
            catch (FormatException)
            {
                // 형식이 깨진 파일은 목록에서 빼고 넘어간다. 하나가 깨져도 나머지는 보여야 한다.
            }
        }

        return items;
    }

    /// <inheritdoc />
    public string Save(PromptPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, SafeId(preset.Id) + ".md");
        File.WriteAllText(path, PromptPresetSerializer.Serialize(preset), Utf8);

        return path;
    }

    /// <inheritdoc />
    public void Remove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var path = Path.Combine(_root, SafeId(id) + ".md");

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc />
    public string WriteIntoProject(PromptPreset preset, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        if (!Directory.Exists(projectDirectory))
        {
            throw new DirectoryNotFoundException($"프로젝트 폴더가 없다: {projectDirectory}");
        }

        var relative = PromptPresetSerializer.ProjectRelativePath(preset)
            .Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(projectDirectory, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, preset.Body.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n", Utf8);

        return path;
    }

    /// <summary>id를 파일 이름으로 쓸 수 있게 다듬는다.</summary>
    public static string SafeId(string id) =>
        string.Join("-", id.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Replace(' ', '-');
}
