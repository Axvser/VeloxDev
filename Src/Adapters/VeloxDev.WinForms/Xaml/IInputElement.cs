namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// The focus and pointer state contract every XAML base control implements.
/// </summary>
/// <remarks>
/// The members map onto WinForms concepts: <see cref="Focusable"/> onto the selectable control style,
/// <see cref="Focus"/> onto <see cref="System.Windows.Forms.Control.Focus"/>, and capture onto
/// <see cref="System.Windows.Forms.Control.Capture"/>. The state properties are backed by dependency properties so
/// styles and triggers can target them.
/// </remarks>
public interface IInputElement
{
    /// <summary>Whether the element can receive keyboard focus.</summary>
    bool Focusable { get; set; }

    /// <summary>Whether the element currently has keyboard focus.</summary>
    bool IsKeyboardFocused { get; }

    /// <summary>Whether the pointer is currently over the element.</summary>
    bool IsPointerOver { get; }

    /// <summary>Moves keyboard focus to the element.</summary>
    /// <returns><see langword="true"/> when the element received focus.</returns>
    bool Focus();

    /// <summary>Captures the pointer.</summary>
    /// <returns><see langword="true"/> when the capture was taken.</returns>
    bool CapturePointer();

    /// <summary>Releases a pointer capture taken by <see cref="CapturePointer"/>.</summary>
    void ReleasePointerCapture();
}
