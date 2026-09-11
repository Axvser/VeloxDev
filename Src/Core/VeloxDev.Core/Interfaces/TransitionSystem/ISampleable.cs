namespace VeloxDev.TransitionSystem;

/// <summary>
/// Declares how a composite <b>value type</b> is animated as a whole: a property whose type is a struct with no
/// registered sampler has its declared members interpolated individually and the struct reassembled every frame
/// through <see cref="CreateFrameValue"/>.
/// </summary>
/// <remarks>
/// Reference types do not go through this path — a value type is the only case where the whole value has to be
/// rebuilt from its members, because its members cannot be written back in place. A property holding a reference
/// type is animated either through explicit member paths (<c>Property(x =&gt; x.Foo.Bar, end)</c>) or by a dedicated
/// <see cref="ISampler"/> that handles decomposition, normalization and interpolation internally.
/// </remarks>
public interface ISampleable
{
    /// <summary>
    /// Returns the animatable members of this type (paths relative to this type, one level, not recursive), in the
    /// order <see cref="CreateFrameValue"/> expects them.
    /// Prefer declaring them with <c>TransitionProperty.Members&lt;Foo&gt;(f =&gt; f.Bar, ...)</c>.
    /// </summary>
    IReadOnlyList<ITransitionProperty> GetAnimatableMembers();

    /// <summary>
    /// Reconstructs a value from its interpolated members, in <see cref="GetAnimatableMembers"/> order.
    /// Implementations construct through their constructor (compile-time, zero reflection), e.g.
    /// <c>new Viewport((double)v[0], (double)v[1], (double)v[2], (double)v[3])</c>.
    /// </summary>
    object? CreateFrameValue(IReadOnlyList<object?> memberValues);
}
