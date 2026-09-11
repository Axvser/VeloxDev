namespace VeloxDev.TransitionSystem;

/// <summary>
/// Thrown when a transition declares a path that can never animate: its leaf is a reference type with no sampler,
/// so there is nothing to interpolate with.
/// </summary>
/// <remarks>
/// A reference type is never assembled from its members the way a struct is, so a whole-object path over one
/// animates nothing. Express it member by member instead (<c>Property(x =&gt; x.Foo.Bar, end)</c>), or register a
/// dedicated <c>ISampler</c> for the type. The declaration is rejected rather than silently animating nothing.
/// <para>
/// Raised when the transition runs rather than while it is being built: that is the first moment every path, every
/// interpolator and every sampler registered by the adapter is known, so a path that only looks unsampleable until
/// its interpolator is declared is not rejected by mistake.
/// </para>
/// </remarks>
public sealed class TransitionPathUnsampleableException : Exception
{
    public TransitionPathUnsampleableException(ITransitionProperty property)
        : base($"'{property.Path}' cannot be animated: {property.PropertyType.Name} has no sampler and is a reference type, so it is not assembled from its members.")
    {
        Property = property;
    }

    /// <summary>The path that cannot be animated.</summary>
    public ITransitionProperty Property { get; }
}
