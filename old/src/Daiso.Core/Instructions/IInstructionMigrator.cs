namespace Daiso.Core;

/// <summary>
/// 도구 사이 지시문 마이그레이션. (REQUIREMENTS §7, ARCHITECTURE §5.6)
/// 파일 읽기·쓰기는 하지 않는다. 내용 문자열만 다룬다.
/// </summary>
public interface IInstructionMigrator
{
    /// <summary>
    /// 도구별 본문(마커 블록 제외)을 모으고 <paramref name="left"/>·<paramref name="right"/> 둘을 비교한다.
    /// diff 는 본디 둘 사이의 것이라 비교 짝은 밖에서 고른다. 옮길 수 있는 방향은 결과가 전부 알려 준다.
    /// </summary>
    InstructionMigrationPlan Plan(
        IReadOnlyList<InstructionToolInfo> tools,
        IReadOnlyDictionary<ToolKind, InstructionSource> sources,
        ToolKind left,
        ToolKind right);

    /// <summary>
    /// 고른 방향으로 대상 파일 내용을 만든다. 원본이 없으면 <see cref="InvalidOperationException"/>.
    /// 대상이 <c>@경로</c> import 를 못 읽는 도구면 내용을 그 자리에 펼치고 경고를 남긴다.
    /// </summary>
    MigrationResult Render(InstructionMigrationPlan plan, MigrationDirection direction);
}
