namespace Daiso.Core;

/// <summary>
/// CLAUDE.md / AGENTS.md의 daiso 마커 블록을 삽입·갱신한다. (ARCHITECTURE §3.1, §7.3)
/// 파일 내용 문자열을 받아 새 내용을 돌려준다. 파일 접근은 하지 않는다.
/// </summary>
public interface IInstructionMarkerWriter
{
    /// <summary>
    /// <paramref name="existingContent"/>가 null이면 새 파일 내용을 만든다.
    /// 마커가 있으면 블록 내부만 <paramref name="instructionBody"/>로 교체하고,
    /// 없으면 끝에 빈 줄과 함께 블록을 덧붙인다. 마커 밖 내용은 개행 문자까지 그대로 둔다.
    /// </summary>
    string Apply(string? existingContent, string instructionBody);

    /// <summary>
    /// 마커 블록(마커 두 줄 포함)을 제거한 나머지를 돌려준다. 마커가 없으면 원본 그대로다.
    /// 블록이 사라져 생기는 빈 줄은 하나로 정리한다. diff·복사용이며 원본 파일에 되쓰기 위한 것이 아니다.
    /// </summary>
    string Strip(string content);
}
