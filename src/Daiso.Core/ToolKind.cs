namespace Daiso.Core;

/// <summary>지원하는 AI CLI 도구 종류.</summary>
public enum ToolKind
{
    Claude,
    Codex,

    /// <summary>
    /// Google Antigravity CLI(`agy`). 2026-06-18에 개인 계정용 Gemini CLI가 요청을 멈추고 이것으로 대체됐다.
    /// 이 값의 이름은 SQLite 인덱스에 문자열로 저장된다 — 바꾸면 `IndexFormatVersion`을 올려 인덱스를 다시 짓게 해야 한다.
    /// </summary>
    Antigravity,
}
