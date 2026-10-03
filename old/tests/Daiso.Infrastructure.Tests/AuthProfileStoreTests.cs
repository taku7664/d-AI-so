using System.Text;
using Daiso.Core;
using Daiso.Providers.Tests;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.7 — 로그인 상태를 이름 붙여 보관하고 되돌린다.</summary>
public sealed class AuthProfileStoreTests : IDisposable
{
    private const string Credentials = """{"claudeAiOauth":{"accessToken":"secret-token-value"}}""";

    private readonly string _home = Fixtures.CreateTempDirectory();
    private readonly string _root = Fixtures.CreateTempDirectory();
    private readonly AuthProfileStore _store;
    private readonly FakeProvider _provider;

    public AuthProfileStoreTests()
    {
        _store = new AuthProfileStore(_root);
        _provider = new FakeProvider();
        _provider.Credentials.Add(new AuthFile(Path.Combine(_home, ".credentials.json"), Required: true));
        _provider.Credentials.Add(new AuthFile(Path.Combine(_home, ".claude.json"), Required: false));
    }

    public void Dispose()
    {
        foreach (var directory in new[] { _home, _root })
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // 임시 폴더 정리 실패는 테스트 결과와 무관하다.
            }
        }
    }

    [Fact]
    public void Saving_without_a_login_file_fails()
    {
        var act = () => _store.Save("없는 계정", _provider, Status());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_saved_profile_appears_in_the_list()
    {
        WriteLogin(Credentials);

        _store.Save("회사 계정", _provider, Status("빽주 · pro", "me@example.com"));

        var profile = _store.List().Should().ContainSingle().Subject;
        profile.Name.Should().Be("회사 계정");
        profile.Tool.Should().Be(ToolKind.Claude);
        profile.AccountLabel.Should().Be("빽주 · pro");
        profile.Email.Should().Be("me@example.com");
    }

    [Fact]
    public void The_stored_files_are_not_readable_as_plain_text()
    {
        WriteLogin(Credentials);
        _store.Save("회사 계정", _provider, Status());

        var stored = Directory.EnumerateFiles(_root, "*.dpapi", SearchOption.AllDirectories).ToList();

        stored.Should().NotBeEmpty();

        foreach (var file in stored)
        {
            Encoding.UTF8.GetString(File.ReadAllBytes(file))
                .Should().NotContain("secret-token-value");
        }
    }

    [Fact]
    public void The_meta_file_never_holds_the_token()
    {
        WriteLogin(Credentials);
        _store.Save("회사 계정", _provider, Status());

        var meta = Directory.EnumerateFiles(_root, "meta.json", SearchOption.AllDirectories).Single();

        File.ReadAllText(meta, Encoding.UTF8).Should().NotContain("secret-token-value");
    }

    [Fact]
    public void Applying_a_profile_puts_the_bytes_back()
    {
        WriteLogin(Credentials);
        var profile = _store.Save("회사 계정", _provider, Status());

        WriteLogin("""{"claudeAiOauth":{"accessToken":"other"}}""");
        _store.Apply(profile, _provider, current: null);

        File.ReadAllText(Path.Combine(_home, ".credentials.json"), Encoding.UTF8).Should().Be(Credentials);
    }

    [Fact]
    public void Applying_keeps_the_previous_state_so_it_can_be_undone()
    {
        WriteLogin(Credentials);
        var saved = _store.Save("회사 계정", _provider, Status());

        const string Personal = """{"claudeAiOauth":{"accessToken":"personal"}}""";
        WriteLogin(Personal);

        _store.Apply(saved, _provider, Status("개인 계정 · pro", "me@home.example"));

        var previous = _store.List()
            .Should().Contain(profile => profile.Name == AuthProfileStore.PreviousProfileName).Subject;

        _store.Apply(previous, _provider, current: null);
        File.ReadAllText(Path.Combine(_home, ".credentials.json"), Encoding.UTF8).Should().Be(Personal);
    }

    [Fact]
    public void Optional_files_are_carried_along_when_they_exist()
    {
        WriteLogin(Credentials);
        File.WriteAllText(Path.Combine(_home, ".claude.json"), """{"a":1}""", Encoding.UTF8);

        var profile = _store.Save("회사 계정", _provider, Status());

        File.Delete(Path.Combine(_home, ".claude.json"));
        _store.Apply(profile, _provider, current: null);

        File.Exists(Path.Combine(_home, ".claude.json")).Should().BeTrue();
    }

    [Fact]
    public void Saving_the_same_name_twice_replaces_it()
    {
        WriteLogin(Credentials);
        _store.Save("회사 계정", _provider, Status());
        WriteLogin("""{"claudeAiOauth":{"accessToken":"newer"}}""");
        var second = _store.Save("회사 계정", _provider, Status());

        _store.List().Should().ContainSingle();

        WriteLogin("something else");
        _store.Apply(second, _provider, current: null);

        File.ReadAllText(Path.Combine(_home, ".credentials.json"), Encoding.UTF8)
            .Should().Contain("newer");
    }

    [Fact]
    public void Removing_a_profile_deletes_it()
    {
        WriteLogin(Credentials);
        var profile = _store.Save("회사 계정", _provider, Status());

        _store.Remove(profile);

        _store.List().Should().NotContain(item => item.Name == "회사 계정");
    }

    [Fact]
    public void A_name_with_path_characters_is_still_usable()
    {
        WriteLogin(Credentials);

        var profile = _store.Save("a/b:c*계정", _provider, Status());

        WriteLogin("other");
        _store.Apply(profile, _provider, current: null);

        File.ReadAllText(Path.Combine(_home, ".credentials.json"), Encoding.UTF8).Should().Be(Credentials);
    }

    [Fact]
    public void The_previous_state_says_which_account_it_holds()
    {
        WriteLogin(Credentials);
        var saved = _store.Save("회사 계정", _provider, Status());

        WriteLogin("""{"claudeAiOauth":{"accessToken":"personal"}}""");
        _store.Apply(saved, _provider, Status("개인 계정 · pro", "me@home.example"));

        var previous = _store.List()
            .Single(profile => profile.Name == AuthProfileStore.PreviousProfileName);

        previous.AccountLabel.Should().Be("개인 계정 · pro");
        previous.Email.Should().Be("me@home.example");
    }

    private void WriteLogin(string content) =>
        File.WriteAllText(Path.Combine(_home, ".credentials.json"), content, Encoding.UTF8);

    private static AuthStatus Status(string? label = null, string? email = null) =>
        new(ToolKind.Claude, AuthState.LoggedIn, label, email, DateTimeOffset.UtcNow.AddDays(30), []);
}
