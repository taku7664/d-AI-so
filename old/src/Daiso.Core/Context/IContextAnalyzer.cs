namespace Daiso.Core;

/// <summary>컨텍스트 파일 내용을 분석한다. 파일 읽기는 호출자 책임. (ARCHITECTURE §3.1)</summary>
public interface IContextAnalyzer
{
    ContextReport Analyze(ToolKind tool, IReadOnlyList<ContextFile> files);
}
