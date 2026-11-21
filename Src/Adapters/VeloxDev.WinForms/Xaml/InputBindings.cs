using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;

namespace VeloxDev.WinForms.Xaml;

/// <summary>The modifier keys a gesture requires, matching the WPF spelling.</summary>
/// <remarks>
/// WinForms reports only <see cref="Alt"/>, <see cref="Control"/> and <see cref="Shift"/> through
/// <see cref="System.Windows.Forms.Control.ModifierKeys"/>; a gesture asking for <see cref="Windows"/> therefore
/// never matches keyboard input.
/// </remarks>
[Flags]
public enum ModifierKeys
{
    /// <summary>No modifier key.</summary>
    None = 0,

    /// <summary>The Alt key.</summary>
    Alt = 1,

    /// <summary>The Control key.</summary>
    Control = 2,

    /// <summary>The Shift key.</summary>
    Shift = 4,

    /// <summary>The Windows key.</summary>
    Windows = 8,
}

/// <summary>The mouse action a <see cref="MouseGesture"/> requires.</summary>
public enum MouseAction
{
    /// <summary>No action; the gesture never matches.</summary>
    None = 0,

    /// <summary>A single press of the left button.</summary>
    LeftClick,

    /// <summary>A single press of the right button.</summary>
    RightClick,

    /// <summary>A single press of the middle button.</summary>
    MiddleClick,

    /// <summary>A press of the wheel, which WinForms reports as the middle button.</summary>
    WheelClick,

    /// <summary>A double press of the left button.</summary>
    LeftDoubleClick,

    /// <summary>A double press of the right button.</summary>
    RightDoubleClick,

    /// <summary>A double press of the middle button.</summary>
    MiddleDoubleClick,

    /// <summary>A double press of the wheel, which WinForms reports as the middle button.</summary>
    WheelDoubleClick,
}

/// <summary>A key and the modifier keys held with it.</summary>
/// <remarks>
/// <see cref="Key"/> is a key code without modifiers; the modifier state lives in <see cref="Modifiers"/>. A gesture
/// matches only when the pressed key and the modifier set both match exactly, so an extra Shift stops a Ctrl+S
/// gesture from firing.
/// </remarks>
public sealed class KeyGesture
{
    /// <summary>Creates an empty gesture that never matches.</summary>
    public KeyGesture()
    {
    }

    /// <summary>Creates a gesture for <paramref name="key"/> plus <paramref name="modifiers"/>.</summary>
    /// <param name="key">The key code without modifiers.</param>
    /// <param name="modifiers">The modifier keys required with the key.</param>
    public KeyGesture(Keys key, ModifierKeys modifiers = ModifierKeys.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>The key code without modifiers; <see cref="Keys.None"/> means the gesture never matches.</summary>
    public Keys Key { get; set; }

    /// <summary>The modifier keys required with <see cref="Key"/>.</summary>
    public ModifierKeys Modifiers { get; set; }

    /// <summary>Whether <paramref name="keyCode"/> was pressed with exactly <paramref name="modifiers"/>.</summary>
    /// <param name="keyCode">The pressed key code, as reported by WinForms.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    public bool Matches(Keys keyCode, Keys modifiers)
        => Key != Keys.None
        && (keyCode & Keys.KeyCode) == (Key & Keys.KeyCode)
        && Modifiers == ModifierKeyMapping.FromKeys(modifiers);

    /// <summary>Returns the gesture as text, for example <c>Control+S</c>.</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & ModifierKeys.Control) != 0) parts.Add("Control");
        if ((Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & ModifierKeys.Windows) != 0) parts.Add("Windows");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}

/// <summary>A mouse action and the modifier keys held with it.</summary>
/// <remarks>
/// A single-click action matches every press with one or more clicks; a double-click action matches only the second
/// press of a pair. Double-click detection therefore relies on WinForms' click count.
/// </remarks>
public sealed class MouseGesture
{
    /// <summary>Creates an empty gesture that never matches.</summary>
    public MouseGesture()
    {
    }

    /// <summary>Creates a gesture for <paramref name="action"/> plus <paramref name="modifiers"/>.</summary>
    /// <param name="action">The mouse action required.</param>
    /// <param name="modifiers">The modifier keys required with the action.</param>
    public MouseGesture(MouseAction action, ModifierKeys modifiers = ModifierKeys.None)
    {
        Action = action;
        Modifiers = modifiers;
    }

    /// <summary>The mouse action required; <see cref="MouseAction.None"/> means the gesture never matches.</summary>
    public MouseAction Action { get; set; }

    /// <summary>The modifier keys required with <see cref="Action"/>.</summary>
    public ModifierKeys Modifiers { get; set; }

    /// <summary>Whether <paramref name="button"/> was pressed with exactly <paramref name="modifiers"/>.</summary>
    /// <param name="button">The pressed button, as reported by WinForms.</param>
    /// <param name="clickCount">The click count reported by WinForms.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    public bool Matches(MouseButtons button, int clickCount, Keys modifiers)
    {
        if (Action == MouseAction.None || clickCount < 1 || Modifiers != ModifierKeyMapping.FromKeys(modifiers))
        {
            return false;
        }

        if (IsDoubleClick(Action) && clickCount < 2)
        {
            return false;
        }

        return ToButton(Action) == button;
    }

    /// <summary>Returns the gesture as text, for example <c>Control+LeftDoubleClick</c>.</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & ModifierKeys.Control) != 0) parts.Add("Control");
        if ((Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & ModifierKeys.Windows) != 0) parts.Add("Windows");
        parts.Add(Action.ToString());
        return string.Join("+", parts);
    }

    // 滚轮按下由 WinForms 报成鼠标中键，两者落到同一个按键。
    private static MouseButtons ToButton(MouseAction action) => action switch
    {
        MouseAction.LeftClick or MouseAction.LeftDoubleClick => MouseButtons.Left,
        MouseAction.RightClick or MouseAction.RightDoubleClick => MouseButtons.Right,
        MouseAction.MiddleClick
            or MouseAction.WheelClick
            or MouseAction.MiddleDoubleClick
            or MouseAction.WheelDoubleClick => MouseButtons.Middle,
        _ => MouseButtons.None,
    };

    private static bool IsDoubleClick(MouseAction action) => action is
        MouseAction.LeftDoubleClick
        or MouseAction.RightDoubleClick
        or MouseAction.MiddleDoubleClick
        or MouseAction.WheelDoubleClick;
}

/// <summary>Binds an input gesture to a command.</summary>
/// <remarks>
/// The parameter is passed unchanged to <see cref="ICommand.CanExecute"/> and <see cref="ICommand.Execute"/>, so a
/// binding can be reused with a different parameter by replacing <see cref="CommandParameter"/>.
/// </remarks>
public abstract class InputBinding
{
    /// <summary>The command executed when the gesture matches; <see langword="null"/> means the binding never executes.</summary>
    public ICommand? Command { get; set; }

    /// <summary>The value passed to <see cref="ICommand.CanExecute"/> and <see cref="ICommand.Execute"/>.</summary>
    public object? CommandParameter { get; set; }

    /// <summary>Whether this binding matches a pressed key.</summary>
    /// <param name="keyCode">The pressed key code, as reported by WinForms.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <returns><see langword="false"/> by default; <see cref="KeyBinding"/> supplies the key match.</returns>
    public virtual bool MatchesKey(Keys keyCode, Keys modifiers) => false;

    /// <summary>Whether this binding matches a pressed mouse button.</summary>
    /// <param name="button">The pressed button, as reported by WinForms.</param>
    /// <param name="clickCount">The click count reported by WinForms.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <returns><see langword="false"/> by default; <see cref="MouseBinding"/> supplies the mouse match.</returns>
    public virtual bool MatchesMouse(MouseButtons button, int clickCount, Keys modifiers) => false;

    /// <summary>Executes <see cref="Command"/> when it is present and <see cref="ICommand.CanExecute"/> allows it.</summary>
    /// <returns><see langword="true"/> when the command ran.</returns>
    public bool TryExecute()
    {
        var command = Command;
        if (command is null)
        {
            return false;
        }

        var parameter = CommandParameter;
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }
}

/// <summary>Binds a <see cref="KeyGesture"/> to a command.</summary>
/// <remarks><see cref="Key"/> and <see cref="Modifiers"/> edit <see cref="Gesture"/> directly, so XAML can write either the gesture object or its two parts.</remarks>
public sealed class KeyBinding : InputBinding
{
    private KeyGesture? _gesture;

    /// <summary>The gesture to match; <see langword="null"/> means the binding never matches.</summary>
    public KeyGesture? Gesture
    {
        get => _gesture;
        set => _gesture = value;
    }

    /// <summary>The key code the gesture requires; setting it creates the gesture on demand.</summary>
    public Keys Key
    {
        get => _gesture?.Key ?? Keys.None;
        set => EnsureGesture().Key = value;
    }

    /// <summary>The modifier keys the gesture requires; setting it creates the gesture on demand.</summary>
    public ModifierKeys Modifiers
    {
        get => _gesture?.Modifiers ?? ModifierKeys.None;
        set => EnsureGesture().Modifiers = value;
    }

    /// <inheritdoc />
    public override bool MatchesKey(Keys keyCode, Keys modifiers)
        => _gesture?.Matches(keyCode, modifiers) ?? false;

    private KeyGesture EnsureGesture() => _gesture ??= new KeyGesture();
}

/// <summary>Binds a <see cref="MouseGesture"/> to a command.</summary>
/// <remarks><see cref="Action"/> and <see cref="Modifiers"/> edit <see cref="Gesture"/> directly, so XAML can write either the gesture object or its two parts.</remarks>
public sealed class MouseBinding : InputBinding
{
    private MouseGesture? _gesture;

    /// <summary>The gesture to match; <see langword="null"/> means the binding never matches.</summary>
    public MouseGesture? Gesture
    {
        get => _gesture;
        set => _gesture = value;
    }

    /// <summary>The mouse action the gesture requires; setting it creates the gesture on demand.</summary>
    public MouseAction Action
    {
        get => _gesture?.Action ?? MouseAction.None;
        set => EnsureGesture().Action = value;
    }

    /// <summary>The modifier keys the gesture requires; setting it creates the gesture on demand.</summary>
    public ModifierKeys Modifiers
    {
        get => _gesture?.Modifiers ?? ModifierKeys.None;
        set => EnsureGesture().Modifiers = value;
    }

    /// <inheritdoc />
    public override bool MatchesMouse(MouseButtons button, int clickCount, Keys modifiers)
        => _gesture?.Matches(button, clickCount, modifiers) ?? false;

    private MouseGesture EnsureGesture() => _gesture ??= new MouseGesture();
}

/// <summary>The input bindings owned by a control, plus the matching helpers used by its input callbacks.</summary>
/// <remarks>
/// Bindings are checked in collection order. A matching binding whose command cannot execute is skipped, so the next
/// matching binding still gets its chance. Commands run synchronously on the calling (UI) thread.
/// </remarks>
public sealed class InputBindingCollection : Collection<InputBinding>
{
    /// <summary>Finds the first <see cref="KeyBinding"/> matching <paramref name="keyCode"/>.</summary>
    /// <param name="keyCode">The pressed key code.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <returns>The first matching binding, or <see langword="null"/>.</returns>
    public KeyBinding? FindKeyBinding(Keys keyCode, Keys modifiers)
    {
        foreach (var binding in this)
        {
            if (binding is KeyBinding key && key.MatchesKey(keyCode, modifiers))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>Finds the first <see cref="MouseBinding"/> matching <paramref name="button"/>.</summary>
    /// <param name="button">The pressed button.</param>
    /// <param name="clickCount">The click count reported by WinForms.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <returns>The first matching binding, or <see langword="null"/>.</returns>
    public MouseBinding? FindMouseBinding(MouseButtons button, int clickCount, Keys modifiers)
    {
        foreach (var binding in this)
        {
            if (binding is MouseBinding mouse && mouse.MatchesMouse(button, clickCount, modifiers))
            {
                return mouse;
            }
        }

        return null;
    }

    /// <summary>Executes the first key binding that matches and can execute.</summary>
    /// <param name="keyCode">The pressed key code.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <returns><see langword="true"/> when a command ran.</returns>
    public bool TryHandleKey(Keys keyCode, Keys modifiers)
    {
        // 命令执行期间可能改动集合本身，先快照再遍历。
        foreach (var binding in System.Linq.Enumerable.ToArray(this))
        {
            if (binding.MatchesKey(keyCode, modifiers) && binding.TryExecute())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Executes the first mouse binding that matches and can execute.</summary>
    /// <param name="button">The pressed button.</param>
    /// <param name="clickCount">The click count reported by WinForms.</param>
    /// <param name="modifiers">The modifier keys held at the time of the event.</param>
    /// <returns><see langword="true"/> when a command ran.</returns>
    public bool TryHandleMouse(MouseButtons button, int clickCount, Keys modifiers)
    {
        foreach (var binding in System.Linq.Enumerable.ToArray(this))
        {
            if (binding.MatchesMouse(button, clickCount, modifiers) && binding.TryExecute())
            {
                return true;
            }
        }

        return false;
    }
}

// WinForms 用 Keys 的修饰位表示修饰键，这里转成手势自己的枚举。
internal static class ModifierKeyMapping
{
    internal static ModifierKeys FromKeys(Keys keys)
    {
        var modifiers = ModifierKeys.None;
        if ((keys & Keys.Control) == Keys.Control) modifiers |= ModifierKeys.Control;
        if ((keys & Keys.Alt) == Keys.Alt) modifiers |= ModifierKeys.Alt;
        if ((keys & Keys.Shift) == Keys.Shift) modifiers |= ModifierKeys.Shift;
        return modifiers;
    }
}
