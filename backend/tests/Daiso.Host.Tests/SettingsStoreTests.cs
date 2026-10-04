using System.Text;
using Daiso.Host.Services;

namespace Daiso.Host.Tests;

/// <summary>옛 앱과 같은 <c>settings.json</c> 을 같이 쓴다 (docs/DECISIONS.md "자료 폴더").</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"daiso-settings-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Saving_keeps_fields_only_the_old_app_knows()
    {
        File.WriteAllText(_path, """
            {
              "Theme": "Dark",
              "OldAppOnly": { "Nested": [1, 2] }
            }
            """, Encoding.UTF8);

        var store = new SettingsStore(_path);
        store.Current.Theme.Should().Be("Dark");

        store.Save();

        var saved = File.ReadAllText(_path, Encoding.UTF8);
        saved.Should().Contain("\"OldAppOnly\"", because: "이쪽이 저장해도 옛 앱의 설정이 지워지면 안 된다");
        saved.Should().Contain("\"Nested\"");
        store.LastSaveError.Should().BeNull();
    }

    [Fact]
    public void Prices_ignore_case_after_loading()
    {
        File.WriteAllText(_path, """{ "Prices": { "claude-opus-5": { "InputPerMillion": 5 } } }""", Encoding.UTF8);

        var store = new SettingsStore(_path);

        store.Current.Prices.Should().ContainKey("Claude-Opus-5");
    }

    [Fact]
    public void A_broken_file_falls_back_to_defaults()
    {
        File.WriteAllText(_path, "{ 깨진", Encoding.UTF8);

        var store = new SettingsStore(_path);

        store.Current.Theme.Should().Be("System");
    }
}
