using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Providers.Codex;

/// <summary>
/// Codex CLI 가 서버에서 받아 두는 모델 목록 <c>~/.codex/models_cache.json</c> (0.153.4, 2026-09-10 확인).
/// <code>
/// { fetched_at, etag, client_version,
///   models: [{ slug, display_name, description, visibility: "list" | "hide", priority, supported_reasoning_levels[] … }] }
/// </code>
/// <para>
/// <c>visibility: "hide"</c> 는 CLI 의 <c>/model</c> 창에도 안 나오는 내부용이라 뺀다. 순서는 파일 순서다(CLI 가 매긴 순서).
/// 파일은 CLI 가 한 번이라도 돌아야 생긴다. <b>문서에 없는 내부 파일</b>이라 없거나 모양이 바뀌면 빈 목록이다.
/// </para>
/// </summary>
internal static class CodexModelCache
{
    public const string FileName = "models_cache.json";

    public static IReadOnlyList<ModelOption> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonHelpers.TryParseLine(json);

        if (document is null)
        {
            return [];
        }

        var models = new List<ModelOption>();

        foreach (var item in document.RootElement.Prop("models").Items())
        {
            if (item.Prop("slug").Text() is not { Length: > 0 } id
                || string.Equals(item.Prop("visibility").Text(), "hide", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = item.Prop("display_name").Text();
            models.Add(new ModelOption(id, string.IsNullOrWhiteSpace(name) ? id : name, item.Prop("description").Text()));
        }

        return models;
    }
}
