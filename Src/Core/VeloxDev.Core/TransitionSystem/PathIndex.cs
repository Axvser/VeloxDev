namespace VeloxDev.TransitionSystem;

/// <summary>
/// Marks an index argument in a transition path, e.g. <c>Property(x =&gt; x.Items[PathIndex.Frozen(i)].Width, end)</c>.
/// </summary>
/// <remarks>
/// An index argument that reads something able to change while the animation runs — a captured local, a property of
/// the target such as <c>x.SelectedIndex</c> — is re-evaluated on <b>every frame</b> by default, so the path follows
/// it: change the index and the remaining frames are written to the newly selected slot.
/// <para>
/// That default has one consequence worth knowing before relying on it. The end value is read once, when the
/// animation starts, so a path that moves mid-flight writes an end value computed against the slot it started on.
/// Wrapping the argument in <see cref="Frozen{T}"/> pins it instead: the index is evaluated once at start-up and the
/// whole animation is anchored to that slot, which is what you want whenever the end value must land where it was
/// read from.
/// </para>
/// <para>
/// The call itself never executes — the parser recognises it structurally and unwraps its argument, so it is a
/// marker rather than a value transform. Because the marker is part of the path's identity —
/// <c>Items[i]</c> and <c>Items[Frozen(i)]</c> are two different paths, not one.
/// </para>
/// <para>
/// A constant argument needs no marker: <c>Items[0]</c> cannot drift, and is treated as frozen whatever it is
/// written as.
/// </para>
/// </remarks>
public static class PathIndex
{
    /// <summary>
    /// Pins the index argument <paramref name="value"/> for the whole animation: it is evaluated once when the
    /// animation starts rather than on every frame. Never executes — the transition parser recognises the call and
    /// unwraps it.
    /// </summary>
    public static T Frozen<T>(T value) => value;
}
