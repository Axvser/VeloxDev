namespace VeloxDev.TransitionSystem;

/// <summary>Which step failed the run. Reported through <see cref="ITransitionEffectCore.Error"/>, whose
/// <c>Value</c> is the escaped exception. The first five values are the engine's own steps; the rest say that a
/// subscriber of that event threw.</summary>
public enum ErrorStage
{
    /// <summary>Sampling a bound property threw.</summary>
    Sampling,

    /// <summary>The segment's own run loop threw.</summary>
    Run,

    /// <summary>Marshaling the eased value onto the target threw.</summary>
    Marshaling,

    /// <summary>The scheduler's awake step threw.</summary>
    Awake,

    /// <summary>Preparing the run threw.</summary>
    Prepare,

    /// <summary>A subscriber of <see cref="ITransitionEffectCore.Start"/> threw.</summary>
    Start,

    /// <summary>A subscriber of <see cref="ITransitionEffectCore.Update"/> threw.</summary>
    Update,

    /// <summary>A subscriber of <see cref="ITransitionEffectCore.LateUpdate"/> threw.</summary>
    LateUpdate,

    /// <summary>A subscriber of <see cref="ITransitionEffectCore.Completed"/> threw.</summary>
    Completed,

    /// <summary>A subscriber of <see cref="ITransitionEffectCore.Canceled"/> threw.</summary>
    Canceled,

    /// <summary>A subscriber of <see cref="ITransitionEffectCore.Finally"/> threw.</summary>
    Finally,
}
