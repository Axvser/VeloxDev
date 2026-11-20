using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>A Control that exposes the protected native input callbacks to tests.</summary>
public class InputProbeControl : VeloxDev.WinForms.Xaml.Controls.Control
{
    /// <summary>Raises the native mouse-down path.</summary>
    public void SimulateMouseDown(MouseEventArgs e) => OnMouseDown(e);

    /// <summary>Raises the native mouse-move path.</summary>
    public void SimulateMouseMove(MouseEventArgs e) => OnMouseMove(e);

    /// <summary>Raises the native mouse-up path.</summary>
    public void SimulateMouseUp(MouseEventArgs e) => OnMouseUp(e);

    /// <summary>Raises the native mouse-enter path.</summary>
    public void SimulateMouseEnter() => OnMouseEnter(EventArgs.Empty);

    /// <summary>Raises the native mouse-leave path.</summary>
    public void SimulateMouseLeave() => OnMouseLeave(EventArgs.Empty);

    /// <summary>Raises the native mouse-wheel path.</summary>
    public void SimulateMouseWheel(MouseEventArgs e) => OnMouseWheel(e);

    /// <summary>Raises the native key-down path.</summary>
    public void SimulateKeyDown(KeyEventArgs e) => OnKeyDown(e);

    /// <summary>Raises the native key-up path.</summary>
    public void SimulateKeyUp(KeyEventArgs e) => OnKeyUp(e);

    /// <summary>Raises the native got-focus path.</summary>
    public void SimulateGotFocus() => OnGotFocus(EventArgs.Empty);

    /// <summary>Raises the native lost-focus path.</summary>
    public void SimulateLostFocus() => OnLostFocus(EventArgs.Empty);
}

/// <summary>A native Button that exposes the protected native input callbacks to tests.</summary>
public sealed class InputProbeButton : Button
{
    /// <summary>Raises the native mouse-down path.</summary>
    public void SimulateMouseDown(MouseEventArgs e) => OnMouseDown(e);

    /// <summary>Raises the native mouse-move path.</summary>
    public void SimulateMouseMove(MouseEventArgs e) => OnMouseMove(e);

    /// <summary>Raises the native mouse-up path.</summary>
    public void SimulateMouseUp(MouseEventArgs e) => OnMouseUp(e);

    /// <summary>Raises the native mouse-enter path.</summary>
    public void SimulateMouseEnter() => OnMouseEnter(EventArgs.Empty);

    /// <summary>Raises the native mouse-leave path.</summary>
    public void SimulateMouseLeave() => OnMouseLeave(EventArgs.Empty);

    /// <summary>Raises the native mouse-wheel path.</summary>
    public void SimulateMouseWheel(MouseEventArgs e) => OnMouseWheel(e);

    /// <summary>Raises the native key-down path.</summary>
    public void SimulateKeyDown(KeyEventArgs e) => OnKeyDown(e);

    /// <summary>Raises the native key-up path.</summary>
    public void SimulateKeyUp(KeyEventArgs e) => OnKeyUp(e);
}
