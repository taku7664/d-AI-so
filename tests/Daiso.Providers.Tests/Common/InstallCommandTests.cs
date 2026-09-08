using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;
using Daiso.Providers.Gemini;

namespace Daiso.Providers.Tests.Common;

/// <summary>REQUIREMENTS §4 — 실행 파일이 없으면 터미널이 설치 명령을 돌린다. 세 도구 모두 npm 전역 설치다.</summary>
public sealed class InstallCommandTests
{
    public static TheoryData<IProvider> Providers =>
    [
        new ClaudeProvider(new ProviderHome(Path.GetTempPath()), new FakeProcessProbe()),
        new CodexProvider(new ProviderHome(Path.GetTempPath())),
        new GeminiProvider(new ProviderHome(Path.GetTempPath())),
    ];

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_provider_installs_with_npm_globally(IProvider provider)
    {
        provider.InstallCommand.Should().StartWith("npm install -g ");
        provider.InstallCommand.Split(' ').Should().HaveCount(4, because: "실행 파일 하나와 인자 셋이어야 화면이 그대로 쪼갤 수 있다");
    }
}
