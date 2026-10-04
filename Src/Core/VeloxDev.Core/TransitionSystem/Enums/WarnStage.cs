namespace VeloxDev.TransitionSystem;

/// <summary>Which step degraded without failing the run. Reported through <see cref="ITransitionEffectCore.Warn"/>;
/// each value is reported at most once per stage and instance.</summary>
public enum WarnStage
{
    /// <summary>A bound path could not be read off the target.</summary>
    Unreadable,

    /// <summary>The value at a bound path has no sampler.</summary>
    Unsampled,

    /// <summary>The host refused a frame or a dispatch; the animation carried on without it.</summary>
    Dropped,
}
