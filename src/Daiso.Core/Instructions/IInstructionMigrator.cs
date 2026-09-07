namespace Daiso.Core;

/// <summary>
/// CLAUDE.md ↔ AGENTS.md 마이그레이션. (REQUIREMENTS §7, ARCHITECTURE §5.6)
/// 파일 읽기·쓰기는 하지 않는다. 내용 문자열만 다룬다.
/// </summary>
public interface IInstructionMigrator
{
    /// <summary>양쪽 본문(마커 블록 제외)을 비교하고 권장 방향을 정한다.</summary>
    InstructionMigrationPlan Plan(InstructionSource claude, InstructionSource codex);

    /// <summary>
    /// 고른 방향으로 대상 파일 내용을 만든다. 원본이 없으면 <see cref="InvalidOperationException"/>.
    /// 대상이 Codex면 Claude 전용 <c>@경로</c> import를 인라인 전개하고 경고를 남긴다.
    /// </summary>
    MigrationResult Render(InstructionMigrationPlan plan, MigrationDirection direction);
}
