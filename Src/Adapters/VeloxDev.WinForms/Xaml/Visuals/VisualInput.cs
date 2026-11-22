using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>Pointer data routed through the visual tree of a <see cref="VisualSurface"/>.</summary>
public sealed class VisualPointerEventArgs : EventArgs
{
    internal VisualPointerEventArgs(
        PointF position,
        MouseButtons buttons,
        MouseButtons button,
        int clickCount,
        int delta,
        Keys modifiers)
    {
        Position = position;
        Buttons = buttons;
        Button = button;
        ClickCount = Math.Max(1, clickCount);
        Delta = delta;
        Modifiers = modifiers;
    }

    /// <summary>The pointer position in the surface's client coordinates.</summary>
    public PointF Position { get; }

    /// <summary>The buttons currently held.</summary>
    public MouseButtons Buttons { get; }

    /// <summary>The button that produced a press, release or double-click.</summary>
    public MouseButtons Button { get; }

    /// <summary>The number of clicks for a press; 1 for a single click.</summary>
    public int ClickCount { get; }

    /// <summary>The wheel delta; positive scrolls up.</summary>
    public int Delta { get; }

    /// <summary>The modifier keys held.</summary>
    public Keys Modifiers { get; }

    /// <summary>Whether an element handled the event; a handled event stops bubbling.</summary>
    public bool Handled { get; set; }

    /// <summary>Converts the surface position into an element's local coordinates.</summary>
    /// <param name="element">The element whose local space is used.</param>
    public PointF GetPosition(VisualElement element)
        => element is null ? Position : element.PointFromSurface(Position);
}

/// <summary>Keyboard data routed to the focused element of a <see cref="VisualSurface"/>.</summary>
public sealed class VisualKeyEventArgs : EventArgs
{
    internal VisualKeyEventArgs(Keys keyCode, Keys modifiers, bool isRepeat)
    {
        KeyCode = keyCode;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
    }

    /// <summary>The key that produced the event.</summary>
    public Keys KeyCode { get; }

    /// <summary>The modifier keys held.</summary>
    public Keys Modifiers { get; }

    /// <summary>Whether the key was already down, making this a repeat.</summary>
    public bool IsRepeat { get; }

    /// <summary>Whether an element handled the event; a handled event stops bubbling.</summary>
    public bool Handled { get; set; }
}

/// <summary>Text input routed to the focused element of a <see cref="VisualSurface"/>.</summary>
public sealed class VisualTextInputEventArgs : EventArgs
{
    internal VisualTextInputEventArgs(string text)
    {
        Text = text;
    }

    /// <summary>The characters to insert; a string so an IME composition can arrive as one unit later.</summary>
    public string Text { get; }

    /// <summary>Whether an element handled the event; a handled event stops bubbling.</summary>
    public bool Handled { get; set; }
}
