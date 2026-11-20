using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The <see cref="IInputElement"/> contract on the XAML base controls: focusability, keyboard focus state,
/// pointer-over state and pointer capture.
/// </summary>
[STATestClass]
public sealed class InputElementTests
{
    [TestMethod]
    public void Focusable_MapsToTheNativeSelectableStyle()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);

        Assert.IsTrue(control.Focusable);
        Assert.IsTrue(control.CanSelect);

        control.Focusable = false;

        Assert.IsFalse(control.Focusable);
        Assert.IsFalse(control.CanSelect);
        Assert.IsFalse(control.Focus(), "a non-focusable control must not take focus");

        control.Focusable = true;
        Assert.IsTrue(control.CanSelect);
    }

    [TestMethod]
    public void IsKeyboardFocused_FollowsTheNativeFocusCallbacks()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);

        Assert.IsFalse(control.IsKeyboardFocused);

        control.SimulateGotFocus();
        Assert.IsTrue(control.IsKeyboardFocused);
        Assert.IsTrue((bool)VeloxDev.WinForms.Xaml.Controls.Control.IsKeyboardFocusedProperty.GetValue(control)!);

        control.SimulateLostFocus();
        Assert.IsFalse(control.IsKeyboardFocused);
        Assert.IsFalse((bool)VeloxDev.WinForms.Xaml.Controls.Control.IsKeyboardFocusedProperty.GetValue(control)!);
    }

    [TestMethod]
    public void IsPointerOver_FollowsMouseEnterAndLeave()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);

        Assert.IsFalse(control.IsPointerOver);

        control.SimulateMouseEnter();
        Assert.IsTrue(control.IsPointerOver);
        Assert.IsTrue((bool)VeloxDev.WinForms.Xaml.Controls.Control.IsPointerOverProperty.GetValue(control)!);

        control.SimulateMouseLeave();
        Assert.IsFalse(control.IsPointerOver);
        Assert.IsFalse((bool)VeloxDev.WinForms.Xaml.Controls.Control.IsPointerOverProperty.GetValue(control)!);
    }

    [TestMethod]
    public void FocusableStateProperties_AreDependencyProperties()
    {
        using var control = new InputProbeControl();

        Assert.IsTrue((bool)VeloxDev.WinForms.Xaml.Controls.Control.FocusableProperty.DefaultValue!);
        Assert.IsFalse((bool)VeloxDev.WinForms.Xaml.Controls.Control.IsKeyboardFocusedProperty.DefaultValue!);
        Assert.IsFalse((bool)VeloxDev.WinForms.Xaml.Controls.Control.IsPointerOverProperty.DefaultValue!);
        Assert.AreSame(typeof(VeloxDev.WinForms.Xaml.Controls.Control), VeloxDev.WinForms.Xaml.Controls.Control.FocusableProperty.OwnerType);
    }

    [TestMethod]
    public void IInputElement_IsImplementedByEveryBaseControl()
    {
        using var control = new VeloxDev.WinForms.Xaml.Controls.Control();
        using var panel = new VeloxDev.WinForms.Xaml.Controls.Panel();
        using var scrollable = new VeloxDev.WinForms.Xaml.Controls.ScrollableControl();
        using var canvas = new VeloxDev.WinForms.Xaml.Controls.Canvas();
        using var view = new VeloxDev.WinForms.Xaml.Controls.UserControl();

        Assert.IsInstanceOfType<IInputElement>(control);
        Assert.IsInstanceOfType<IInputElement>(panel);
        Assert.IsInstanceOfType<IInputElement>(scrollable);
        Assert.IsInstanceOfType<IInputElement>(canvas);
        Assert.IsInstanceOfType<IInputElement>(view);

        Assert.IsTrue(scrollable.AutoScroll, "the scrollable base control enables AutoScroll");
        Assert.IsInstanceOfType<System.Windows.Forms.ScrollableControl>(panel);
    }

    [TestMethod]
    public void PointerCapture_IsTakenAndReleasedThroughTheNativeCapture()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);

        var captured = control.CapturePointer();
        Assert.AreEqual(captured, control.Capture, "CapturePointer reports the native capture state");

        control.ReleasePointerCapture();
        Assert.IsFalse(control.Capture);

        if (captured)
        {
            Assert.IsTrue(control.CapturePointer());
            control.SimulateMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0));
            Assert.IsFalse(control.Capture, "a pointer release must return the capture");
        }
    }

    [TestMethod]
    public void Control_StillBehavesAsAWinFormsControlWithChildren()
    {
        using var host = new VeloxDev.WinForms.Xaml.Controls.Control();
        using var child = new Label { Text = "child", Location = new Point(4, 5) };

        host.Controls.Add(child);
        XamlTestSurface.EnsureHandle(host);

        Assert.AreSame(host, child.Parent);
        Assert.HasCount(1, host.Controls);
        Assert.AreEqual(new Point(4, 5), child.Location);
    }
}
