using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A plain container: a <see cref="Control"/> that paints its children with double buffering and no extra behavior.
/// </summary>
/// <remarks>Use it where a XAML tree needs a grouping element without a native WinForms counterpart.</remarks>
public class Panel : Control
{
    /// <summary>Creates the container and enables double buffering.</summary>
    public Panel()
    {
        DoubleBuffered = true;
    }
}

/// <summary>
/// A container that enables <see cref="System.Windows.Forms.ScrollableControl.AutoScroll"/> by default.
/// </summary>
/// <remarks>
/// <see cref="Control"/> already derives from <see cref="System.Windows.Forms.ScrollableControl"/>, so scrolling is
/// available on every base control; this type only turns it on for the containers that are meant to scroll.
/// </remarks>
public class ScrollableControl : Control
{
    /// <summary>Creates the container with automatic scrolling enabled.</summary>
    public ScrollableControl()
    {
        AutoScroll = true;
    }
}

/// <summary>
/// An absolutely positioned drawing surface.
/// </summary>
/// <remarks>
/// WinForms positions children by <see cref="System.Windows.Forms.Control.Location"/>, so a canvas needs no layout
/// engine. This type enables the double-buffered, resize-redrawing paint style that drawing surfaces usually want.
/// </remarks>
public class Canvas : Control
{
    /// <summary>Creates the canvas with double buffering and resize redraw enabled.</summary>
    public Canvas()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw,
            true);
    }
}

/// <summary>
/// The base class for XAML views with a code-behind file.
/// </summary>
/// <remarks>
/// The type exists so views can write <c>public partial class MyView : UserControl</c> without importing the XAML
/// namespace through an alias. It behaves as a normal <see cref="Control"/>.
/// </remarks>
public class UserControl : Control
{
    /// <summary>Creates the view.</summary>
    public UserControl()
    {
    }
}
