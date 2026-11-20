using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The WinForms-native input translation layer: pointer/key data, ancestor bubbling with Handled, native-control
/// bridges, duplicate suppression and cleanup.
/// </summary>
[STATestClass]
public sealed class InputEventTests
{
    [TestMethod]
    public void PointerPressedMovedReleased_TranslateMouseData()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);
        var modifiers = System.Windows.Forms.Control.ModifierKeys;

        PointerPressedEventArgs? pressed = null;
        PointerMovedEventArgs? moved = null;
        PointerReleasedEventArgs? released = null;
        control.PointerPressed += (_, e) => pressed = e;
        control.PointerMoved += (_, e) => moved = e;
        control.PointerReleased += (_, e) => released = e;

        control.SimulateMouseDown(new MouseEventArgs(MouseButtons.Left, 2, 10, 20, 0));
        control.SimulateMouseMove(new MouseEventArgs(MouseButtons.None, 0, 30, 40, 0));
        control.SimulateMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 30, 40, 0));

        Assert.IsNotNull(pressed);
        Assert.AreEqual(new Point(10, 20), pressed.Position);
        Assert.AreEqual(MouseButtons.Left, pressed.Button);
        Assert.AreEqual(2, pressed.ClickCount);
        Assert.AreSame(control, pressed.Source);
        Assert.AreEqual(modifiers, pressed.Modifiers);

        Assert.IsNotNull(moved);
        Assert.AreEqual(new Point(30, 40), moved.Position);
        Assert.AreSame(control, moved.Source);

        Assert.IsNotNull(released);
        Assert.AreEqual(MouseButtons.Left, released.Button);
        Assert.AreSame(control, released.Source);
    }

    [TestMethod]
    public void PointerEnteredExitedAndWheel_TranslateMouseData()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);

        PointerEnteredEventArgs? entered = null;
        PointerExitedEventArgs? exited = null;
        PointerWheelEventArgs? wheel = null;
        control.PointerEntered += (_, e) => entered = e;
        control.PointerExited += (_, e) => exited = e;
        control.PointerWheelChanged += (_, e) => wheel = e;

        control.SimulateMouseEnter();
        control.SimulateMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 10, 10, 120));
        control.SimulateMouseLeave();

        Assert.IsNotNull(entered);
        Assert.AreSame(control, entered.Source);

        Assert.IsNotNull(wheel);
        Assert.AreEqual(120, wheel.Delta);
        Assert.AreEqual(120, wheel.DeltaY);
        Assert.AreEqual(0, wheel.DeltaX);

        Assert.IsNotNull(exited);
        Assert.AreSame(control, exited.Source);
    }

    [TestMethod]
    public void KeyboardInputAbstraction_TranslateAndDetectRepeat()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);

        var downs = new List<KeyInputEventArgs>();
        var ups = new List<KeyInputEventArgs>();
        control.KeyInputDown += (_, e) => downs.Add(e);
        control.KeyInputUp += (_, e) => ups.Add(e);

        control.SimulateKeyDown(new KeyEventArgs(Keys.A));
        control.SimulateKeyDown(new KeyEventArgs(Keys.A));
        control.SimulateKeyUp(new KeyEventArgs(Keys.A));
        control.SimulateKeyDown(new KeyEventArgs(Keys.Control | Keys.A));

        Assert.HasCount(3, downs);
        Assert.AreEqual(Keys.A, downs[0].KeyCode);
        Assert.IsFalse(downs[0].IsRepeat);
        Assert.IsTrue(downs[1].IsRepeat);
        Assert.AreEqual(Keys.Control, downs[2].Modifiers);
        Assert.IsFalse(downs[2].IsRepeat);

        Assert.HasCount(1, ups);
        Assert.AreEqual(Keys.A, ups[0].KeyCode);
        Assert.AreSame(control, ups[0].Source);
    }

    [TestMethod]
    public void PointerEvents_BubbleToAncestorsAndHandledStopsBubbling()
    {
        using var parent = new InputProbeControl();
        using var child = new InputProbeControl();
        parent.Controls.Add(child);
        XamlTestSurface.EnsureHandle(child);

        var parentMoves = 0;
        var childMoves = 0;
        parent.PointerMoved += (_, e) =>
        {
            parentMoves++;
            Assert.AreSame(child, e.Source);
        };
        child.PointerMoved += (_, e) =>
        {
            childMoves++;
            Assert.AreSame(child, e.Source);
        };

        child.SimulateMouseMove(new MouseEventArgs(MouseButtons.None, 0, 1, 2, 0));
        Assert.AreEqual(1, childMoves);
        Assert.AreEqual(1, parentMoves);

        var parentPressed = 0;
        var childPressed = 0;
        parent.PointerPressed += (_, _) => parentPressed++;
        child.PointerPressed += (_, e) =>
        {
            childPressed++;
            e.Handled = true;
        };

        child.SimulateMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 1, 2, 0));
        Assert.AreEqual(1, childPressed);
        Assert.AreEqual(0, parentPressed);
    }

    [TestMethod]
    public void KeyInputEvents_BubbleAndHandledStopsBubbling()
    {
        using var parent = new InputProbeControl();
        using var child = new InputProbeControl();
        parent.Controls.Add(child);
        XamlTestSurface.EnsureHandle(child);

        var parentDowns = 0;
        var childDowns = 0;
        parent.KeyInputDown += (_, _) => parentDowns++;
        child.KeyInputDown += (_, e) =>
        {
            childDowns++;
            e.Handled = true;
        };

        child.SimulateKeyDown(new KeyEventArgs(Keys.B));

        Assert.AreEqual(1, childDowns);
        Assert.AreEqual(0, parentDowns);
    }

    [TestMethod]
    public void NativeKeyDown_IsNotShadowedByTheKeyboardInputAbstraction()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);

        var nativeDowns = 0;
        var inputDowns = 0;
        control.KeyDown += (_, e) =>
        {
            _ = e.SuppressKeyPress;
            nativeDowns++;
        };
        control.KeyInputDown += (_, _) => inputDowns++;

        control.SimulateKeyDown(new KeyEventArgs(Keys.A));

        Assert.AreEqual(1, nativeDowns, "the native KeyDown event must still be raised");
        Assert.AreEqual(1, inputDowns, "the explicit KeyInputDown abstraction is separate");
    }

    [TestMethod]
    public void GetPosition_ConvertsBetweenAncestorsAndDescendants()
    {
        using var parent = new InputProbeControl();
        using var child = new InputProbeControl();
        parent.Controls.Add(child);
        child.Location = new Point(50, 60);
        XamlTestSurface.EnsureHandle(parent);
        XamlTestSurface.EnsureHandle(child);

        PointerPressedEventArgs? pressed = null;
        child.PointerPressed += (_, e) => pressed = e;
        child.SimulateMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 10, 20, 0));

        Assert.IsNotNull(pressed);
        Assert.AreEqual(new Point(10, 20), pressed.GetPosition(child));
        Assert.AreEqual(new Point(60, 80), pressed.GetPosition(parent));
    }

    [TestMethod]
    public void NativeBridge_TranslatesDeduplicatesAndBubbles()
    {
        using var host = new InputProbeControl();
        using var button = new InputProbeButton();
        host.Controls.Add(button);
        XamlTestSurface.EnsureHandle(button);

        var bridgeCalls = 0;
        PointerPressedEventArgs? bridged = null;
        var hostCalls = 0;
        PointerPressedEventArgs? bubbled = null;

        EventHandler<PointerPressedEventArgs> handler = (_, e) =>
        {
            bridgeCalls++;
            bridged = e;
        };

        XamlInput.AddPointerPressed(button, handler);
        XamlInput.AddPointerPressed(button, handler);
        host.PointerPressed += (_, e) =>
        {
            hostCalls++;
            bubbled = e;
        };

        button.SimulateMouseDown(new MouseEventArgs(MouseButtons.Right, 1, 5, 6, 0));

        Assert.AreEqual(1, bridgeCalls, "the same bridge handler must not be registered twice");
        Assert.AreEqual(1, hostCalls);
        Assert.IsNotNull(bridged);
        Assert.IsNotNull(bubbled);
        Assert.AreSame(button, bridged.Source);
        Assert.AreSame(button, bubbled.Source);
        Assert.AreEqual(MouseButtons.Right, bridged.Button);
        Assert.AreEqual(new Point(5, 6), bridged.Position);

        XamlInput.Remove(button);
        button.SimulateMouseDown(new MouseEventArgs(MouseButtons.Right, 1, 5, 6, 0));

        Assert.AreEqual(1, bridgeCalls);
        Assert.AreEqual(1, hostCalls, "removing the bridge must stop translation and bubbling");
    }

    [TestMethod]
    public void NativeBridge_TranslatesKeyboardInputRepeatAndBubbles()
    {
        using var host = new InputProbeControl();
        using var button = new InputProbeButton();
        host.Controls.Add(button);
        XamlTestSurface.EnsureHandle(button);

        var downs = new List<KeyInputEventArgs>();
        var ups = new List<KeyInputEventArgs>();
        var hostDowns = 0;
        XamlInput.AddKeyInputDown(button, (_, e) => downs.Add(e));
        XamlInput.AddKeyInputUp(button, (_, e) => ups.Add(e));
        host.KeyInputDown += (_, _) => hostDowns++;

        button.SimulateKeyDown(new KeyEventArgs(Keys.A));
        button.SimulateKeyDown(new KeyEventArgs(Keys.A));
        button.SimulateKeyUp(new KeyEventArgs(Keys.A));

        Assert.HasCount(2, downs);
        Assert.IsFalse(downs[0].IsRepeat);
        Assert.IsTrue(downs[1].IsRepeat);
        Assert.AreSame(button, downs[0].Source);
        Assert.HasCount(1, ups);
        Assert.AreEqual(2, hostDowns);
    }

    [TestMethod]
    public void NativeBridge_EnterLeaveAndWheelTranslate()
    {
        using var host = new InputProbeControl();
        using var button = new InputProbeButton();
        host.Controls.Add(button);
        XamlTestSurface.EnsureHandle(button);

        var entered = 0;
        var exited = 0;
        var wheel = 0;
        XamlInput.AddPointerEntered(button, (_, _) => entered++);
        XamlInput.AddPointerExited(button, (_, _) => exited++);
        XamlInput.AddPointerWheelChanged(button, (_, e) => wheel = e.Delta);

        button.SimulateMouseEnter();
        button.SimulateMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 1, 2, -120));
        button.SimulateMouseLeave();

        Assert.AreEqual(1, entered);
        Assert.AreEqual(1, exited);
        Assert.AreEqual(-120, wheel);
    }

    [TestMethod]
    public void NativeBridge_DisposeRemovesTheBridge()
    {
        var button = new InputProbeButton();
        XamlTestSurface.EnsureHandle(button);
        var calls = 0;
        XamlInput.AddPointerPressed(button, (_, _) => calls++);
        button.SimulateMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0));
        Assert.AreEqual(1, calls);

        button.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(
            () => XamlInput.AddPointerPressed(button, (_, _) => calls++));
        XamlInput.Remove(button);
    }

    [TestMethod]
    public void RaiseHelpers_ProduceCustomEventsWithoutNativeInput()
    {
        using var control = new InputProbeControl();
        XamlTestSurface.EnsureHandle(control);

        PointerMovedEventArgs? moved = null;
        KeyInputEventArgs? keyDown = null;
        control.PointerMoved += (_, e) => moved = e;
        control.KeyInputDown += (_, e) => keyDown = e;

        XamlInput.RaisePointerMoved(control, new Point(7, 8));
        XamlInput.RaiseKeyInputDown(control, Keys.Space, Keys.Shift, isRepeat: true);

        Assert.IsNotNull(moved);
        Assert.AreEqual(new Point(7, 8), moved.Position);
        Assert.AreSame(control, moved.Source);

        Assert.IsNotNull(keyDown);
        Assert.AreEqual(Keys.Space, keyDown.KeyCode);
        Assert.AreEqual(Keys.Shift, keyDown.Modifiers);
        Assert.IsTrue(keyDown.IsRepeat);
        Assert.AreSame(control, keyDown.Source);
    }
}
