using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;

namespace Daiso.Providers.Tests.Common;

/// <summary>
/// 요약 화면의 도구 카드는 <b>줄 뜻이 고정</b>이다: 이름 → 이메일 → 요금제 → 만료 → 설치.
/// <para>
/// 도구마다 이름 줄의 뜻이 달라지면 카드 셋을 나란히 놓고 같은 자리를 비교할 수 없다.
/// 예전에는 Claude 가 이름 줄에 구독을 붙이고(`… · max`) Codex 는 인증 방식(`chatgpt`)을 넣어
/// 세 카드가 서로 다른 것을 같은 자리에 그렸다 (2026-09-11 사람의 지적).
/// </para>
/// </summary>
public sealed class AccountShapeTests
{
    public static TheoryData<string, AuthStatus> Statuses() => new()
    {
        { "claude", ClaudeAuthReader.Read(Fixtures.ReadClaude("credentials.json"), Fixtures.ReadClaude("claude.json"), Fixtures.Now) },
        { "codex", CodexAuthReader.Read(Fixtures.ReadCodex("auth.json"), Fixtures.Now) },
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void The_account_label_carries_no_plan(string tool, AuthStatus status)
    {
        if (status.Plan is not { Length: > 0 } plan || status.AccountLabel is not { } label)
        {
            return;
        }

        label.Should().NotContainEquivalentOf(plan,
            because: $"{tool} 의 이름 줄에 요금제가 섞였다. 요금제는 제 줄이 있다");
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public void An_email_is_an_email(string tool, AuthStatus status)
    {
        if (status.Email is not { } email)
        {
            return;
        }

        email.Should().Contain("@", because: $"{tool} 의 이메일 자리에 이메일이 아닌 것이 들어왔다");
    }
}
