namespace VeloxDev.TransitionSystem;

/// <summary>
/// Thrown when a property path is added to a transition that already carries a path above or below it.
/// </summary>
/// <remarks>
/// One object must be expressed by exactly one path. With both a whole-object path and one of its sub-leaf paths
/// present, a whole-object sampler and a sub-leaf sampler would write the same object every frame and the result
/// would depend on the order they happen to run in — so the conflict is rejected instead of resolved silently.
/// This is raised while the transition is being defined, because it reports a mistake in the paths the author
/// wrote rather than a runtime condition; it therefore deliberately breaks the otherwise repo-wide convention of
/// swallowing property-access failures so the animation never breaks.
/// </remarks>
public sealed class TransitionPathConflictException : Exception
{
    public TransitionPathConflictException(ITransitionProperty existing, ITransitionProperty conflicting)
        : base($"'{conflicting.Path}' conflicts with '{existing.Path}': one object must be expressed by exactly one path.")
    {
        Existing = existing;
        Conflicting = conflicting;
    }

    /// <summary>The path that was already on the transition.</summary>
    public ITransitionProperty Existing { get; }

    /// <summary>The path that was rejected.</summary>
    public ITransitionProperty Conflicting { get; }
}
