namespace VeloxDev.TransitionSystem
{
    /// <summary>
    /// One animatable value addressed by a path: the accessor a sampler reads and writes through, and the identity
    /// that a transition's state keys it by.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. A path may end in an array element or an indexer, and neither has a
    /// <c>PropertyInfo</c> to expose — an array element has none at all, and every indexer on a type reports the
    /// same <c>Item</c> member, so it could not tell <c>Items[0]</c> from <c>Items[1]</c> even where a
    /// <c>PropertyInfo</c> exists. What a consumer needs is what it can do with the value, not how the path was
    /// spelled.
    /// </remarks>
    public interface ITransitionProperty
    {
        /// <summary>The path's text, for diagnostics only — it is not the identity, which is the object's equality.</summary>
        string Path { get; }

        /// <summary>The type of the value at the end of the path.</summary>
        Type PropertyType { get; }

        /// <summary>True when every segment of the path can be read.</summary>
        bool CanRead { get; }

        /// <summary>True when the last segment can be written.</summary>
        bool CanWrite { get; }

        /// <summary>
        /// Reads the current value from <paramref name="target"/>, or <c>null</c> when an intermediate object on the
        /// path is null. A path that does not match the target's runtime type is reported through the transition's
        /// own sentinel rather than by throwing, so the caller can skip the property instead of distorting the
        /// interpolation.
        /// </summary>
        object? GetValue(object? target);

        /// <summary>Writes <paramref name="value"/> to <paramref name="target"/>. False when the path does not apply to it.</summary>
        bool SetValue(object target, object? value);
    }
}
