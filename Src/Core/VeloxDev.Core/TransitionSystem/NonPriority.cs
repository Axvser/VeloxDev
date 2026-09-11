namespace VeloxDev.TransitionSystem;

/// <summary>
/// The priority type of an adapter whose host dispatcher has none: filled in as the priority type argument of
/// <c>TransitionCore&lt;...&gt;</c>, <c>TransitionSchedulerCore&lt;...&gt;</c> and the effect, inspector and
/// interpreter bases.
/// </summary>
/// <remarks>
/// An empty struct on purpose, because this type is never instantiated and carries no state — it only fills a type
/// parameter. A struct is the cheapest thing that can do that: no instance to pass, no allocation for the type
/// argument, and <c>default(NonPriority)</c> is a real value, so it flows through the existing
/// <c>is TPriorityCore</c> checks unchanged.
/// <para>
/// It exists so the priority-aware and priority-free adapters share one set of generic bases instead of two
/// near-identical families. A priority-free host keeps sampling on the priority-free path
/// (<c>frameSet.Apply(target, easedT)</c>), so the marker costs nothing per frame.
/// </para>
/// </remarks>
public readonly struct NonPriority
{
}
