namespace VeloxDev.TransitionSystem;

/// <summary>The event args a transition reports for an outcome that carries a payload — a degraded step's message,
/// or the exception that failed the run. <typeparamref name="TStage"/> names which step, so a subscriber can tell
/// two failures of the same event apart without parsing a string.</summary>
/// <typeparam name="TStage">The enum naming the step that reported this.</typeparam>
/// <typeparam name="TValue">What that step produced — the message for <see cref="ITransitionEffectCore.Warn"/>, the
/// exception for <see cref="ITransitionEffectCore.Error"/>.</typeparam>
public sealed class TransitionEventArgs<TStage, TValue> : TransitionEventArgs
    where TStage : struct, Enum
{
    /// <summary>Which step reported this.</summary>
    public TStage Stage { get; init; }

    /// <summary>What that step produced; <see langword="null"/> when the step had nothing to hand over.</summary>
    public TValue? Value { get; init; }
}
