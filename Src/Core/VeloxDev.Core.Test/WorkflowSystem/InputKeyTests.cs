using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Tests for the key half of the standard input: a key this build does not name still reaches a subscriber
/// distinguishably, and the modifier state is never approximated.
/// </summary>
[TestClass]
public class InputKeyTests : WorkflowInputTestBase
{
    [TestMethod]
    public void KeyDown_AnUnnamedKey_ReportsUnknownAndKeepsThePlatformCode()
    {
        // Core 只枚举图编辑器会绑的那些键；其余一律 Unknown，但平台码还在，宿主照样分得出来。
        var tree = TreeWith(ReadyLink(0, 0, 100, 0));
        var key = InputKey.Delete;
        var raw = 0;
        Events(tree).Input.KeyDown += (_, e) => { key = e.Key; raw = e.RawKeyCode; };

        WorkflowInput.For(tree).Route(new KeyDownEventArgs(
            InputKey.Unknown, 0x1234, InputModifiers.None, false, new SourceView(), null, new WorkflowEventHandle()));

        Assert.AreEqual(InputKey.Unknown, key);
        Assert.AreEqual(0x1234, raw);
    }

    [TestMethod]
    public void KeyDown_ModifiersStayPrecise_EvenForAnUnnamedKey()
    {
        var tree = TreeWith(ReadyLink(0, 0, 100, 0));
        var modifiers = InputModifiers.None;
        Events(tree).Input.KeyDown += (_, e) => modifiers = e.Modifiers;

        WorkflowInput.For(tree).Route(new KeyDownEventArgs(
            InputKey.Unknown, 0, InputModifiers.Control | InputModifiers.Shift, false, new SourceView(), null, new WorkflowEventHandle()));

        Assert.AreEqual(InputModifiers.Control | InputModifiers.Shift, modifiers);
    }

    [TestMethod]
    public void KeyUp_CarriesTheKeyAndReachesTheTarget()
    {
        var link = ReadyLink(0, 0, 100, 0);
        var tree = TreeWith(link);
        var key = InputKey.Unknown;
        Events(link).Input.KeyUp += (_, e) => key = e.Key;

        WorkflowInput.For(tree).Route(Up(InputKey.Escape, link));

        Assert.AreEqual(InputKey.Escape, key);
    }
}
