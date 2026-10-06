using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daiso.Host.Tests;

/// <summary>
/// 웹이 TS 타입을 만드는 OpenAPI 문서(<c>frontend/web/src/api/openapi.json</c>)가 서버와 같은지 본다 (docs/ARCHITECTURE.md "화면과 서버가 주고받는 법" 5).
/// 서버 경로나 타입을 바꾸면 이 시험이 빨개진다. <c>DAISO_UPDATE_OPENAPI=1</c> 로 돌리면 파일을 새로 쓴다.
/// 그 뒤 <c>frontend/</c> 에서 <c>npm run gen:api -w @daiso/web</c> 로 TS 타입을 다시 만든다.
/// </summary>
public sealed class OpenApiSnapshotTests
{
    private const string UpdateVariable = "DAISO_UPDATE_OPENAPI";

    [Fact]
    public async Task The_committed_openapi_document_matches_the_server()
    {
        await using var host = await RunningHost.StartAsync(probe: false);
        using var client = host.Client();
        using var request = host.Authed(HttpMethod.Get, "/openapi/v1.json");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var actual = Normalize(await response.Content.ReadAsStringAsync());
        var path = SnapshotPath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, actual);
            return;
        }

        File.Exists(path).Should().BeTrue(because: $"{path} 가 없다. {UpdateVariable}=1 로 한 번 돌려 만든다");
        Normalize(await File.ReadAllTextAsync(path)).Should().Be(actual, because: $"서버가 바뀌었다. {UpdateVariable}=1 로 다시 쓰고 TS 타입을 다시 만든다");
    }

    /// <summary>
    /// 서버 주소는 띄울 때마다 바뀐다. 문서에서 빼고 줄 바꿈을 맞춘다.
    /// </summary>
    private static string Normalize(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("servers");

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>저장소 루트를 위로 찾아 올라간다. 시험은 bin 아래에서 돈다.</summary>
    private static string SnapshotPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "frontend")) && Directory.Exists(Path.Combine(dir.FullName, "backend")))
            {
                return Path.Combine(dir.FullName, "frontend", "web", "src", "api", "openapi.json");
            }
        }

        throw new InvalidOperationException("저장소 루트를 찾지 못했다");
    }
}
