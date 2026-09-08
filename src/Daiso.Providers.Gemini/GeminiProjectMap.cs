using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Daiso.Providers.Common;

namespace Daiso.Providers.Gemini;

/// <summary>
/// Gemini는 세션을 프로젝트 경로 대신 이름 또는 SHA-256 해시 폴더에 둔다. `~/.gemini/projects.json`이
/// <c>{"projects": {"소문자 경로": "이름"}}</c> 로 둘을 이어 준다. 여기서 폴더 이름·해시 → 경로를 되짚는다.
/// 모르는 폴더는 null이다. 프로젝트를 모르는 세션도 목록에는 나온다.
/// </summary>
internal sealed class GeminiProjectMap
{
    private readonly Dictionary<string, string> _byKey = new(StringComparer.OrdinalIgnoreCase);

    private GeminiProjectMap()
    {
    }

    /// <summary>projects.json 내용으로 만든다. null이나 깨진 내용이면 빈 지도.</summary>
    internal static GeminiProjectMap From(string? projectsJson)
    {
        var map = new GeminiProjectMap();

        if (projectsJson is null)
        {
            return map;
        }

        using var document = JsonHelpers.TryParseLine(projectsJson);
        if (document?.RootElement.Prop("projects") is not { ValueKind: JsonValueKind.Object } projects)
        {
            return map;
        }

        foreach (var entry in projects.EnumerateObject())
        {
            var path = entry.Name;

            if (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { Length: > 0 } name)
            {
                map._byKey[name] = path;
            }

            map._byKey[Sha256Hex(path)] = path;
        }

        return map;
    }

    /// <summary>폴더 이름이나 projectHash로 프로젝트 경로를 찾는다. 모르면 null.</summary>
    internal string? Resolve(string? directoryNameOrHash) =>
        directoryNameOrHash is { Length: > 0 } key && _byKey.TryGetValue(key, out var path) ? path : null;

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
