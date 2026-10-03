using Daiso.Core;

namespace Daiso.Providers.Antigravity;

/// <summary>
/// <c>agy models</c> 의 출력 (1.1.28, 2026-09-10 확인). 세 도구 중 유일하게 <b>공식 명령</b>으로 목록을 준다.
/// <code>
/// Fetching available models...
/// gemini-3.8-flash-high	Gemini 3.8 Flash (High)
/// claude-sonnet-4-6	Claude Sonnet 4.6 (Thinking)
/// </code>
/// <para>
/// 한 줄에 하나, <c>id</c> 와 이름 사이는 탭이다. 탭이 없는 줄은 안내 문장이라 건너뛴다.
/// 추론 강도가 id 에 붙어 있다(<c>-high</c>). 서버에 묻는 명령이라 몇 초 걸리고 인터넷이 필요하다.
/// </para>
/// </summary>
internal static class AntigravityModelList
{
    public const string Arguments = "models";

    public static IReadOnlyList<ModelOption> Parse(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return [];
        }

        var models = new List<ModelOption>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var tab = line.IndexOf('\t', StringComparison.Ordinal);

            if (tab <= 0)
            {
                continue;
            }

            var id = line[..tab].Trim();
            var name = line[(tab + 1)..].Trim();

            if (id.Length == 0 || id.Contains(' ', StringComparison.Ordinal))
            {
                continue;
            }

            models.Add(new ModelOption(id, name.Length > 0 ? name : id));
        }

        return models;
    }
}
