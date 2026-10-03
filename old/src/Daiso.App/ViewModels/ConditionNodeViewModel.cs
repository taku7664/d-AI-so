using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daiso.Core;
using Daiso.App.Strings;

namespace Daiso.App.ViewModels;

/// <summary>조건 트리 노드의 종류.</summary>
public enum ConditionNodeKind
{
    Leaf,
    And,
    Or,
}

/// <summary>
/// 편집 가능한 조건 트리 노드. Core의 <see cref="Condition"/>은 불변이라 편집용 모델을 따로 둔다.
/// </summary>
public sealed partial class ConditionNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private ConditionNodeKind kind;

    [ObservableProperty]
    private string text = string.Empty;

    public ConditionNodeViewModel(ConditionNodeKind kind, string text = "")
    {
        this.kind = kind;
        this.text = text;
    }

    /// <summary>이 노드나 하위가 바뀌었다. 규칙이 미리보기를 다시 그리도록 위로 전달한다.</summary>
    public event EventHandler? Changed;

    /// <summary>하위 노드. Leaf는 비어 있다.</summary>
    public ObservableCollection<ConditionNodeViewModel> Children { get; } = [];

    /// <summary>부모 노드. 루트는 null.</summary>
    public ConditionNodeViewModel? Parent { get; private set; }

    /// <summary>트리에 보여줄 라벨.</summary>
    public string Label => Kind switch
    {
        ConditionNodeKind.Leaf => Text.Length == 0 ? UiStrings.Get("RuleMaker_EmptyCondition") : Text,
        ConditionNodeKind.And => "AND",
        _ => "OR",
    };

    /// <summary>리프인지. 텍스트 편집란 표시에 쓴다.</summary>
    public bool IsLeaf => Kind == ConditionNodeKind.Leaf;

    /// <summary>리프일 때만 텍스트 입력란을 보여준다.</summary>
    public Microsoft.UI.Xaml.Visibility LeafVisibility =>
        IsLeaf ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>하위 노드를 더 받을 수 있는지.</summary>
    public bool CanAcceptChild => Kind is ConditionNodeKind.And or ConditionNodeKind.Or;

    /// <summary>샘플 트리를 만들 때 쓰는 도우미.</summary>
    public static ConditionNodeViewModel Leaf(string text) => new(ConditionNodeKind.Leaf, text);

    /// <summary>Core 모델에서 편집용 트리를 만든다.</summary>
    public static ConditionNodeViewModel FromCondition(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        switch (condition)
        {
            case LeafCondition leaf:
                return Leaf(leaf.Text);

            case AndCondition and:
                return Operator(ConditionNodeKind.And, and.Items);

            case OrCondition or:
                return Operator(ConditionNodeKind.Or, or.Items);

            default:
                throw new ArgumentOutOfRangeException(nameof(condition), condition, "알 수 없는 조건");
        }
    }

    /// <summary>
    /// 편집용 트리를 Core 모델로 되돌린다.
    /// **비어 있는 것은 없는 것으로 본다.** 빈 리프·자식 없는 연산자는 null이고,
    /// 자식이 하나뿐인 연산자는 그 자식을 그대로 쓴다. (`A & B & ()` 같은 미리보기를 막는다)
    /// </summary>
    public Condition? ToCondition()
    {
        if (Kind == ConditionNodeKind.Leaf)
        {
            return Text.Trim().Length == 0 ? null : new LeafCondition(Text.Trim());
        }

        var items = Children
            .Select(child => child.ToCondition())
            .Where(condition => condition is not null)
            .Select(condition => condition!)
            .ToList();

        return items.Count switch
        {
            0 => null,
            1 => items[0],
            _ => Kind == ConditionNodeKind.And ? new AndCondition(items) : new OrCondition(items),
        };
    }

    /// <summary>자식을 붙이고 부모를 기록한다.</summary>
    public void Add(ConditionNodeViewModel child)
    {
        ArgumentNullException.ThrowIfNull(child);

        child.Parent = this;
        Children.Add(child);
        Notify();
    }

    /// <summary>지정한 위치에 자식을 끼워 넣는다.</summary>
    public void Insert(int index, ConditionNodeViewModel child)
    {
        ArgumentNullException.ThrowIfNull(child);

        child.Parent = this;
        Children.Insert(Math.Clamp(index, 0, Children.Count), child);
        Notify();
    }

    /// <summary>자식을 떼어낸다.</summary>
    public bool Remove(ConditionNodeViewModel child)
    {
        ArgumentNullException.ThrowIfNull(child);

        var removed = Children.Remove(child);
        if (removed)
        {
            child.Parent = null;
            Notify();
        }

        return removed;
    }

    /// <summary>형제 안에서 위로 옮긴다.</summary>
    public bool MoveUp()
    {
        if (Parent is null)
        {
            return false;
        }

        var index = Parent.Children.IndexOf(this);
        if (index <= 0)
        {
            return false;
        }

        Parent.Children.Move(index, index - 1);
        return true;
    }

    /// <summary>형제 안에서 아래로 옮긴다.</summary>
    public bool MoveDown()
    {
        if (Parent is null)
        {
            return false;
        }

        var index = Parent.Children.IndexOf(this);
        if (index < 0 || index >= Parent.Children.Count - 1)
        {
            return false;
        }

        Parent.Children.Move(index, index + 1);
        return true;
    }

    /// <summary>이 노드를 새 연산자 노드로 감싼다. 새 노드를 돌려준다.</summary>
    public ConditionNodeViewModel WrapIn(ConditionNodeKind kind)
    {
        var wrapper = new ConditionNodeViewModel(kind);
        var parent = Parent;

        if (parent is not null)
        {
            var index = parent.Children.IndexOf(this);
            parent.Children.RemoveAt(index);
            Parent = null;
            wrapper.Add(this);
            parent.Insert(index, wrapper);
        }
        else
        {
            wrapper.Add(this);
        }

        return wrapper;
    }

    /// <summary>루트까지 올라간다.</summary>
    public ConditionNodeViewModel Root() => Parent?.Root() ?? this;

    /// <summary>
    /// 하위 전체의 부모 포인터를 다시 맞춘다.
    /// 드래그로 노드가 옮겨지면 컬렉션은 바뀌지만 <see cref="Parent"/>는 낡은 값을 들고 있다.
    /// </summary>
    public void Reparent(ConditionNodeViewModel? parent = null)
    {
        Parent = parent;

        foreach (var child in Children)
        {
            child.Reparent(this);
        }

        Notify();
    }

    private static ConditionNodeViewModel Operator(
        ConditionNodeKind kind,
        IReadOnlyList<Condition> items)
    {
        var node = new ConditionNodeViewModel(kind);

        foreach (var item in items)
        {
            node.Add(FromCondition(item));
        }

        return node;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanAcceptChild));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsLeaf));
        OnPropertyChanged(nameof(LeafVisibility));
        Bubble();
    }

    /// <summary>루트까지 알린다. 규칙 쪽에서 미리보기를 다시 만든다.</summary>
    private void Bubble()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        Parent?.Bubble();
    }

    partial void OnKindChanged(ConditionNodeKind value) => Notify();

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(Label));
        Bubble();
    }
}
