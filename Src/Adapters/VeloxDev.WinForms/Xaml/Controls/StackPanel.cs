using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Lays its children out one after another along one axis, like a WPF or Avalonia stack panel.
/// </summary>
/// <remarks>
/// The stack axis uses each child's measured size; the cross axis stretches when the child's
/// <see cref="Control.HorizontalAlignment"/> or <see cref="Control.VerticalAlignment"/> is
/// <see cref="HorizontalAlignment.Stretch"/>. <see cref="Spacing"/> is inserted between children only.
/// </remarks>
public class StackPanel : Control
{
    /// <summary>Whether children stack vertically (the default) or horizontally.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(StackPanel),
        new PropertyMetadata(Orientation.Vertical, OnLayoutPropertyChanged));

    /// <summary>The number of pixels inserted between adjacent children.</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(int),
        typeof(StackPanel),
        new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>Creates the panel with double buffering.</summary>
    public StackPanel()
    {
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the stacking axis.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets the gap between adjacent children, in pixels.</summary>
    public int Spacing
    {
        get => (int)GetValue(SpacingProperty)!;
        set => SetValue(SpacingProperty, value);
    }

    protected internal override Size MeasureCore(Size availableSize)
    {
        var content = ContentSize(availableSize);
        var spacing = Math.Max(0, Spacing);
        var main = 0;
        var cross = 0;
        var count = 0;

        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                continue;
            }

            var measured = Orientation == Orientation.Vertical
                ? XamlLayout.Measure(child, new Size(content.Width, 0))
                : XamlLayout.Measure(child, new Size(0, content.Height));
            if (Orientation == Orientation.Vertical)
            {
                main += measured.Height;
                cross = Math.Max(cross, measured.Width);
            }
            else
            {
                main += measured.Width;
                cross = Math.Max(cross, measured.Height);
            }

            count++;
        }

        if (count > 1)
        {
            main += spacing * (count - 1);
        }

        var width = Orientation == Orientation.Vertical ? cross : main;
        var height = Orientation == Orientation.Vertical ? main : cross;
        return ConstrainSize(
            new Size(
                width + (int)Math.Ceiling(Padding.Horizontal),
                height + (int)Math.Ceiling(Padding.Vertical)),
            availableSize);
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var content = XamlLayout.ContentBounds(this);
        var spacing = Math.Max(0, Spacing);
        var position = Orientation == Orientation.Vertical ? content.Top : content.Left;
        var children = Controls.Cast<System.Windows.Forms.Control>().ToArray();

        foreach (var child in children)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                XamlLayout.Hide(child);
                continue;
            }

            var measured = Orientation == Orientation.Vertical
                ? XamlLayout.Measure(child, new Size(content.Width, 0))
                : XamlLayout.Measure(child, new Size(0, content.Height));

            var slot = Orientation == Orientation.Vertical
                ? new Rectangle(content.Left, position, content.Width, Math.Max(0, measured.Height))
                : new Rectangle(position, content.Top, Math.Max(0, measured.Width), content.Height);

            XamlLayout.Arrange(child, slot);
            position += Orientation == Orientation.Vertical ? slot.Height : slot.Width;
            position += spacing;
        }
    }

    private Size ContentSize(Size available)
    {
        var padding = Padding;
        return new Size(
            Math.Max(0, available.Width - (int)Math.Ceiling(padding.Horizontal)),
            Math.Max(0, available.Height - (int)Math.Ceiling(padding.Vertical)));
    }

    private static void OnLayoutPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Parent?.PerformLayout();
        }
    }
}
