using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml.Visuals;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Hosts a lightweight <see cref="ContainerVisual"/> tree and paints it in one pass without creating a child window
/// per visual.
/// </summary>
/// <remarks>
/// <para>
/// This is the opt-in composition surface of the WinForms XAML runtime. Decoration, text, shapes and connector
/// batches built from <see cref="Visual"/> nodes support <see cref="Visual.Transform"/>, <see cref="Visual.Opacity"/>,
/// <see cref="Visual.Clip"/> and <see cref="Visual.Effect"/>; native controls and HWND-backed XAML controls remain
/// ordinary children and are not part of this tree.
/// </para>
/// <para>
/// The root fills the host's client area. Assign <see cref="Root"/> children in local coordinates and call
/// <see cref="InvalidateVisual"/> after changing the tree.
/// </para>
/// </remarks>
public class VisualHost : Control
{
    /// <summary>Creates the host with the double-buffered paint style it needs.</summary>
    public VisualHost()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
        Root = new ContainerVisual();
    }

    /// <summary>The visual tree painted by this host.</summary>
    public ContainerVisual Root { get; }

    /// <summary>Requests a repaint after the visual tree changed.</summary>
    public void InvalidateVisual() => Invalidate();

    /// <summary>Renders <see cref="Root"/> into <paramref name="graphics"/> at <paramref name="size"/>.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="size">The size assigned to the root for this pass.</param>
    public void RenderTo(Graphics graphics, Size size)
    {
        if (graphics is null) throw new ArgumentNullException(nameof(graphics));
        Root.Bounds = new RectangleF(0f, 0f, size.Width, size.Height);
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Root.Render(graphics);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <summary>Returns the deepest visual under <paramref name="point"/>.</summary>
    /// <param name="point">A point in the host's client coordinates.</param>
    public Visual? HitTestVisual(Point point)
    {
        if (Root.Bounds.Width <= 0f || Root.Bounds.Height <= 0f)
        {
            Root.Bounds = new RectangleF(0f, 0f, ClientSize.Width, ClientSize.Height);
        }

        return Root.HitTestVisual(point);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        RenderTo(e.Graphics, ClientSize);
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}
