using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Docks each child to one edge of the remaining room, like a WPF or Avalonia dock panel.
/// </summary>
/// <remarks>
/// Child order is the docking order. An undocked child placed last fills the remaining room when
/// <see cref="LastChildFill"/> is <see langword="true"/>; otherwise it is measured and placed at the current corner.
/// </remarks>
public class DockPanel : Control
{
    /// <summary>Which edge a child docks to.</summary>
    public static readonly DependencyProperty DockProperty = DependencyProperty.RegisterAttached(
        "Dock",
        typeof(DockPlacement),
        typeof(DockPanel),
        new PropertyMetadata(DockPlacement.Left) { AffectsMeasure = true });

    /// <summary>Whether the last child fills the room left by the docked children.</summary>
    public static readonly DependencyProperty LastChildFillProperty = DependencyProperty.Register(
        nameof(LastChildFill),
        typeof(bool),
        typeof(DockPanel),
        new PropertyMetadata(true) { AffectsMeasure = true });

    /// <summary>Creates the panel with double buffering.</summary>
    public DockPanel()
    {
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets whether the last child fills the remaining room.</summary>
    public bool LastChildFill
    {
        get => (bool)GetValue(LastChildFillProperty)!;
        set => SetValue(LastChildFillProperty, value);
    }

    /// <summary>Reads the dock edge of <paramref name="element"/>.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    public static DockPlacement GetDock(System.Windows.Forms.Control element)
        => (DockPlacement)(DockProperty.GetValue(element) ?? DockPlacement.Left);

    /// <summary>Writes the dock edge of <paramref name="element"/>.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The edge to dock to.</param>
    public static void SetDock(System.Windows.Forms.Control element, DockPlacement value)
        => DockProperty.SetValue(element, value);

    protected internal override Size MeasureCore(Size availableSize)
    {
        var bounds = XamlLayout.ContentBounds(this).Size;
        var padding = Padding;
        var content = new Size(
            availableSize.Width > 0 ? Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal)) : bounds.Width,
            availableSize.Height > 0 ? Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical)) : bounds.Height);
        var width = 0;
        var height = 0;

        foreach (System.Windows.Forms.Control child in Controls)
        {
            if (!XamlLayout.OccupiesSpace(child))
            {
                continue;
            }

            var measured = XamlLayout.Measure(child, content);
            switch (GetDock(child))
            {
                case DockPlacement.Left:
                case DockPlacement.Right:
                    width += measured.Width;
                    height = Math.Max(height, measured.Height);
                    break;
                case DockPlacement.Top:
                case DockPlacement.Bottom:
                    width = Math.Max(width, measured.Width);
                    height += measured.Height;
                    break;
                default:
                    width = Math.Max(width, measured.Width);
                    height = Math.Max(height, measured.Height);
                    break;
            }
        }

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
        var remaining = content;
        var children = Controls.Cast<System.Windows.Forms.Control>().ToArray();

        for (var index = 0; index < children.Length; index++)
        {
            var item = children[index];
            if (!XamlLayout.OccupiesSpace(item))
            {
                XamlLayout.Hide(item);
                continue;
            }

            var isLast = true;
            for (var later = index + 1; later < children.Length; later++)
            {
                if (XamlLayout.OccupiesSpace(children[later]))
                {
                    isLast = false;
                    break;
                }
            }

            var dock = GetDock(item);
            // WPF 的 LastChildFill 对最后一个子项无条件生效，连它声明的 Dock 也一并忽略。
            if (isLast && LastChildFill)
            {
                XamlLayout.Arrange(item, remaining);
                continue;
            }

            var measured = XamlLayout.Measure(item, remaining.Size);
            Rectangle slot;
            switch (dock)
            {
                case DockPlacement.Left:
                    slot = new Rectangle(remaining.Left, remaining.Top, Math.Min(measured.Width, remaining.Width), remaining.Height);
                    remaining = new Rectangle(remaining.Left + slot.Width, remaining.Top, Math.Max(0, remaining.Width - slot.Width), remaining.Height);
                    break;
                case DockPlacement.Top:
                    slot = new Rectangle(remaining.Left, remaining.Top, remaining.Width, Math.Min(measured.Height, remaining.Height));
                    remaining = new Rectangle(remaining.Left, remaining.Top + slot.Height, remaining.Width, Math.Max(0, remaining.Height - slot.Height));
                    break;
                case DockPlacement.Right:
                    slot = new Rectangle(remaining.Right - Math.Min(measured.Width, remaining.Width), remaining.Top, Math.Min(measured.Width, remaining.Width), remaining.Height);
                    remaining = new Rectangle(remaining.Left, remaining.Top, Math.Max(0, remaining.Width - slot.Width), remaining.Height);
                    break;
                case DockPlacement.Bottom:
                    slot = new Rectangle(remaining.Left, remaining.Bottom - Math.Min(measured.Height, remaining.Height), remaining.Width, Math.Min(measured.Height, remaining.Height));
                    remaining = new Rectangle(remaining.Left, remaining.Top, remaining.Width, Math.Max(0, remaining.Height - slot.Height));
                    break;
                default:
                    slot = new Rectangle(remaining.Left, remaining.Top, Math.Min(measured.Width, remaining.Width), Math.Min(measured.Height, remaining.Height));
                    break;
            }

            XamlLayout.Arrange(item, slot);
        }
    }


}
