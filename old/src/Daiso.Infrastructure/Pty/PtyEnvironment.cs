using System.Collections;
using System.Text;

namespace Daiso.Infrastructure.Pty;

/// <summary>
/// 방 프로세스에 넘길 환경 변수. 우리 프로세스의 환경을 물려주되, 다른 Claude Code 세션 안에서 앱이 떴을 때
/// 새는 표식(<c>CLAUDECODE</c>, <c>CLAUDE_CODE_*</c> …)은 걷어낸다. 그대로 두면 안의 CLI가 자기를 하위 세션으로 알고
/// 온보딩·프록시 주소·다른 설정을 물려받는다. 사용자가 평소 쓰는 변수는 건드리지 않는다. (ARCHITECTURE §5.3)
/// </summary>
public static class PtyEnvironment
{
    /// <summary>중첩 표식. 이 접두사·이름은 상위 Claude Code가 자식에게만 심는 값이다.</summary>
    private static readonly string[] NestedPrefixes = ["CLAUDE_CODE_", "CLAUDE_PREVIEW_"];

    private static readonly string[] NestedNames = ["CLAUDECODE", "CLAUDE_PID", "CLAUDE_EFFORT", "CLAUDE_AGENT_SDK_VERSION", "AI_AGENT", "BAGGAGE"];

    /// <summary>중첩 세션일 때만 함께 걷어내는 값. 상위 세션이 프록시로 돌려 놓은 주소라 그대로 두면 안의 CLI가 그 프록시로 간다.</summary>
    private static readonly string[] NestedOnlyNames = ["ANTHROPIC_BASE_URL", "ANTHROPIC_AUTH_TOKEN"];

    /// <summary>현재 프로세스 환경에서 시작해 걷어낸 사전.</summary>
    public static Dictionary<string, string> Sanitized()
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                source[key] = value;
            }
        }

        return Sanitize(source);
    }

    /// <summary>순수 함수. 테스트가 쓴다.</summary>
    public static Dictionary<string, string> Sanitize(IReadOnlyDictionary<string, string> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var nested = source.Keys.Any(key => string.Equals(key, "CLAUDECODE", StringComparison.OrdinalIgnoreCase));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in source)
        {
            if (NestedNames.Contains(key, StringComparer.OrdinalIgnoreCase)
                || NestedPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                || (nested && NestedOnlyNames.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            result[key] = value;
        }

        // 안의 프로그램이 색·유니코드를 마음껏 쓰게. xterm이 다 그린다
        result["TERM"] = "xterm-256color";
        result["COLORTERM"] = "truecolor";

        return result;
    }

    /// <summary>CreateProcessW의 유니코드 환경 블록: "K=V\0K=V\0\0". 이름순 정렬은 Windows 관례.</summary>
    public static string ToBlock(IReadOnlyDictionary<string, string> variables)
    {
        var builder = new StringBuilder();

        foreach (var (key, value) in variables.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(key).Append('=').Append(value).Append('\0');
        }

        builder.Append('\0');
        return builder.ToString();
    }
}
