using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A container that gives its single child the whole content area.
/// </summary>
/// <remarks>
/// The child is either assigned through <see cref="Child"/> or declared directly in XAML. Unlike
/// <see cref="Border"/> it paints nothing of its own; it only positions the child inside the padding.
/// </remarks>
public class Decorator : Control
{
    /// <summary>The single child laid out in this decorator's content area.</summary>
    public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        nameof(Child),
        typeof(System.Windows.Forms.Control),
        typeof(Decorator),
        new PropertyMetadata(null, OnChildPropertyChanged));

    private System.Windows.Forms.Control? _child;

    /// <summary>Creates the decorator with double buffering.</summary>
    public Decorator()
    {
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the single child laid out in this decorator's content area.</summary>
    /// <remarks>Reading an unset property returns a child declared directly in XAML, if there is one.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? Child
    {
        get => (System.Windows.Forms.Control?)GetValue(ChildProperty) ?? DirectChild();
        set => SetValue(ChildProperty, value);
    }

    /// <inheritdoc />
    protected override ControlCollection CreateControlsInstance() => new SingleChildCollection(this);

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var padding = Padding;
        if (Child is not { } child)
        {
            return new Size(
                (int)Math.Ceiling(padding.Horizontal),
                (int)Math.Ceiling(padding.Vertical));
        }

        var measured = XamlLayout.Measure(child, proposedSize);
        return new Size(
            measured.Width + (int)Math.Ceiling(padding.Horizontal),
            measured.Height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (Child is not { } child)
        {
            return;
        }

        var bounds = XamlLayout.ContentBounds(this);
        if (child is Control xaml)
        {
            XamlLayout.Arrange(xaml, bounds);
        }
        else if (child.Bounds != bounds)
        {
            child.Bounds = bounds;
        }
    }

    private static void OnChildPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Decorator decorator && !decorator.IsDisposed)
        {
            decorator.ApplyChild(e.NewValue as System.Windows.Forms.Control);
        }
    }

    private void ApplyChild(System.Windows.Forms.Control? next)
    {
        var previous = _child ?? DirectChild();
        if (ReferenceEquals(previous, next))
        {
            _child = next;
            return;
        }

        _child = next;
        if (previous is not null && Controls.Contains(previous))
        {
            Controls.Remove(previous);
        }

        if (next is not null && !Controls.Contains(next))
        {
            Controls.Add(next);
        }

        PerformLayout();
        Invalidate();
    }

    private System.Windows.Forms.Control? DirectChild()
        => Controls.Count > 0 ? Controls[0] : null;

    // 单子容器：第二个直接声明的子控件会抢走布局，直接报错而不是悄悄忽略。
    private sealed class SingleChildCollection(Decorator owner) : ControlCollection(owner)
    {
        public override void Add(System.Windows.Forms.Control? value)
        {
            if (value is not null && Count > 0 && !owner.Controls.Contains(value))
            {
                throw new InvalidOperationException("A Decorator accepts a single child control.");
            }

            base.Add(value);
        }
    }
}
