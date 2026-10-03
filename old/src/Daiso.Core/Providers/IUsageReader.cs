namespace Daiso.Core;

/// <summary>
/// 세션 파일에서 토큰 사용량을 읽는다. <see cref="IProvider"/>가 함께 구현한다.
/// </summary>
/// <remarks>
/// 도구마다 사용량 기록 방식이 달라 (ARCHITECTURE §5.1 usage_daily) 집계 방법도 달라진다.
/// Claude는 메시지 timestamp 날짜별 합산, Codex는 누적값이라 세션 단위 덮어쓰기다.
/// <see cref="IProvider.ReadSessionInfoAsync"/>는 세션 전체 합계만 주므로 날짜별 집계를 위해 따로 둔다.
/// </remarks>
public interface IUsageReader
{
    /// <summary>true면 날짜별로 더한다. false면 세션 단위로 덮어쓴다.</summary>
    bool UsageIsAdditive { get; }

    /// <summary>
    /// <paramref name="fromByteOffset"/>부터 읽은 사용량을 날짜별로 돌려준다.
    /// 누적 기록 도구는 <paramref name="sessionDate"/>에 한 건으로 몰아 준다.
    /// </summary>
    IAsyncEnumerable<UsageDay> ReadUsageAsync(
        string filePath,
        long fromByteOffset,
        DateOnly sessionDate,
        CancellationToken ct);
}
