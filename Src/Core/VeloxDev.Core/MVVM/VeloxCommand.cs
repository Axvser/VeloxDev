using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.MVVM;

/// <summary>
/// The <c>object?</c>-shaped command: a body that takes whatever the caller passes and returns nothing.
/// </summary>
/// <remarks>
/// <para>
/// This is the fallback shape. It is what <see cref="System.Windows.Input.ICommand"/> and XAML binding can reach,
/// so every command is one, and it is the right choice when the argument genuinely is unknown at compile time.
/// When it is known, use <see cref="VeloxCommand{TParam,TResult}"/> instead: the same pipeline, with the argument and the
/// value kept in their own types instead of being boxed on the way through.
/// </para>
/// <para>
/// Everything the pipeline does — the concurrency cap, the queue, <c>Lock</c>/<c>Interrupt</c>/<c>Clear</c>, the
/// eight lifecycle events — is documented on <see cref="CommandPipeline{TParam, TResult}"/>.
/// </para>
/// </remarks>
/// <seealso cref="CommandPipeline{TParam, TResult}"/>
/// <seealso cref="VeloxCommand{TParam,TResult}"/>
public class VeloxCommand : CommandPipeline<object?, object?>
{
    /// <summary>
    /// Creates a command from a body that takes the parameter and the cancellation token.
    /// </summary>
    /// <param name="command">The body to run.</param>
    /// <param name="canExecute">The predicate behind <see cref="System.Windows.Input.ICommand.CanExecute"/>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public VeloxCommand(
        Func<object?, CancellationToken, Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        : base(command, null, canExecute, semaphore, isCtsNeeded: true)
    {
    }

    /// <summary>
    /// Creates a command whose body returns a value, so <c>ExecuteAsync(parameter, cancellationToken)</c> can
    /// hand it back to the caller.
    /// </summary>
    /// <param name="command">The body, with its value discarded. Used only if <paramref name="resultCommand"/> is never consulted.</param>
    /// <param name="resultCommand">The body, with its value preserved.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <param name="isCtsNeeded">
    /// Whether each execution gets a <see cref="CancellationTokenSource"/>. Pass <see langword="false"/> when the
    /// body takes no token — it cannot observe cancellation anyway, and the source would be pure allocation.
    /// </param>
    /// <remarks>
    /// The value is read at the single place the pipeline invokes the body, so it belongs to exactly one
    /// execution and is never kept on the command itself.
    /// </remarks>
    protected VeloxCommand(
        Func<object?, CancellationToken, Task> command,
        Func<object?, CancellationToken, Task<object?>>? resultCommand,
        Predicate<object?>? canExecute,
        int semaphore,
        bool isCtsNeeded = true)
        : base(command, resultCommand, canExecute, semaphore, isCtsNeeded)
    {
    }

    /// <summary>
    /// Creates a command from a body that takes the parameter and returns a value, but cannot be cancelled.
    /// </summary>
    /// <param name="command">The body. Its value is what <see cref="IVeloxCommandResult"/> hands back.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose executions report their value.</returns>
    /// <remarks>
    /// The <c>null</c>-token twin of <see cref="CreateTaskWithResult"/> — same contract as
    /// <see cref="CreateTaskOnlyWithParameter"/>, so a body returning a value does not silently acquire a
    /// cancellation source it can never observe.
    /// </remarks>
    public static VeloxCommand CreateTaskOnlyWithResult(
        Func<object?, Task<object?>> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (parameter, _) => { await command(parameter).ConfigureAwait(false); },
            async (parameter, _) => await command(parameter).ConfigureAwait(false),
            canExecute,
            semaphore,
            isCtsNeeded: false);

    /// <summary>
    /// Creates a command from a body that takes the parameter and the cancellation token, and returns a value.
    /// </summary>
    /// <param name="command">The body. Its value is what <see cref="IVeloxCommandResult"/> hands back.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose executions report their value.</returns>
    public static VeloxCommand CreateTaskWithResult(
        Func<object?, CancellationToken, Task<object?>> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (parameter, ct) => { await command(parameter, ct).ConfigureAwait(false); },
            command,
            canExecute,
            semaphore);

    /// <summary>
    /// Creates a command from a body that takes the parameter but cannot be cancelled.
    /// </summary>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose body always runs to completion.</returns>
    /// <remarks>
    /// The returned command still raises <see cref="IVeloxCommand.Canceled"/> when interrupted, but the body
    /// always runs to completion: it never receives a token to observe.
    /// </remarks>
    public static VeloxCommand CreateTaskOnlyWithParameter(
        Func<object?, Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (parameter, _) => { await command(parameter).ConfigureAwait(false); },
            resultCommand: null,
            canExecute,
            semaphore,
            isCtsNeeded: false);

    /// <summary>
    /// Creates a strongly typed command from a body that takes its parameter but cannot be cancelled.
    /// </summary>
    /// <typeparam name="T">The type the command body receives.</typeparam>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose body always runs to completion.</returns>
    /// <remarks>
    /// The typed counterpart of <see cref="CreateTaskOnlyWithParameter"/>, carrying the same <c>null</c>-token
    /// contract. It exists so that a <c>[VeloxCommand]</c> method whose parameter is a concrete type keeps that
    /// contract rather than silently acquiring a cancellable <see cref="CancellationTokenSource"/> the body has
    /// no way to observe.
    /// </remarks>
    public static VeloxCommand<T, object?> CreateTypedTaskOnlyWithParameter<T>(
        Func<T, Task> command,
        Predicate<T>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (parameter, _) => { await command(parameter).ConfigureAwait(false); },
            canExecute,
            semaphore,
            isCtsNeeded: false);

    /// <summary>
    /// Creates a strongly typed command from a body that takes its parameter and the cancellation token but
    /// returns nothing.
    /// </summary>
    /// <typeparam name="T">The type the command body receives.</typeparam>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose result channel is always <see langword="null"/>.</returns>
    /// <remarks>
    /// Exists so a body with no return value keeps the pipeline's <c>null</c>-token channel instead of being
    /// wrapped in a result-returning thunk, which would cost an async state machine on every execution — more
    /// than the boxing this design removes. The declared result type is <see cref="object"/> and the value is
    /// always <see langword="null"/>.
    /// </remarks>
    public static VeloxCommand<T, object?> CreateTypedWithParameter<T>(
        Func<T, CancellationToken, Task> command,
        Predicate<T>? canExecute = null,
        int semaphore = 1)
        => new(command, canExecute, semaphore, isCtsNeeded: true);

    /// <summary>
    /// Creates a command from a body that takes only the cancellation token, and really can be cancelled.
    /// </summary>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose body receives the execution's token.</returns>
    public static VeloxCommand CreateTaskOnlyWithCancellationToken(
        Func<CancellationToken, Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (_, ct) => { await command(ct).ConfigureAwait(false); },
            resultCommand: null,
            canExecute,
            semaphore);

#if !NETSTANDARD2_0 && !NETFRAMEWORK
    /// <summary>
    /// Creates a command from a body that takes the parameter and returns a <see cref="ValueTask"/>.
    /// </summary>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose body always runs to completion.</returns>
    /// <remarks>
    /// Named rather than an overload of <see cref="CreateTaskOnlyWithParameter"/>. A
    /// <c>Func&lt;object?, ValueTask&gt;</c> sitting next to the <c>Task</c> one would make every
    /// <c>async</c> lambda call site ambiguous (CS0121) — and <c>async p =&gt; { … }</c> is how callers of this
    /// library write commands. It also stays <c>null</c>-token like its <c>Task</c> sibling: the body cannot be
    /// stopped, it can only be told that it was.
    /// </remarks>
    public static VeloxCommand CreateTaskOnlyWithValueTaskParameter(
        Func<object?, ValueTask> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (parameter, _) => { await command(parameter).ConfigureAwait(false); },
            resultCommand: null,
            canExecute,
            semaphore,
            isCtsNeeded: false);

    /// <summary>
    /// Creates a command from a body that takes only the cancellation token and returns a
    /// <see cref="ValueTask"/>, so it really can be cancelled.
    /// </summary>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <returns>A command whose body receives the execution's token.</returns>
    public static VeloxCommand CreateTaskOnlyWithValueTaskCancellationToken(
        Func<CancellationToken, ValueTask> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        =>
        new(
            async (_, ct) => { await command(ct).ConfigureAwait(false); },
            resultCommand: null,
            canExecute,
            semaphore);
#endif

    /// <summary>
    /// Creates a command from a body that takes nothing.
    /// </summary>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    /// <remarks>
    /// Like <see cref="CreateTaskOnlyWithParameter"/>, the body never receives a token, so an interrupted
    /// execution reports <see cref="CommandEventType.Canceled"/> without the body actually stopping.
    /// </remarks>
    public VeloxCommand(
        Func<Task> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        : base(
            (_, _) => command(),
            resultCommand: null,
            canExecute,
            semaphore,
            isCtsNeeded: false)
    {
    }

    /// <summary>
    /// Creates a command from a synchronous body that takes the parameter.
    /// </summary>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    public VeloxCommand(
        Action<object?> command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        : base(
            (parameter, _) => { command(parameter); return Task.CompletedTask; },
            resultCommand: null,
            canExecute,
            semaphore,
            isCtsNeeded: false)
    {
    }

    /// <summary>
    /// Creates a command from a synchronous body that takes nothing.
    /// </summary>
    /// <param name="command">The body.</param>
    /// <param name="canExecute">The predicate behind <c>CanExecute</c>, or <see langword="null"/> to always allow.</param>
    /// <param name="semaphore">How many executions may run at once; further calls queue rather than drop.</param>
    public VeloxCommand(
        Action command,
        Predicate<object?>? canExecute = null,
        int semaphore = 1)
        : base(
            (_, _) => { command(); return Task.CompletedTask; },
            resultCommand: null,
            canExecute,
            semaphore,
            isCtsNeeded: false)
    {
    }
}
