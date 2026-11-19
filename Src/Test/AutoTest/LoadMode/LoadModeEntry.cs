using VeloxDev.AT.Engine;

namespace VeloxDev.AT.LoadMode;

/// <summary>
/// One platform's load-mode target state: which payload fields describe the three shapes, and what they read
/// before anything is loaded.
/// </summary>
/// <remarks>
/// The declared initial values are the point of this type. Asserting that a reset restored the target by comparing it
/// against <em>what the reset itself produced</em> would be circular; the expectation has to be held here, written
/// from the demo's own declared start state, independently of the code under test.
/// </remarks>
/// <param name="Platform">The platform this describes.</param>
/// <param name="Initial">
/// The fields that make up the three targets' observable state, in report order, with the value each must have at
/// rest. Values that parse as numbers are compared with a tolerance; the rest are compared exactly.
/// </param>
internal sealed record LoadModeEntry(string Platform, IReadOnlyDictionary<string, string> Initial)
{
    /// <summary>
    /// An extra claim to check **while the shapes are in flight**, or null for none. Returns the failure text, or null
    /// when it holds.
    /// </summary>
    /// <remarks>
    /// The cases in the suite are deliberately generic — "it moves", "it stops", "reset restores" — and those hold
    /// whatever the animation is doing to the values. An invariant is where a platform states something sharper that
    /// only its own rendering can satisfy, such as "the brush this produces must be one that paints".
    /// </remarks>
    public Func<StatePayload, string?>? InFlight { get; init; }
}
