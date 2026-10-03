using System.Globalization;
using Daiso.App.Strings;
using Daiso.Core;

namespace Daiso.App.Services;

/// <summary>
/// 화면에 숫자·경로·시각을 사람이 읽기 쉽게 만든다. (ARCHITECTURE §6.2)
/// 원래 값은 ToolTip으로 남기므로 여기서는 과감히 줄인다.
/// </summary>
public static class Formats
{
    private const double Thousand = 1_000d;
    private const double Million = 1_000_000d;
    private const double Billion = 1_000_000_000d;

    /// <summary>토큰 수처럼 큰 수를 줄여 쓴다. 1,234 → 1.2K, 729,015,129 → 729.0M.</summary>
    public static string Tokens(long value)
    {
        var abs = Math.Abs((double)value);

        return abs switch
        {
            >= Billion => UiStrings.Format("Number_Billion", value / Billion),
            >= Million => UiStrings.Format("Number_Million", value / Million),
            >= Thousand => UiStrings.Format("Number_Thousand", value / Thousand),
            _ => value.ToString("N0", CultureInfo.CurrentCulture),
        };
    }

    /// <summary>바이트 수를 사람이 읽는 단위로. 화면 어디서나 같은 규칙을 쓴다.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):N1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):N2} GB",
    };

    /// <summary>정확한 값. ToolTip에 쓴다.</summary>
    public static string Exact(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>재로그인까지 남은 기간. 날짜와 남은 일수를 함께 보여준다.</summary>
    public static string Expiry(DateTimeOffset? at)
    {
        if (at is not { } deadline)
        {
            return UiStrings.Get("Auth_NoExpiry");
        }

        var local = deadline.ToLocalTime();
        var days = (int)Math.Floor((local - DateTimeOffset.Now).TotalDays);

        return days switch
        {
            < 0 => UiStrings.Get("Auth_Expired"),
            0 => UiStrings.Format("Auth_ExpiresToday", local),
            _ => UiStrings.Format("Auth_ExpiresIn", local, days),
        };
    }

    /// <summary>
    /// 경로 목록을 목록에 보여줄 이름으로 바꾼다. 규칙은 <see cref="PathLabels"/> 가 들고 있고
    /// 여기서는 "모를 때 쓸 이름"만 문구에서 가져와 넘긴다 (규칙에는 테스트가 붙어 있다).
    /// </summary>
    public static IReadOnlyList<string> Labels(IReadOnlyList<string> paths) =>
        PathLabels.For(paths, UiStrings.Get("Common_Unknown"));

    /// <summary>경로에서 사람이 부르는 이름(마지막 폴더)만 뽑는다.</summary>
    public static string FolderName(string? path) =>
        PathLabels.FolderName(path, UiStrings.Get("Common_Unknown"));
}
