using System.Text;
using System.Text.Json;

namespace Daiso.App.Services;

/// <summary>설정 파일 읽기·쓰기.</summary>
public interface ISettingsStore
{
    AppSettings Current { get; }

    /// <summary>설정이 저장될 때마다 발생한다. 단가표 변경 즉시 재계산에 쓴다.</summary>
    event EventHandler? Changed;

    /// <summary>지금 값을 디스크에 쓴다.</summary>
    void Save();
}

/// <summary>`%LOCALAPPDATA%\d-AI-so\settings.json` 기반 설정 저장소.</summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public SettingsStore()
    {
        Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "d-AI-so",
            "settings.json");

        Current = Load(Path);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public AppSettings Current { get; private set; }

    /// <summary>설정 파일 경로.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public void Save()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 임시 파일에 다 쓰고 나서 자리를 바꾼다. 쓰는 도중에 죽어도 기존 설정이 반쪽으로 남지 않는다
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Current, Options), Utf8NoBom);
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 설정을 못 써도 앱은 계속 돌아가야 한다.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 역직렬화가 지운 사전 비교자를 되살린다.
    /// <c>Prices</c>는 필드 초기값에서 <see cref="StringComparer.OrdinalIgnoreCase"/>로 만들지만,
    /// System.Text.Json은 set 가능한 사전 속성을 채울 때 사전을 **새로** 만들어 기본(대소문자 구분) 비교자를 쓴다.
    /// 그대로 두면 settings.json 을 한 번 읽은 뒤부터 <c>claude-opus-5</c>와 <c>Claude-Opus-5</c>가 서로 다른 모델이 되어
    /// 사람이 적어 둔 단가가 비용 계산에서 조용히 빠진다.
    /// </summary>
    private static AppSettings Normalize(AppSettings settings)
    {
        var prices = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase);

        // 대소문자만 다른 키가 둘 있으면 나중 것을 남긴다. 생성자로 옮기면 그 경우 예외가 난다
        foreach (var entry in settings.Prices)
        {
            prices[entry.Key] = entry.Value;
        }

        settings.Prices = prices;

        return settings;
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }
}
