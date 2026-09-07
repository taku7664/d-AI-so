using System.Text;

namespace Daiso.Core.Tests.Rules;

/// <summary>테스트가 공유하는 .daiso 원본.</summary>
internal static class SampleRules
{
    /// <summary>저장소의 정본 샘플 (samples/PROJECT_RULES.daiso).</summary>
    internal static string Canonical { get; } = ReadCanonical();

    private static string ReadCanonical()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "PROJECT_RULES.daiso");
        return System.IO.File.ReadAllText(path, Encoding.UTF8);
    }
}
