using System.Globalization;
using System.Xml.Linq;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 격자에 없는 줄·칸에 무언가를 놓지 않았는지 검사한다.
/// <para>
/// WinUI 는 <c>Grid.Row="2"</c> 인데 줄이 둘뿐이어도 <b>말없이 마지막 줄에 겹쳐 그린다</b>.
/// 빌드도 통과하고 예외도 없다 — 화면을 열어야 보이고, 그마저 "왜 겹쳐 보이지"로 읽힌다.
/// 2026-09-11 내 규칙 목록에 거르개를 끼우면서 <c>RowDefinition</c> 을 안 늘려
/// 목록과 `지금 규칙에 추가` 단추가 같은 줄에 겹쳤다. 그래서 여기서 잠근다.
/// </para>
/// </summary>
public sealed class GridPlacementTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string Root = FindRepositoryRoot();

    private static readonly string AppDirectory = Path.Combine(Root, "src", "Daiso.App");

    public static IEnumerable<object[]> XamlFiles() =>
        Directory.EnumerateFiles(AppDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => new object[] { Path.GetRelativePath(AppDirectory, path) });

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void Every_child_lands_on_a_row_and_column_that_exists(string relativePath)
    {
        var document = XDocument.Load(Path.Combine(AppDirectory, relativePath));

        var offenders = document.Descendants(Presentation + "Grid")
            .SelectMany(Offenders)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            because: "격자에 없는 줄·칸에 놓으면 WinUI 가 말없이 마지막 줄·칸에 겹쳐 그린다");
    }

    private static IEnumerable<string> Offenders(XElement grid)
    {
        var rows = Math.Max(1, Definitions(grid, "RowDefinitions").Count);
        var columns = Math.Max(1, Definitions(grid, "ColumnDefinitions").Count);

        foreach (var child in grid.Elements().Where(child => !child.Name.LocalName.Contains('.', StringComparison.Ordinal)))
        {
            if (Index(child, "Grid.Row") is { } row && row >= rows)
            {
                yield return $"{Where(child)} Grid.Row={row} (줄 {rows}개)";
            }

            if (Index(child, "Grid.Column") is { } column && column >= columns)
            {
                yield return $"{Where(child)} Grid.Column={column} (칸 {columns}개)";
            }
        }
    }

    private static List<XElement> Definitions(XElement grid, string name) =>
        [.. grid.Elements(Presentation + "Grid." + name).Elements()];

    private static int? Index(XElement element, string attribute) =>
        int.TryParse((string?)element.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>어긋난 곳을 사람이 찾을 수 있게 이름표를 붙인다.</summary>
    private static string Where(XElement element) =>
        (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")
        ?? (string?)element.Attribute("AutomationProperties.AutomationId")
        ?? element.Name.LocalName;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Daiso.sln 을 찾지 못했다");
    }
}
