using System.Text;

namespace Daiso.Host.Tabs.Terminal;

/// <summary>
/// 방에 들어간 키 입력으로 지금 입력 줄을 대강 따라간다. Enter 를 치면 그 줄을 돌려준다.
/// 화살표·붙여넣기 표시 같은 제어 순서는 건너뛰고, 지우기(Backspace)·줄 지우기(Ctrl+U)만 반영한다.
/// CLI 화면의 실제 입력 줄과 꼭 같지는 않다 — "질문을 보냈나, 명령(/…)을 쳤나"를 가를 만큼만 쓴다.
/// </summary>
internal sealed class InputLine
{
    private const char Escape = (char)0x1b;

    private readonly StringBuilder _line = new();
    private int _escape; // 0: 보통, 1: ESC 를 봤다, 2: CSI(ESC [) 안

    public List<string> Feed(string text)
    {
        var done = new List<string>();

        foreach (var ch in text)
        {
            if (_escape == 1)
            {
                _escape = ch == '[' ? 2 : 0;
                continue;
            }

            if (_escape == 2)
            {
                // CSI 는 0x40~0x7E 글자 하나로 끝난다
                if (ch >= '@' && ch <= '~')
                {
                    _escape = 0;
                }

                continue;
            }

            switch (ch)
            {
                case Escape:
                    _escape = 1;
                    break;
                case '\r':
                    done.Add(_line.ToString());
                    _line.Clear();
                    break;
                case '\b' or (char)0x7f:
                    if (_line.Length > 0)
                    {
                        _line.Length--;
                    }

                    break;
                case (char)0x15:
                    _line.Clear();
                    break;
                default:
                    if (!char.IsControl(ch) || ch == '\n')
                    {
                        _line.Append(ch);
                    }

                    break;
            }
        }

        return done;
    }
}
