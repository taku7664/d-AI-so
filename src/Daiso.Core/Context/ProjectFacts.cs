namespace Daiso.Core;

/// <summary>
/// Context Doctor가 함께 보여주는 프로젝트 부가 정보. (REQUIREMENTS §5.3)
/// </summary>
/// <param name="ProjectPath">정규화된 프로젝트 경로.</param>
/// <param name="Settings">`.claude/settings.json` 계열 요약 줄.</param>
/// <param name="SkillCount">`.claude/skills` 항목 수.</param>
/// <param name="AgentCount">`.claude/agents` 항목 수.</param>
/// <param name="CommandCount">`.claude/commands` 항목 수.</param>
/// <param name="HasMcpJson">`.mcp.json` 존재 여부.</param>
/// <param name="GitBranch">현재 브랜치. git 폴더가 없으면 null.</param>
/// <param name="GitCommit">브랜치가 가리키는 커밋 SHA 앞 7자.</param>
public sealed record ProjectFacts(
    string ProjectPath,
    IReadOnlyList<string> Settings,
    int SkillCount,
    int AgentCount,
    int CommandCount,
    bool HasMcpJson,
    string? GitBranch,
    string? GitCommit)
{
    /// <summary>아무 정보도 못 읽었을 때.</summary>
    public static ProjectFacts Empty(string projectPath) =>
        new(projectPath, [], 0, 0, 0, false, null, null);
}

/// <summary>프로젝트 폴더에서 부가 정보를 읽는다. 구현은 Daiso.Infrastructure.</summary>
public interface IProjectFactsReader
{
    Task<ProjectFacts> ReadAsync(string projectDir, CancellationToken ct);
}
