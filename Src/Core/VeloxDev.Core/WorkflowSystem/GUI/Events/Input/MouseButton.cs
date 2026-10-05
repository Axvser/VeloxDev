namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Which pointer button an input event concerns. The spellings mirror <c>Avalonia.Input.MouseButton</c>; the wheel
/// is deliberately not a button here — it has its own event, <see cref="PointerWheelEventArgs"/>.
/// </summary>
/// <remarks>
/// <para>
/// A platform that cannot tell the buttons apart reports <see cref="None"/>, which is the only meaning of "no
/// button" an event can carry — a plain move, or a device whose press cannot be attributed.
/// </para>
/// <para>
/// The name carries the <c>Workflow</c> prefix because every adapter file imports both this namespace and a
/// platform's input namespace, where a plain <c>MouseButton</c> already exists.
/// </para>
/// </remarks>
public enum MouseButton
{
    /// <summary>No button is involved — a move, or a device that cannot say which button it was.</summary>
    None = 0,

    /// <summary>The primary button.</summary>
    Left = 1,

    /// <summary>The secondary button — the one a host usually opens its context menu from.</summary>
    Right = 2,

    /// <summary>The middle button.</summary>
    Middle = 3,

    /// <summary>The first extended button.</summary>
    XButton1 = 4,

    /// <summary>The second extended button.</summary>
    XButton2 = 5,
}
