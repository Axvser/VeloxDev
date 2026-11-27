// VeloxDev customization: the tree takes its model from the inherited DataContext and owns the host-side key/wheel policy.
using Easel;
using Easel.Visuals;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public sealed partial class TreeView : Easel.Visuals.Controls.UserControl
{
    private ITreeComponent? _tree;

    public TreeView() => InitializeComponent();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, DataContextProperty))
        {
            BindTree(DataContext as ITreeComponent);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            BindTree(null);
        }

        base.Dispose(disposing);
    }

    // Rebinds the host-side input policy when the inherited tree changes.
    private void BindTree(ITreeComponent? tree)
    {
        if (ReferenceEquals(tree, _tree))
        {
            return;
        }

        if (_tree?.GetHelper() is IInputEvents oldEvents)
        {
            oldEvents.Input.KeyDown -= OnKeyDown;
            oldEvents.Input.PointerWheelChanged -= OnWheel;
        }

        _tree = tree;
        if (_tree?.GetHelper() is IInputEvents events)
        {
            events.Input.KeyDown += OnKeyDown;
            events.Input.PointerWheelChanged += OnWheel;
        }
    }

    // 撤销/重做与删除都是宿主的：库只把按键路由过来，做什么由这里写。
    private void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        if (_tree is not { } tree)
        {
            return;
        }

        if (e.Key == InputKey.Delete)
        {
            if (WorkflowInput.For(tree).HoveredLink is { } hovered && hovered.DeleteCommand.CanExecute(null))
            {
                hovered.DeleteCommand.Execute(null);
                e.Handle.PreventDefault = true;
            }

            return;
        }

        if (!e.Modifiers.HasFlag(InputModifiers.Control)) return;

        var redo = e.Modifiers.HasFlag(InputModifiers.Shift);
        if (e.Key == InputKey.Z && !redo) tree.UndoCommand.Execute(null);
        else if (e.Key == InputKey.Y || (e.Key == InputKey.Z && redo)) tree.RedoCommand.Execute(null);
        else return;

        e.Handle.PreventDefault = true;
    }

    // 画布滚轮由适配器整笔接管；按住 Shift 时改从横轴滚。
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_tree is not { } tree || !e.Modifiers.HasFlag(InputModifiers.Shift)) return;

        e.Handle.PreventDefault = true;
        // 竖轴正 = 远离用户，塞进横轴要取反 —— 横轴正 = 向右，而「远离用户」在画面上是向左。
        WorkflowInput.For(tree).Scroller?.ScrollBy(-e.DeltaY, 0d);
    }
}
