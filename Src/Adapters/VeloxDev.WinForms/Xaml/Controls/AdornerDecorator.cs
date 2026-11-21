using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Hosts one child plus an <see cref="AdornerLayer"/> that paints adorners over it.
/// </summary>
/// <remarks>
/// The child and the layer share the decorator's content area; the layer is raised above the child, so its adorners
/// stay visible while its colour-key background remains transparent. The child can be assigned through
/// <see cref="Child"/> or declared directly in XAML.
/// </remarks>
public class AdornerDecorator : Control
{
    /// <summary>The element the adorner layer decorates.</summary>
    public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        nameof(Child),
        typeof(System.Windows.Forms.Control),
        typeof(AdornerDecorator),
        new PropertyMetadata(null, OnChildPropertyChanged));

    private System.Windows.Forms.Control? _child;

    /// <summary>Creates the decorator and its adorner layer.</summary>
    public AdornerDecorator()
    {
        DoubleBuffered = true;
        AdornerLayer = new AdornerLayer { Name = "PART_AdornerLayer" };
        Controls.Add(AdornerLayer);
    }

    /// <summary>The layer that paints adorners over the child.</summary>
    public AdornerLayer AdornerLayer { get; }

    /// <summary>Gets or sets the element the adorner layer decorates.</summary>
    /// <remarks>Reading an unset property returns a child declared directly in XAML, if there is one.</remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? Child
    {
        get
        {
            if (GetValue(ChildProperty) is System.Windows.Forms.Control assigned)
            {
                return assigned;
            }

            return DirectChild();
        }
        set => SetValue(ChildProperty, value);
    }

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

        var content = XamlLayout.ContentBounds(this);
        if (Child is { } child)
        {
            if (child is Control xaml)
            {
                XamlLayout.Arrange(xaml, content);
            }
            else if (child.Bounds != content)
            {
                child.Bounds = content;
            }
        }

        if (AdornerLayer.Bounds != content)
        {
            AdornerLayer.Bounds = content;
        }

        // 层是覆盖在子控件上的兄弟窗口，必须保持在 z 序最上；子控件加入后这一趟重新抬回来。
        AdornerLayer.BringToFront();
        AdornerLayer.Invalidate();
    }

    /// <inheritdoc />
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (!ReferenceEquals(e.Control, AdornerLayer))
        {
            PerformLayout();
        }
    }

    private static void OnChildPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is AdornerDecorator decorator && !decorator.IsDisposed)
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
        if (previous is not null
            && !ReferenceEquals(previous, AdornerLayer)
            && Controls.Contains(previous))
        {
            Controls.Remove(previous);
        }

        if (next is not null
            && !ReferenceEquals(next, AdornerLayer)
            && !Controls.Contains(next))
        {
            Controls.Add(next);
        }

        PerformLayout();
        Invalidate();
    }

    private System.Windows.Forms.Control? DirectChild()
    {
        foreach (System.Windows.Forms.Control current in Controls)
        {
            if (!ReferenceEquals(current, AdornerLayer))
            {
                return current;
            }
        }

        return null;
    }
}
