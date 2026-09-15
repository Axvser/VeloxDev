using System.Runtime.CompilerServices;

namespace VeloxDev.Threading;

/// <summary>A thread, as an opaque handle the layer above cannot name.</summary>
/// <remarks>
/// Wraps whatever the host uses to identify a thread — a Dispatcher, a DispatcherQueue, an IDispatcher, a
/// SynchronizationContext. "No thread owns this" becomes an answer with a name instead of a null every caller has to
/// cast and hope about.
/// </remarks>
public readonly struct ThreadRef : IEquatable<ThreadRef>
{
    private readonly object? _handle;

    private ThreadRef(object? handle) => _handle = handle;

    /// <summary>No thread owns this. A real answer, not a failure.</summary>
    public static ThreadRef None => default;

    public bool IsNone => _handle is null;

    /// <summary>Wraps a host's handle, or returns <see cref="None"/> when it is null.</summary>
    public static ThreadRef From<T>(T? handle) where T : class => handle is null ? None : new(handle);

    /// <summary>The handle as its own type, or false when this is <see cref="None"/> or another host's type.</summary>
    /// <remarks>
    /// The out parameter is declared non-nullable so a caller that checks the return value can use it directly. It is
    /// null whenever this returns false, which is why the check is load-bearing.
    /// </remarks>
    public bool TryGet<T>(out T handle) where T : class
    {
        handle = (_handle as T)!;
        return handle is not null;
    }

    public bool Equals(ThreadRef other) => ReferenceEquals(_handle, other._handle);

    public override bool Equals(object? obj) => obj is ThreadRef other && Equals(other);

    public override int GetHashCode() => _handle is null ? 0 : RuntimeHelpers.GetHashCode(_handle);

    public static bool operator ==(ThreadRef left, ThreadRef right) => left.Equals(right);

    public static bool operator !=(ThreadRef left, ThreadRef right) => !left.Equals(right);
}
