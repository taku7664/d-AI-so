using System.Text;

namespace Daiso.Cli;

internal static class Program
{
    private const string Usage = """
        daiso — d-AI-so 검증용 CLI

        사용법:
          daiso auth
          daiso sessions [--tool claude|codex] [--include-archived]
          daiso search <query>
          daiso usage --days N
          daiso rules render <path>
          daiso rules roundtrip <path>
          daiso rules install <projectDir>
          daiso doctor <dir> [--tool claude|codex]
          daiso export <sessionId> <out.md>
        """;

    internal static Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            Console.WriteLine(Usage);
            return Task.FromResult(1);
        }

        Console.Error.WriteLine($"미구현 명령: {args[0]}");
        return Task.FromResult(2);
    }
}
