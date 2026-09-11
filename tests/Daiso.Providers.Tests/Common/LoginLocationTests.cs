using Daiso.Core;
using Daiso.Providers.Antigravity;
using Daiso.Providers.Common;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;

namespace Daiso.Providers.Tests.Common;

/// <summary>
/// 계정 보관·전환(<c>IAuthProfileStore</c>)은 <see cref="IProvider.AuthFiles"/> 를 복사했다 되돌리는 방식이다.
/// 그러니 <b>로그인이 정말 그 파일에 들어 있어야</b> 뜻이 있다.
///
/// <para>
/// Antigravity 는 아니다 — 로그인은 Windows 자격 증명 관리자에 있고 토큰은 파일에 없다(2026-09-09 실측).
/// 그걸 모르고 보관을 내주면 "저장해 뒀다"고 믿게 해 놓고 되돌리기가 아무 일도 안 한다.
/// 그 사실을 <see cref="IProvider.LoginLivesInFiles"/> 로 밝히고, 여기서 잠근다.
/// </para>
/// </summary>
public sealed class LoginLocationTests
{
    /// <summary>
    /// 어느 폴더인지는 이 테스트와 무관하다. 파일을 읽지 않고 계약만 본다.
    /// <para>
    /// <c>LoginLivesInFiles</c> 는 기본 인터페이스 멤버라 구체 클래스로는 안 보인다 —
    /// 그래서 <see cref="IProvider"/> 로 받는다. 실제로 이 값을 보는 쪽(<c>AuthProfileStore</c>)도 그렇게 본다.
    /// </para>
    /// </summary>
    private static readonly ProviderHome Home = new(Path.Combine(Path.GetTempPath(), "daiso-login-contract"));

    [Fact]
    public void Claude_keeps_its_login_in_files()
    {
        IProvider provider = new ClaudeProvider(Home, new FakeProcessProbe());

        provider.LoginLivesInFiles.Should().BeTrue();
        provider.AuthFiles.Should().Contain(file => file.Required,
            because: "로그인을 담은 파일이 반드시 하나는 있어야 보관이 뜻을 가진다");
    }

    [Fact]
    public void Codex_keeps_its_login_in_files()
    {
        IProvider provider = new CodexProvider(Home);

        provider.LoginLivesInFiles.Should().BeTrue();
        provider.AuthFiles.Should().Contain(file => file.Required);
    }

    /// <summary>
    /// 이 테스트가 빨개졌다면 둘 중 하나다: `agy` 가 토큰을 파일에 남기기 시작했거나(그러면 반가운 일이니
    /// AuthFiles 와 이 값을 함께 고친다), 누군가 사실 확인 없이 값을 바꿨거나.
    /// </summary>
    [Fact]
    public void Antigravity_does_not_and_says_so()
    {
        IProvider provider = new AntigravityProvider(Home);

        provider.LoginLivesInFiles.Should().BeFalse(
            because: "로그인은 Windows 자격 증명 관리자에 있다. 파일 복사로는 계정이 바뀌지 않는다");

        provider.AuthFiles.Should().NotContain(file => file.Required,
            because: "필수 로그인 파일이 있다고 적으면 보관이 되는 것처럼 보인다");
    }

    /// <summary>보관을 내주는 도구는 되돌릴 파일도 반드시 있어야 한다. 둘이 어긋나면 조용히 빈 프로필이 생긴다.</summary>
    [Theory]
    [MemberData(nameof(AllProviders))]
    public void Saving_is_offered_only_when_there_is_a_required_file(IProvider provider)
    {
        if (provider.LoginLivesInFiles)
        {
            provider.AuthFiles.Should().Contain(file => file.Required);
        }
    }

    public static IEnumerable<object[]> AllProviders() =>
    [
        [new ClaudeProvider(Home, new FakeProcessProbe())],
        [new CodexProvider(Home)],
        [new AntigravityProvider(Home)],
    ];

    /// <summary>
    /// 로그인 단추는 로그인돼 있어도 보인다 — 다른 계정으로 바꾸는 길이기 때문이다.
    /// 그러니 로그인 인자는 <b>이미 로그인된 채로도</b> 새 로그인 흐름을 여는 것이어야 한다.
    /// 빈 인자로 도구를 그냥 띄우면 로그인된 Claude 는 물음 없이 대화로 들어간다 (2026-09-11 사람의 지적).
    /// </summary>
    [Fact]
    public void Claude_and_Codex_open_a_fresh_login_flow_even_when_logged_in()
    {
        IProvider claude = new ClaudeProvider(Home, new FakeProcessProbe());
        IProvider codex = new CodexProvider(Home);

        claude.LoginArguments.Should().Be("auth login");
        codex.LoginArguments.Should().Be("login");
    }
}
