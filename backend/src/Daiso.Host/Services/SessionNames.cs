using System.Text;
using System.Text.Json;

namespace Daiso.Host.Services;

/// <summary>
/// 사람이 붙인 세션 이름. 첫 질문을 제목으로 쓰면 세션끼리 구별이 안 돼서 이름을 붙인다
/// (docs/DECISIONS.md "세션·방 이름은 DAIso 쪽에 따로 둔다").
/// <para>
/// 도구가 쓰는 세션 파일은 고치지 않는다. 옛 앱과 같이 쓰는 인덱스 DB 의 모양도 바꾸지 않으려고
/// 자료 폴더의 <c>session-names.json</c>(세션 파일 경로 → 이름)에 둔다.
/// </para>
/// </summary>
public sealed class SessionNames
{
    public const string FileName = "session-names.json";

    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _names;

    public SessionNames(DaisoHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _path = Path.Combine(options.DataDirectory, FileName);
        _names = Load(_path);
    }

    /// <summary>붙인 이름. 없으면 null.</summary>
    public string? Get(string filePath)
    {
        lock (_gate)
        {
            return _names.GetValueOrDefault(filePath);
        }
    }

    /// <summary>이름을 붙인다. 비우거나 null 이면 뗀다.</summary>
    public void Set(string filePath, string? name)
    {
        lock (_gate)
        {
            var next = new Dictionary<string, string>(_names, StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(name))
            {
                next.Remove(filePath);
            }
            else
            {
                next[filePath] = name.Trim();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temporary, _path, overwrite: true);
            _names = next;
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) is { } names)
            {
                return new Dictionary<string, string>(names, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 못 읽으면 이름 없이 시작한다. 다음에 붙이면 새로 쓴다
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
