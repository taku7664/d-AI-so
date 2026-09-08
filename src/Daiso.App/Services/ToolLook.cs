using Daiso.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Daiso.App.Services;

/// <summary>도구별 표시 규칙 한 곳. 이름·한 글자·색. 도구가 늘면 여기만 늘린다. (ARCHITECTURE §6.2)</summary>
public static class ToolLook
{
    /// <summary>화면에 도구를 늘어놓는 순서. 탭·카드·필터가 모두 이 순서를 따른다.</summary>
    public static readonly IReadOnlyList<ToolKind> DisplayOrder = [ToolKind.Codex, ToolKind.Claude, ToolKind.Gemini];

    /// <summary>표시 순서상 위치. 모르는 도구는 맨 뒤.</summary>
    public static int Rank(ToolKind kind)
    {
        for (var i = 0; i < DisplayOrder.Count; i++)
        {
            if (DisplayOrder[i] == kind)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>표시 순서대로 정렬한다.</summary>
    public static IEnumerable<T> InDisplayOrder<T>(IEnumerable<T> items, Func<T, ToolKind> kindOf) =>
        items.OrderBy(item => Rank(kindOf(item)));

    /// <summary>카드 제목에 쓰는 정식 이름.</summary>
    public static string Title(ToolKind kind) => kind switch
    {
        ToolKind.Claude => "Claude Code",
        ToolKind.Codex => "Codex CLI",
        ToolKind.Gemini => "Gemini CLI",
        _ => kind.ToString(),
    };

    /// <summary>버튼·미리보기에 쓰는 짧은 이름.</summary>
    public static string Short(ToolKind kind) => kind switch
    {
        ToolKind.Claude => "Claude",
        ToolKind.Codex => "Codex",
        ToolKind.Gemini => "Gemini",
        _ => kind.ToString(),
    };

    /// <summary>동그란 배지 안 한 글자.</summary>
    public static string Initial(ToolKind kind) => kind switch
    {
        ToolKind.Claude => "C",
        ToolKind.Codex => "X",
        ToolKind.Gemini => "G",
        _ => "?",
    };

    /// <summary>배지 색. Claude 보라, Codex 회색, Gemini 파랑.</summary>
    public static Color Color(ToolKind kind) => kind switch
    {
        ToolKind.Claude => Windows.UI.Color.FromArgb(255, 122, 90, 248),
        ToolKind.Codex => Windows.UI.Color.FromArgb(255, 96, 104, 120),
        ToolKind.Gemini => Windows.UI.Color.FromArgb(255, 52, 120, 246),
        _ => Colors.Gray,
    };

    public static Brush Brush(ToolKind kind) => new SolidColorBrush(Color(kind));
}
