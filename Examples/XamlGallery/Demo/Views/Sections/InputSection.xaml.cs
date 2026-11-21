using VeloxDev.WinForms.Xaml;

namespace Demo.Views.Sections;

/// <summary>The input and chrome examples.</summary>
public sealed partial class InputSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    /// <summary>Creates the section.</summary>
    public InputSection()
    {
        InitializeComponent();
    }

    private void OnPopupClick(object? sender, System.EventArgs e)
    {
        popup.IsOpen = !popup.IsOpen;
    }

    private void OnPadPointerEntered(object? sender, PointerEnteredEventArgs e)
        => inputReadout.Text = $"Pointer entered at {e.GetPosition(inputPad).X}, {e.GetPosition(inputPad).Y}";

    private void OnPadPointerMoved(object? sender, PointerMovedEventArgs e)
    {
        var position = e.GetPosition(inputPad);
        inputReadout.Text = $"Pointer {position.X}, {position.Y} · modifiers {e.Modifiers}";
    }

    private void OnPadPointerExited(object? sender, PointerExitedEventArgs e)
        => inputReadout.Text = "Pointer left the pad.";

    private void OnPadKeyDown(object? sender, KeyInputEventArgs e)
        => inputReadout.Text = $"Key {e.KeyCode} · modifiers {e.Modifiers} · repeat {e.IsRepeat}";
}
