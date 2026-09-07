namespace Daiso.App.Services;

/// <summary>`%LOCALAPPDATA%\d-AI-so\settings.json`에 담기는 설정. (ARCHITECTURE §6)</summary>
public sealed class AppSettings
{
    /// <summary>최근에 터미널을 연 폴더. 최신 것이 앞.</summary>
    public List<string> RecentFolders { get; set; } = [];

    /// <summary>최근에 연 .daiso 파일. 최신 것이 앞.</summary>
    public List<string> RecentRuleFiles { get; set; } = [];

    /// <summary>인덱스 DB 위치를 바꿀 때만 채운다. null이면 기본 위치.</summary>
    public string? IndexDatabasePath { get; set; }

    /// <summary>
    /// 세션을 찾는 기준 폴더. null이면 %USERPROFILE%.
    /// 삭제 기능을 더미 폴더로 검증할 때 여기를 바꾼다.
    /// </summary>
    public string? SessionHomeOverride { get; set; }

    /// <summary>테마. "System" | "Light" | "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>정리 규칙 기본값: 이 일수보다 오래된 세션.</summary>
    public int CleanupOlderThanDays { get; set; } = 30;

    /// <summary>정리 규칙 기본값: 이 크기(MB)를 넘는 세션.</summary>
    public int CleanupLargerThanMegabytes { get; set; } = 20;

    /// <summary>모델별 100만 토큰당 추정 단가(USD). 비용은 "추정" 표시와 함께만 쓴다.</summary>
    public Dictionary<string, ModelPrice> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>창 크기 기억.</summary>
    public int WindowWidth { get; set; } = 1280;

    /// <summary>창 높이 기억.</summary>
    public int WindowHeight { get; set; } = 820;

    /// <summary>최근 목록에 담는 최대 개수.</summary>
    public const int RecentLimit = 10;

    /// <summary>목록 맨 앞에 넣고 중복을 없애며 길이를 맞춘다.</summary>
    public static void Remember(List<string> list, string value)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        list.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, value);

        while (list.Count > RecentLimit)
        {
            list.RemoveAt(list.Count - 1);
        }
    }
}

/// <summary>모델 하나의 추정 단가. 100만 토큰당 USD.</summary>
public sealed class ModelPrice
{
    public decimal InputPerMillion { get; set; }

    public decimal OutputPerMillion { get; set; }

    public decimal CacheWritePerMillion { get; set; }

    public decimal CacheReadPerMillion { get; set; }
}
