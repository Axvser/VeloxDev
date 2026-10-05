using System;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The modifier keys held while an input event happened. Mirrors <c>Avalonia.Input.KeyModifiers</c>.
/// </summary>
/// <remarks>
/// Modifier state is always precise, even when <see cref="KeyEventArgs.Key"/> is
/// <see cref="InputKey.Unknown"/> — which is what makes a combination such as Ctrl + an unmapped key still
/// distinguishable to a subscriber.
/// </remarks>
[Flags]
public enum InputModifiers
{
    /// <summary>No modifier is held.</summary>
    None = 0,

    /// <summary>Alt is held.</summary>
    Alt = 1,

    /// <summary>Ctrl is held.</summary>
    Control = 2,

    /// <summary>Shift is held.</summary>
    Shift = 4,

    /// <summary>The platform's Meta key (Windows key, Command) is held.</summary>
    Meta = 8,
}
