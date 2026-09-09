using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;
using Daiso.Providers.Antigravity;

namespace Daiso.Providers.Tests.Common;

/// <summary>
/// REQUIREMENTS §4 — 실행 파일이 없으면 터미널이 설치 명령을 돌린다.
/// Claude·Codex 는 npm 전역 설치이고, Antigravity 는 npm 패키지가 아니라 공식 설치 스크립트다(Go 단일 실행 파일).
/// </summary>
public sealed class InstallCommandTests
{
    public static TheoryData<IProvider> NpmProviders =>
    [
        new ClaudeProvider(new ProviderHome(Path.GetTempPath()), new FakeProcessProbe()),
        new CodexProvider(new ProviderHome(Path.GetTempPath())),
    ];

    [Theory]
    [MemberData(nameof(NpmProviders))]
    public void Npm_providers_install_globally(IProvider provider)
    {
        provider.InstallCommand.Should().StartWith("npm install -g ");
        provider.InstallCommand.Split(' ').Should().HaveCount(4, because: "실행 파일 하나와 인자 셋이어야 화면이 그대로 쪼갤 수 있다");
    }

    /// <summary>
    /// Antigravity 는 설치 방식이 다르다. npm 을 기대하는 코드가 다시 들어오면 여기서 걸린다.
    /// 명령은 공식 문서의 PowerShell 한 줄이고, 받는 곳은 antigravity.google 이어야 한다.
    /// </summary>
    [Fact]
    public void Antigravity_installs_with_the_official_script_not_npm()
    {
        var command = new AntigravityProvider(new ProviderHome(Path.GetTempPath())).InstallCommand;

        command.Should().NotContain("npm");
        command.Should().Contain("https://antigravity.google/cli/install.ps1");
    }

    /// <summary>실행 파일 이름이 옛 도구로 되돌아가지 않게 잠근다.</summary>
    [Fact]
    public void Antigravity_runs_agy()
    {
        new AntigravityProvider(new ProviderHome(Path.GetTempPath())).ExecutableName.Should().Be("agy");
    }
}
