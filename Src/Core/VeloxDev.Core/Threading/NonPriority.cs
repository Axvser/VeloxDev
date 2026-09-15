namespace VeloxDev.Threading;

/// <summary>
/// The priority type of a host whose dispatcher carries none. Fills a type parameter; never instantiated.
/// </summary>
/// <remarks>
/// An empty struct on purpose: no instance to pass, no allocation for the type argument, and
/// <c>default(NonPriority)</c> is a real value, so it flows through the existing <c>is TPriorityCore</c> checks
/// unchanged.
/// </remarks>
public readonly struct NonPriority
{
}
