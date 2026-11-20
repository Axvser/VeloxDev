using VeloxDev.TransitionSystem;

namespace VeloxDev.WinForms.Xaml;

/// <summary>An action run by a <see cref="Trigger"/> when it enters or exits its active state.</summary>
public abstract class TriggerAction
{
    /// <summary>Runs the action against the trigger's owning control.</summary>
    /// <param name="control">The control the trigger belongs to.</param>
    public abstract void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control);
}

/// <summary>
/// A trigger action that animates one dependency property to <see cref="To"/> through the VeloxDev transition
/// system's WinForms host and samplers.
/// </summary>
public sealed class TransitionAction : TriggerAction
{
    /// <summary>The dependency property to animate, or <see langword="null"/> while unconfigured.</summary>
    public DependencyProperty? Property { get; set; }

    /// <summary>The value the animation ends at.</summary>
    public object? To { get; set; }

    /// <summary>The timing and easing of the animation; omitted means a 240 ms ease-out.</summary>
    public TransitionEffect? Effect { get; set; }

    /// <inheritdoc />
    public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (Property is null)
        {
            throw new InvalidOperationException("A TransitionAction needs a Property.");
        }

        XamlTransition.Animate(control, Property, To, Effect);
    }
}
