#if NET

namespace VeloxDev.AspectOriented
{
    /// <summary>
    /// One stage of an aspect.
    /// </summary>
    /// <param name="parameters">
    /// The member's arguments, or <see langword="null"/> for a getter. A setter's single element is the value
    /// being written; a method's are its own arguments.
    /// </param>
    /// <param name="previous">What the preceding stage returned; <see langword="null"/> for <c>start</c>.</param>
    /// <returns>
    /// For <c>start</c>, the value handed to the next stage; for <c>coverage</c>, the member's result; for
    /// <c>end</c>, nothing — its return value is discarded.
    /// </returns>
    public delegate object? ProxyHandler(object?[]? parameters, object? previous);

    /// <summary>
    /// The three stages a generated proxy runs around one member.
    /// </summary>
    /// <param name="start">Runs before the member; its return value is passed on as the next stage's <c>previous</c>.</param>
    /// <param name="coverage">
    /// Replaces the member: when this is not <see langword="null"/> the member's own body does not run, and this
    /// stage's return value becomes the member's result.
    /// </param>
    /// <param name="end">Runs after the member; its return value is discarded.</param>
    /// <remarks>
    /// <para>
    /// A hook set is installed whole, so a member's three stages are always replaced together — there is no way
    /// to keep an existing stage and swap another. Passing <see langword="null"/> for the whole set clears the
    /// member's aspects.
    /// </para>
    /// <para>
    /// The handlers are boxed: <see cref="ProxyHandler"/> takes the arguments and the previous value as
    /// <see cref="object"/>, which is the contract the library has always had and is what keeps the generated
    /// proxies free of any reflection or runtime code generation.
    /// </para>
    /// </remarks>
    public sealed class AspectHooks(ProxyHandler? start, ProxyHandler? coverage, ProxyHandler? end)
    {
        /// <summary>The stage that runs before the member.</summary>
        public ProxyHandler? Start { get; } = start;

        /// <summary>The stage that replaces the member when it is not <see langword="null"/>.</summary>
        public ProxyHandler? Coverage { get; } = coverage;

        /// <summary>The stage that runs after the member.</summary>
        public ProxyHandler? End { get; } = end;
    }

    /// <summary>
    /// A generated proxy's installation surface: one hook slot per interceptable member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implemented by every generated AOP interface, and reached by <see cref="ProxyEx.SetProxy"/> rather than
    /// called by hand. The key is the member's interception key — <c>get_Name</c>, <c>set_Name</c>, or the
    /// method's own name — which is why a member cannot be overloaded.
    /// </para>
    /// <para>
    /// An unknown key throws instead of doing nothing: a name that matches no member is a mistake, and a silently
    /// ignored aspect is the failure mode this library's documentation warns about most often.
    /// </para>
    /// </remarks>
    public interface IAopHookTarget
    {
        /// <summary>
        /// Installs or clears the aspects of one member.
        /// </summary>
        /// <param name="memberKey">The member's interception key.</param>
        /// <param name="hooks">The stages to run, or <see langword="null"/> to clear them.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// <paramref name="memberKey"/> is not a member of the proxied type.
        /// </exception>
        void SetHooks(string memberKey, AspectHooks? hooks);
    }
}

#endif
