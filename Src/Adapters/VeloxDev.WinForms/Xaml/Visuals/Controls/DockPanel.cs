using System.Drawing;
using System.Runtime.CompilerServices;
using XamlDockPlacement = VeloxDev.WinForms.Xaml.Controls.DockPlacement;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>Docks each child to one edge of the room left by the children docked before it.</summary>
/// <remarks>
/// <para>
/// Child order is the docking order. The last visible child fills the remaining room when
/// <see cref="LastChildFill"/> is <see langword="true"/>, whatever <see cref="GetDock"/> returns for it.
/// </para>
/// <para>
/// <see cref="VeloxDev.WinForms.Xaml.Controls.DockPlacement.None"/> leaves the child at the current corner at its
/// desired size. A collapsed child
/// is skipped and does not count as the last child.
/// </para>
/// </remarks>
public class DockPanel : Panel
{
    private static readonly ConditionalWeakTable<VisualElement, DockState> States = new();

    private bool _lastChildFill = true;

    /// <summary>Whether the last visible child fills the room left by the docked children.</summary>
    public bool LastChildFill
    {
        get => _lastChildFill;
        set
        {
            if (_lastChildFill == value)
            {
                return;
            }

            _lastChildFill = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Reads which edge <paramref name="element"/> docks to.</summary>
    /// <param name="element">The element whose attached value is read.</param>
    /// <returns>The dock edge; <see cref="VeloxDev.WinForms.Xaml.Controls.DockPlacement.Left"/> when the element
    /// never set one.</returns>
    public static XamlDockPlacement GetDock(VisualElement element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return StateOf(element).Dock;
    }

    /// <summary>Writes which edge <paramref name="element"/> docks to.</summary>
    /// <param name="element">The element receiving the attached value.</param>
    /// <param name="value">The edge to dock to.</param>
    public static void SetDock(VisualElement element, XamlDockPlacement value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        StateOf(element).Dock = value;
        element.InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var content = new Size(
            Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal)),
            Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical)));
        var width = 0;
        var height = 0;

        foreach (var child in ArrangedChildren())
        {
            var measured = child.Measure(content);
            switch (GetDock(child))
            {
                case XamlDockPlacement.Left:
                case XamlDockPlacement.Right:
                    width += measured.Width;
                    height = Math.Max(height, measured.Height);
                    break;
                case XamlDockPlacement.Top:
                case XamlDockPlacement.Bottom:
                    width = Math.Max(width, measured.Width);
                    height += measured.Height;
                    break;
                default:
                    width = Math.Max(width, measured.Width);
                    height = Math.Max(height, measured.Height);
                    break;
            }
        }

        return new Size(
            width + (int)Math.Ceiling(padding.Horizontal),
            height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        var padding = Padding;
        var remaining = new RectangleF(
            padding.Left,
            padding.Top,
            Math.Max(0f, finalSize.Width - padding.Horizontal),
            Math.Max(0f, finalSize.Height - padding.Vertical));

        var children = ArrangedChildren().ToList();
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (index == children.Count - 1 && LastChildFill)
            {
                child.Arrange(remaining);
                continue;
            }

            var measured = child.Measure(new Size(
                (int)Math.Ceiling(remaining.Width),
                (int)Math.Ceiling(remaining.Height)));
            var dock = GetDock(child);
            RectangleF slot;
            switch (dock)
            {
                case XamlDockPlacement.Left:
                    slot = new RectangleF(
                        remaining.Left,
                        remaining.Top,
                        Math.Min(measured.Width, remaining.Width),
                        remaining.Height);
                    remaining = new RectangleF(
                        remaining.Left + slot.Width,
                        remaining.Top,
                        Math.Max(0f, remaining.Width - slot.Width),
                        remaining.Height);
                    break;
                case XamlDockPlacement.Top:
                    slot = new RectangleF(
                        remaining.Left,
                        remaining.Top,
                        remaining.Width,
                        Math.Min(measured.Height, remaining.Height));
                    remaining = new RectangleF(
                        remaining.Left,
                        remaining.Top + slot.Height,
                        remaining.Width,
                        Math.Max(0f, remaining.Height - slot.Height));
                    break;
                case XamlDockPlacement.Right:
                    slot = new RectangleF(
                        remaining.Right - Math.Min(measured.Width, remaining.Width),
                        remaining.Top,
                        Math.Min(measured.Width, remaining.Width),
                        remaining.Height);
                    remaining = new RectangleF(
                        remaining.Left,
                        remaining.Top,
                        Math.Max(0f, remaining.Width - slot.Width),
                        remaining.Height);
                    break;
                case XamlDockPlacement.Bottom:
                    slot = new RectangleF(
                        remaining.Left,
                        remaining.Bottom - Math.Min(measured.Height, remaining.Height),
                        remaining.Width,
                        Math.Min(measured.Height, remaining.Height));
                    remaining = new RectangleF(
                        remaining.Left,
                        remaining.Top,
                        remaining.Width,
                        Math.Max(0f, remaining.Height - slot.Height));
                    break;
                default:
                    slot = new RectangleF(
                        remaining.Left,
                        remaining.Top,
                        Math.Min(measured.Width, remaining.Width),
                        Math.Min(measured.Height, remaining.Height));
                    break;
            }

            child.Arrange(slot);
        }
    }

    // 折叠的子项不参与布局，也不算“最后一个”。
    private IEnumerable<VisualElement> ArrangedChildren()
    {
        foreach (var child in Children)
        {
            if (child is VisualElement element && element.Visibility != XamlVisibility.Collapsed)
            {
                yield return element;
            }
        }
    }

    private static DockState StateOf(VisualElement element)
        => States.GetValue(element, static _ => new DockState());

    private sealed class DockState
    {
        public XamlDockPlacement Dock = XamlDockPlacement.Left;
    }
}
