namespace VeloxDev.Lifetime;

/// <summary>Whether the host application is still running.</summary>
public interface IApplicationState
{
    /// <summary>False once the host has begun shutting down; a hint, never a reason to throw.</summary>
    bool IsAlive { get; }
}

/// <summary>A liveness flag a host reports into, for the hosts that can observe their own exit.</summary>
public sealed class ApplicationState : IApplicationState
{
    private volatile bool _isAlive = true;

    public bool IsAlive => _isAlive;

    /// <remarks>
    /// Deliberately not one-way. A host that reports death from one signal and later finds the signal was spurious —
    /// WinUI clears its flag when a single enqueue is refused — has to be able to take it back, or every consumer
    /// stays dead for the rest of the process with nothing logged.
    /// </remarks>
    public void SetAlive(bool alive) => _isAlive = alive;
}
