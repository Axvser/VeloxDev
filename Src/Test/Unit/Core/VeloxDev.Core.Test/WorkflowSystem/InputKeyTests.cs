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

    [TestMethod]
    public void ModifierKeys_AreNamedAndStayOutOfTheArithmeticRuns()
    {
        // 适配器把平台的字母 / 数字 / 功能键按**差值**映射过来，靠的就是这三段连续；一个修饰键要是插进
        // 任何一段里，那家的 ToKey 就会把某个字母报成 LeftShift —— 构建照绿，只有按键悄悄错位。
        var modifiers = new[]
        {
            InputKey.LeftShift, InputKey.RightShift,
            InputKey.LeftCtrl, InputKey.RightCtrl,
            InputKey.LeftAlt, InputKey.RightAlt,
            InputKey.LWin, InputKey.RWin,
        };

        Assert.AreEqual(modifiers.Length, modifiers.Distinct().Count(), "八个修饰键各占一个值");
        CollectionAssert.DoesNotContain(modifiers, InputKey.Unknown);

        foreach (var key in modifiers)
        {
            Assert.IsFalse(key >= InputKey.A && key <= InputKey.Z, $"{key} 落在字母段里了");
            Assert.IsFalse(key >= InputKey.D0 && key <= InputKey.D9, $"{key} 落在数字段里了");
            Assert.IsFalse(key >= InputKey.F1 && key <= InputKey.F12, $"{key} 落在功能键段里了");
        }

        Assert.AreEqual(25, (int)InputKey.Z - (int)InputKey.A);
        Assert.AreEqual(9, (int)InputKey.D9 - (int)InputKey.D0);
        Assert.AreEqual(11, (int)InputKey.F12 - (int)InputKey.F1);
    }
}
