using VeloxDev.TransitionSystem;

namespace VeloxDev.WinForms.Xaml;

/// <summary>An action run by a <see cref="Trigger"/> when it enters or exits its active state.</summary>
public abstract class TriggerAction
{
    /// <summary>Runs the action against the trigger's owning control.</summary>
    /// <param name="control">The control the trigger belongs to.</param>
    public abstract void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control);
}

/// <summary>Runs several actions in declaration order when a trigger enters or exits.</summary>
public sealed class TriggerActionGroup : TriggerAction
{
    /// <summary>The actions to run, in order.</summary>
    public IList<TriggerAction> Actions { get; } = new List<TriggerAction>();

    /// <inheritdoc />
    public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        foreach (var action in Actions)
        {
            action?.Invoke(control);
        }
    }
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

    /// <summary>Additional full passes; zero runs once and <see cref="int.MaxValue"/> repeats forever.</summary>
    public int LoopTime { get; set; }

    /// <summary>Whether each pass also runs backwards.</summary>
    public bool IsAutoReverse { get; set; }

    /// <inheritdoc />
    public override void Invoke(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (Property is null)
        {
            throw new InvalidOperationException("A TransitionAction needs a Property.");
        }

        var effect = ResolveEffect();
        XamlTransition.Animate(control, Property, To, effect);
    }

    private TransitionEffect? ResolveEffect()
    {
        if (LoopTime == 0 && !IsAutoReverse)
        {
            return Effect;
        }

        return new TransitionEffect
        {
            Duration = Effect?.Duration ?? TimeSpan.FromMilliseconds(240),
            Ease = Effect?.Ease ?? Eases.Cubic.Out,
            FPS = Effect?.FPS ?? 60,
            LoopTime = LoopTime,
            IsAutoReverse = IsAutoReverse,
        };
    }
}
