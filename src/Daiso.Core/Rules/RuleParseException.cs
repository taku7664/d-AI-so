namespace Daiso.Core;

/// <summary>.daiso 파싱·검증 실패. 오류 위치(1부터 시작하는 줄·칸)를 함께 전달한다.</summary>
public sealed class RuleParseException : Exception
{
    public RuleParseException(int line, int column, string message)
        : base($"{message} (line {line}, column {column})")
    {
        Line = line;
        Column = column;
        Detail = message;
    }

    /// <summary>오류가 발생한 줄 번호 (1부터).</summary>
    public int Line { get; }

    /// <summary>오류가 발생한 칸 번호 (1부터).</summary>
    public int Column { get; }

    /// <summary>위치 정보를 뺀 오류 설명.</summary>
    public string Detail { get; }
}
