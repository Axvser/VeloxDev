namespace VeloxDev.MVVM;

/// <summary>
/// A command that can report when one specific execution has ended.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IVeloxCommand.ExecuteAsync"/> returns as soon as the call is accepted — queued or started — which
/// is the right contract for a fire-and-forget <c>ICommand</c>, but it leaves callers that need the actual
/// result pairing <see cref="IVeloxCommand.Exited"/> with <see cref="IVeloxCommand.Failed"/> by hand. Worse,
/// that pairing never completes for a call the command refuses or drops, because those never raise
/// <see cref="IVeloxCommand.Exited"/>.
/// </para>
/// <para>
/// This is the awaitable alternative. It is a separate interface rather than a member of
/// <see cref="IVeloxCommand"/> so that adding it breaks no existing implementer.
/// </para>
/// </remarks>
/// <seealso cref="VeloxCommandExtensions.ExecuteAndWaitAsync(IVeloxCommand, object?, System.Threading.CancellationToken)"/>
public interface IVeloxCommandCompletion
{
    /// <summary>
    /// Executes the command and completes when <em>this</em> execution has ended.
    /// </summary>
    /// <param name="parameter">The argument to pass to the body.</param>
    /// <param name="cancellationToken">
    /// Abandons the wait only — it does not cancel the execution. Use <see cref="IVeloxCommand.Interrupt"/> or
    /// <see cref="IVeloxCommand.Clear"/> to stop work that is already under way.
    /// </param>
    /// <returns>How the execution ended.</returns>
    /// <remarks>
    /// A call that is still queued when the command stays locked has not ended, so the returned task does not
    /// complete until a slot frees or the queue is cleared. Pass a <paramref name="cancellationToken"/> if that
    /// wait needs an escape hatch.
    /// </remarks>
    /// <exception cref="System.OperationCanceledException"><paramref name="cancellationToken"/> fired first.</exception>
    Task<CommandCompletion> ExecuteAndWaitAsync(object? parameter, CancellationToken cancellationToken = default);
}
