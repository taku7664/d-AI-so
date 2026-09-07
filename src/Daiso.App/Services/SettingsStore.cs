using System.Text;
using System.Text.Json;

namespace Daiso.App.Services;

/// <summary>설정 파일 읽기·쓰기.</summary>
public interface ISettingsStore
{
    AppSettings Current { get; }

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

            File.WriteAllText(Path, JsonSerializer.Serialize(Current, Options), Utf8NoBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 설정을 못 써도 앱은 계속 돌아가야 한다.
        }
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
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }
}
