namespace VeloxDev.TransitionSystem;

/// <summary>
/// Declares which members of this type are animatable (one level, not recursive).
/// When a property's type has no registered sampler, discovery/capture calls this method and expands the declared
/// members into member paths (e.g. target.Foo.Bar) to resolve samplers. Complex composite types
/// (Transform/Transition, etc.) should use a dedicated <see cref="ISampler"/> that handles decomposition,
/// normalization and interpolation internally — they do not need to implement this interface.
/// </summary>
public interface ISampleable
{
    /// <summary>
    /// Returns the animatable members of this type (paths relative to this type, one level, not recursive).
    /// Prefer declaring them with <c>TransitionProperty.Members&lt;Foo&gt;(f =&gt; f.Bar, ...)</c>.
    /// </summary>
    IReadOnlyList<ITransitionProperty> GetAnimatableMembers();
}
