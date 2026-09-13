namespace VeloxDev.TransitionSystem;

/// <summary>
/// Marks an index argument in a transition path, e.g. <c>Property(x =&gt; x.Items[PathIndex.Frozen(i)].Width, end)</c>.
/// </summary>
/// <remarks>
/// An index argument that can change while the animation runs — a captured local, or a property of the target such
/// as <c>x.SelectedIndex</c> — is re-evaluated on <b>every frame</b> by default, so the path follows it. The end
/// value, though, was read once when the animation started, so a path that moves mid-flight writes an end value
/// computed against the slot it started on. <see cref="Frozen{T}"/> pins the index to one slot instead, which is what
/// you want whenever the end value must land where it was read from.
/// <para>
/// The call never executes — the parser recognises it structurally and unwraps its argument — and it is part of the
/// path's identity, so <c>Items[i]</c> and <c>Items[Frozen(i)]</c> are two different paths.
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
