using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daiso.Providers.Manifest;

/// <summary>
/// 어댑터와 주고받는 줄의 모양 (docs/PLUGIN_PLAN.md §6).
/// <para>
/// <b>필드를 새로 상상하지 않는다.</b> <c>SessionInfo</c> · <c>SessionMessage</c> 를 그대로 옮긴 것이고,
/// 지금 화면이 쓰는 것만 넘긴다.
/// </para>
/// </summary>
public static class AdapterProtocol
{
    /// <summary>이 앱이 말할 줄 아는 판. 어댑터가 다른 판이면 그 도구만 오류로 내린다.</summary>
    public const int Version = 1;

    /// <summary>
    /// 줄 단위 JSON 이다. 들여쓰기를 넣지 않는다 — 한 줄이 한 메시지라는 것이 프로토콜의 전부다.
    /// 한글을 이스케이프하지 않아 어댑터를 손으로 만들 때 눈으로 읽힌다.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };
}

/// <summary>앱이 어댑터에 보내는 줄.</summary>
/// <param name="V">프로토콜 판.</param>
/// <param name="Op"><c>hello</c> · <c>sessions</c> · <c>session</c> · <c>messages</c>.</param>
/// <param name="Root">세션 폴더. <c>sessions</c> 에만.</param>
/// <param name="FilePath">읽을 파일. <c>session</c> · <c>messages</c> 에만.</param>
/// <param name="FromByteOffset">이어 읽기 시작 위치. <c>messages</c> 에만.</param>
public sealed record AdapterRequest(
    int V,
    string Op,
    string? Root = null,
    string? FilePath = null,
    long? FromByteOffset = null);

/// <summary>어댑터가 돌려주는 줄. 한 요청에 여러 줄이 오고 <see cref="Done"/> 으로 끝난다.</summary>
/// <param name="V">프로토콜 판.</param>
/// <param name="Ok"><c>hello</c> 의 답.</param>
/// <param name="Name">어댑터 이름. 설정 화면이 보여 준다.</param>
/// <param name="AppendOnly">뒤에만 붙는 로그인가. 매니페스트 값을 덮어쓴다.</param>
/// <param name="Session">세션 한 줄.</param>
/// <param name="Message">메시지 한 줄.</param>
/// <param name="ReadTo">어디까지 읽었나. 다음 이어 읽기가 여기서 시작한다.</param>
/// <param name="Error">어댑터가 알린 잘못. 오면 그 요청은 실패다.</param>
/// <param name="Done">이 요청의 마지막 줄인가.</param>
public sealed record AdapterResponse(
    int V = 0,
    bool? Ok = null,
    string? Name = null,
    bool? AppendOnly = null,
    AdapterSession? Session = null,
    AdapterMessage? Message = null,
    long? ReadTo = null,
    string? Error = null,
    bool Done = false);

/// <summary>세션 하나. <c>SessionInfo</c> 에서 화면이 쓰는 것만.</summary>
public sealed record AdapterSession(
    string Id,
    string FilePath,
    string? ProjectPath = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? ModifiedAt = null,
    long SizeBytes = 0,
    int UserCount = 0,
    int AssistantCount = 0,
    string? FirstPrompt = null,
    AdapterUsage? Usage = null,
    string? ToolVersion = null,
    bool IsArchived = false,
    bool IsActive = false);

/// <summary>메시지 하나.</summary>
/// <param name="Role"><c>user</c> · <c>assistant</c> · <c>tool</c> · <c>system</c>. 모르는 값은 <c>system</c> 으로 본다.</param>
public sealed record AdapterMessage(
    DateTimeOffset At,
    string Role,
    string Text,
    bool IsSidechain = false);

/// <summary>토큰 사용량.</summary>
public sealed record AdapterUsage(
    long Input = 0,
    long Output = 0,
    long CacheCreate = 0,
    long CacheRead = 0,
    string? Model = null);
