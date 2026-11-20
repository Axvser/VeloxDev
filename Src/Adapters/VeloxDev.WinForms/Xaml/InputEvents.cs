using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>The shared pointer data carried by every pointer input event.</summary>
/// <remarks>
/// <see cref="Position"/> is expressed in <see cref="Source"/>'s client coordinates, matching WinForms'
/// <see cref="MouseEventArgs.Location"/>. <see cref="GetPosition"/> converts it to another control's client area.
/// </remarks>
public class PointerEventArgs : EventArgs
{
    /// <summary>Creates pointer event data.</summary>
    /// <param name="source">The control that originally received the native input.</param>
    /// <param name="position">The pointer position in the source's client coordinates.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public PointerEventArgs(System.Windows.Forms.Control source, Point position, Keys modifiers)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Position = position;
        Modifiers = modifiers;
    }

    /// <summary>The pointer position in <see cref="Source"/>'s client coordinates.</summary>
    public Point Position { get; }

    /// <summary>The modifier keys held at the time of the event.</summary>
    public Keys Modifiers { get; }

    /// <summary>The control that originally received the native input.</summary>
    public System.Windows.Forms.Control Source { get; }

    /// <summary>Whether a handler has handled the event; a handled event stops bubbling.</summary>
    public bool Handled { get; set; }

    /// <summary>Returns <see cref="Position"/> converted to <paramref name="relativeTo"/>'s client coordinates.</summary>
    /// <param name="relativeTo">The control whose client coordinates are requested.</param>
    /// <exception cref="ArgumentNullException"><paramref name="relativeTo"/> is <see langword="null"/>.</exception>
    public Point GetPosition(System.Windows.Forms.Control relativeTo)
    {
        if (relativeTo is null) throw new ArgumentNullException(nameof(relativeTo));
        if (ReferenceEquals(relativeTo, Source))
        {
            return Position;
        }

        try
        {
            return relativeTo.PointToClient(Source.PointToScreen(Position));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return Position;
        }
    }
}

/// <summary>Pointer press data, including which button and the click count.</summary>
public sealed class PointerPressedEventArgs : PointerEventArgs
{
    /// <summary>Creates pointer press data.</summary>
    /// <param name="source">The control that originally received the native input.</param>
    /// <param name="position">The pointer position in the source's client coordinates.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <param name="button">The pressed mouse button.</param>
    /// <param name="clickCount">The click count reported by WinForms; at least one.</param>
    public PointerPressedEventArgs(
        System.Windows.Forms.Control source,
        Point position,
        Keys modifiers,
        MouseButtons button,
        int clickCount)
        : base(source, position, modifiers)
    {
        Button = button;
        ClickCount = clickCount < 1 ? 1 : clickCount;
    }

    /// <summary>The pressed mouse button.</summary>
    public MouseButtons Button { get; }

    /// <summary>The click count; 1 for a single press, 2 for a double-click, and so on.</summary>
    public int ClickCount { get; }
}

/// <summary>Pointer movement data.</summary>
public sealed class PointerMovedEventArgs : PointerEventArgs
{
    /// <summary>Creates pointer movement data.</summary>
    /// <param name="source">The control that originally received the native input.</param>
    /// <param name="position">The pointer position in the source's client coordinates.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    public PointerMovedEventArgs(System.Windows.Forms.Control source, Point position, Keys modifiers)
        : base(source, position, modifiers)
    {
    }
}

/// <summary>Pointer release data, including which button was released.</summary>
public sealed class PointerReleasedEventArgs : PointerEventArgs
{
    /// <summary>Creates pointer release data.</summary>
    /// <param name="source">The control that originally received the native input.</param>
    /// <param name="position">The pointer position in the source's client coordinates.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <param name="button">The released mouse button.</param>
    public PointerReleasedEventArgs(System.Windows.Forms.Control source, Point position, Keys modifiers, MouseButtons button)
        : base(source, position, modifiers)
    {
        Button = button;
    }

    /// <summary>The released mouse button.</summary>
    public MouseButtons Button { get; }
}

/// <summary>Pointer entry data.</summary>
public sealed class PointerEnteredEventArgs : PointerEventArgs
{
    /// <summary>Creates pointer entry data.</summary>
    /// <param name="source">The control that originally received the native input.</param>
    /// <param name="position">The pointer position in the source's client coordinates.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    public PointerEnteredEventArgs(System.Windows.Forms.Control source, Point position, Keys modifiers)
        : base(source, position, modifiers)
    {
    }
}

/// <summary>Pointer exit data.</summary>
public sealed class PointerExitedEventArgs : PointerEventArgs
{
    /// <summary>Creates pointer exit data.</summary>
    /// <param name="source">The control that originally received the native input.</param>
    /// <param name="position">The pointer position in the source's client coordinates.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    public PointerExitedEventArgs(System.Windows.Forms.Control source, Point position, Keys modifiers)
        : base(source, position, modifiers)
    {
    }
}

/// <summary>Pointer wheel data.</summary>
public sealed class PointerWheelEventArgs : PointerEventArgs
{
    /// <summary>Creates pointer wheel data.</summary>
    /// <param name="source">The control that originally received the native input.</param>
    /// <param name="position">The pointer position in the source's client coordinates.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <param name="delta">The vertical wheel delta; positive means forward/up.</param>
    /// <param name="deltaX">The horizontal wheel delta; zero on current WinForms.</param>
    /// <param name="deltaY">The vertical wheel delta; equal to <paramref name="delta"/>.</param>
    public PointerWheelEventArgs(
        System.Windows.Forms.Control source,
        Point position,
        Keys modifiers,
        int delta,
        int deltaX,
        int deltaY)
        : base(source, position, modifiers)
    {
        Delta = delta;
        DeltaX = deltaX;
        DeltaY = deltaY;
    }

    /// <summary>The vertical wheel delta; positive means forward/up.</summary>
    public int Delta { get; }

    /// <summary>The horizontal wheel delta; zero on current WinForms.</summary>
    public int DeltaX { get; }

    /// <summary>The vertical wheel delta; equal to <see cref="Delta"/>.</summary>
    public int DeltaY { get; }
}

/// <summary>Keyboard input data translated from WinForms.</summary>
public sealed class KeyInputEventArgs : EventArgs
{
    /// <summary>Creates keyboard input data.</summary>
    /// <param name="source">The control that originally received the native input.</param>
    /// <param name="keyCode">The key that produced the event.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <param name="isRepeat">Whether the key was already down, making this a repeat.</param>
    public KeyInputEventArgs(System.Windows.Forms.Control source, Keys keyCode, Keys modifiers, bool isRepeat)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        KeyCode = keyCode;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
    }

    /// <summary>The control that originally received the native input.</summary>
    public System.Windows.Forms.Control Source { get; }

    /// <summary>The key that produced the event.</summary>
    public Keys KeyCode { get; }

    /// <summary>The modifier keys held at the time of the event.</summary>
    public Keys Modifiers { get; }

    /// <summary>Whether a handler has handled the event; a handled event stops bubbling.</summary>
    public bool Handled { get; set; }

    /// <summary>Whether the key was already down, making this a repeat.</summary>
    public bool IsRepeat { get; }
}
