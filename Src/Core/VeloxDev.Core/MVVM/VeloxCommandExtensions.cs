namespace VeloxDev.MVVM;

/// <summary>
/// Reaches the capabilities <see cref="VeloxCommand"/> has beyond <see cref="IVeloxCommand"/>, from the
/// interface type that generated command properties are declared with.
/// </summary>
/// <remarks>
/// The generator emits <c>public IVeloxCommand FooCommand</c>, so consumers hold the interface even though the
/// instance behind it is always a <see cref="VeloxCommand"/>. These extensions are how those call sites reach
/// the extra members without <see cref="IVeloxCommand"/> itself growing any — which would break every
/// hand-written implementer.
/// </remarks>
public static class VeloxCommandExtensions
{
    /// <summary>
    /// Executes the command and completes when <em>this</em> execution has ended.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="parameter">The argument to pass to the body.</param>
    /// <param name="cancellationToken">
    /// Abandons the wait only — it does not cancel the execution. Use <see cref="IVeloxCommand.Interrupt"/> or
    /// <see cref="IVeloxCommand.Clear"/> to stop work already under way.
    /// </param>
    /// <returns>How the execution ended.</returns>
    /// <remarks>
    /// A call that is still queued while the command stays locked has not ended, so the returned task does not
    /// complete until a slot frees or the queue is cleared.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="command"/> is a hand-written implementation that does not implement
    /// <see cref="IVeloxCommandCompletion"/>.
    /// </exception>
    public static Task<CommandCompletion> ExecuteAndWaitAsync(
        this IVeloxCommand command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return command is IVeloxCommandCompletion completable
            ? completable.ExecuteAndWaitAsync(parameter, cancellationToken)
            : throw new NotSupportedException(Unsupported(nameof(IVeloxCommandCompletion)));
    }

    /// <summary>Whether an execution is running or waiting for a free slot.</summary>
    /// <param name="command">The command to inspect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="command"/> is a hand-written implementation that does not implement
    /// <see cref="IVeloxCommandStatus"/>.
    /// </exception>
    public static bool IsBusy(this IVeloxCommand command) => Status(command).IsBusy;

    /// <summary>How many executions are running right now.</summary>
    /// <param name="command">The command to inspect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="command"/> is a hand-written implementation that does not implement
    /// <see cref="IVeloxCommandStatus"/>.
    /// </exception>
    public static int ActiveCount(this IVeloxCommand command) => Status(command).ActiveCount;

    /// <summary>How many calls are waiting for a free slot.</summary>
    /// <param name="command">The command to inspect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="command"/> is a hand-written implementation that does not implement
    /// <see cref="IVeloxCommandStatus"/>.
    /// </exception>
    public static int PendingCount(this IVeloxCommand command) => Status(command).PendingCount;

    private static IVeloxCommandStatus Status(IVeloxCommand command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return command as IVeloxCommandStatus
            ?? throw new NotSupportedException(Unsupported(nameof(IVeloxCommandStatus)));
    }

    private static string Unsupported(string interfaceName) =>
        $"This command does not implement {interfaceName}. Every command built by VeloxCommand does; " +
        "a hand-written IVeloxCommand implementation has to opt in.";
}
