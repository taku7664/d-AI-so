using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Claude;

/// <summary>
/// Claude Code 가 받는 모델. <b>목록을 내놓는 명령이 없다</b> (2.1.266 <c>claude --help</c>, 2026-09-10 확인). 그래서 두 곳을 합친다.
/// <list type="bullet">
/// <item>별칭 — <c>--help</c> 가 드는 <c>fable</c> · <c>opus</c> · <c>sonnet</c>. 각 계열의 최신 모델을 가리키므로 새 버전이 나와도 틀리지 않는다.</item>
/// <item>계정에 따라 더 붙는 모델 — <c>~/.claude.json</c> 의 <c>additionalModelOptionsCache[]</c> (<c>value</c> · <c>label</c> · <c>description</c>).
/// CLI 가 <c>/model</c> 고르기 창에 쓰려고 받아 둔 것이다. <b>문서에 없는 내부 항목</b>이라 없거나 모양이 바뀌면 별칭만 준다.</item>
/// </list>
/// 이 파일에는 계정 정보도 있지만 여기서는 그 배열만 본다. 토큰은 이 파일에 없다.
/// </summary>
internal static class ClaudeModelList
{
    private static readonly ModelOption[] Aliases =
    [
        new("fable", "Fable"),
        new("opus", "Opus"),
        new("sonnet", "Sonnet"),
    ];

    public static IReadOnlyList<ModelOption> From(string? claudeJson)
    {
        var models = new List<ModelOption>(Aliases);

        if (string.IsNullOrWhiteSpace(claudeJson))
        {
            return models;
        }

        using var document = JsonHelpers.TryParseLine(claudeJson);

        if (document is null)
        {
            return models;
        }

        foreach (var item in document.RootElement.Prop("additionalModelOptionsCache").Items())
        {
            if (item.Prop("value").Text() is not { Length: > 0 } id
                || models.Exists(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var label = item.Prop("label").Text();
            models.Add(new ModelOption(id, string.IsNullOrWhiteSpace(label) ? id : label, item.Prop("description").Text()));
        }

        return models;
    }
}
