namespace VeloxDev.MVVM;

/// <summary>
/// A command that can report how busy it is.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System.Windows.Input.ICommand.CanExecute"/> cannot answer this: it reflects the predicate and the
/// lock, never the queue. A command whose only slot is taken still reports itself as executable, so a UI bound
/// to <c>CanExecute</c> alone shows a button that does nothing when pressed. These members are the read model
/// for that.
/// </para>
/// <para>
/// A separate interface rather than a member of <see cref="IVeloxCommand"/>, so that adding it breaks no
/// existing implementer.
/// </para>
/// </remarks>
/// <seealso cref="VeloxCommandExtensions.IsBusy(IVeloxCommand)"/>
public interface IVeloxCommandStatus
{
    /// <summary>
    /// Whether an execution is running or waiting for a free slot.
    /// </summary>
    /// <remarks>
    /// Read without taking the command's internal lock, so a concurrent update can make this one step stale.
    /// That is deliberate: this is a display value, and a property getter must not block.
    /// </remarks>
    bool IsBusy { get; }

    /// <summary>How many executions are running right now.</summary>
    /// <inheritdoc cref="IsBusy" path="/remarks"/>
    int ActiveCount { get; }

    /// <summary>How many calls are waiting for a free slot.</summary>
    /// <inheritdoc cref="IsBusy" path="/remarks"/>
    int PendingCount { get; }
}
