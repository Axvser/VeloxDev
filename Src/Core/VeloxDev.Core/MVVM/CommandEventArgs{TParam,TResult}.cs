using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.MVVM;

/// <summary>
/// Handles one lifecycle stage of a typed execution.
/// </summary>
/// <typeparam name="TParam">The type the command body receives.</typeparam>
/// <typeparam name="TResult">The type the command body returns.</typeparam>
/// <param name="e">The stage that was reached, carrying the argument and the result unboxed.</param>
/// <seealso cref="IVeloxCommandEvents{TParam, TResult}"/>
public delegate void CommandEventHandler<TParam, TResult>(CommandEventArgs<TParam, TResult> e);

/// <summary>
/// The lifecycle events of a typed command, carrying the argument and the result without boxing.
/// </summary>
/// <typeparam name="TParam">The type the command body receives.</typeparam>
/// <typeparam name="TResult">The type the command body returns.</typeparam>
/// <remarks>
/// <para>
/// A separate interface rather than events on <see cref="IVeloxCommand{TP, TR}"/>: one class cannot declare two
/// public events with the same name, so the typed events are implemented <em>explicitly</em> and the class-level
/// names stay the <c>object?</c>-shaped ones that <see cref="IVeloxCommand"/> and XAML binding need. Subscribe
/// through a reference typed as this interface to get the typed payload.
/// </para>
/// <para>
/// The stages mean exactly what they mean on <see cref="IVeloxCommand"/>; only the payload differs.
/// </para>
/// </remarks>
public interface IVeloxCommandEvents<TParam, TResult>
{
    /// <summary>The call was accepted.</summary>
    event CommandEventHandler<TParam, TResult>? Created;

    /// <summary>The call was parked waiting for a free slot.</summary>
    event CommandEventHandler<TParam, TResult>? Enqueued;

    /// <summary>A parked call left the queue and started.</summary>
    event CommandEventHandler<TParam, TResult>? Dequeued;

    /// <summary>The body began running.</summary>
    event CommandEventHandler<TParam, TResult>? Started;

    /// <summary>The body ran to completion without throwing.</summary>
    event CommandEventHandler<TParam, TResult>? Completed;

    /// <summary>The body threw.</summary>
    event CommandEventHandler<TParam, TResult>? Failed;

    /// <summary>The execution was cancelled.</summary>
    event CommandEventHandler<TParam, TResult>? Canceled;

    /// <summary>The execution left the pipeline, whatever its outcome.</summary>
    event CommandEventHandler<TParam, TResult>? Exited;
}

/// <summary>
/// The payload a typed execution's events carry: the same stage, with the argument and the result kept in their
/// own types.
/// </summary>
/// <typeparam name="TParam">The type the command body receives.</typeparam>
/// <typeparam name="TResult">The type the command body returns.</typeparam>
/// <remarks>
/// <para>
/// This derives from <see cref="CommandEventArgs"/> so that one instance can be handed to both faces: a typed
/// handler reads <see cref="TypedParameter"/>, and an <c>object?</c>-shaped handler reads the inherited
/// <see cref="CommandEventArgs.Parameter"/>, which boxes only when it is actually read. Nothing in this library
/// reads it, so a typed-only consumer pays nothing.
/// </para>
/// <para>
/// An execution may also arrive through the <c>object?</c>-shaped entry points — <c>ICommand.Execute</c>, or
/// binding. Then the argument was boxed before it got here and its type is not known to be
/// <typeparamref name="TParam"/>; <see cref="TypedParameter"/> performs the cast where the body is invoked, so a
/// wrong argument ends the execution as <see cref="CommandOutcome.Failed"/> rather than throwing at the call
/// site.
/// </para>
/// </remarks>
/// <seealso cref="IVeloxCommandEvents{TParam, TResult}"/>
public sealed class CommandEventArgs<TParam, TResult> : CommandEventArgs
{
    private readonly TParam _typedParameter;
    private readonly object? _boxedParameter;
    private readonly bool _hasTypedParameter;

    /// <summary>
    /// Creates the stage of an execution that entered through a typed entry point.
    /// </summary>
    /// <param name="parameter">The argument for the body, in its own type.</param>
    /// <param name="type">The stage to report.</param>
    /// <param name="ex">The failure the stage should carry, if any.</param>
    /// <param name="cts">This execution's cancellation source, if it has one.</param>
    public CommandEventArgs(
        TParam parameter,
        CommandEventType type,
        Exception? ex = null,
        CancellationTokenSource? cts = null)
        : base(type, ex, cts)
    {
        _typedParameter = parameter;
        _hasTypedParameter = true;
    }

    private CommandEventArgs(
        TParam typedParameter,
        object? boxedParameter,
        bool hasTypedParameter,
        CommandEventType type,
        Exception? ex,
        CancellationTokenSource? cts)
        : base(type, ex, cts)
    {
        _typedParameter = typedParameter;
        _boxedParameter = boxedParameter;
        _hasTypedParameter = hasTypedParameter;
    }

    /// <summary>
    /// Creates the stage of an execution that entered through an <c>object?</c>-shaped entry point.
    /// </summary>
    /// <param name="parameter">The argument as it arrived — already boxed, and possibly not a
    /// <typeparamref name="TParam"/>.</param>
    /// <param name="type">The stage to report.</param>
    /// <returns>The stage, with no typed parameter resolved yet.</returns>
    internal static CommandEventArgs<TParam, TResult> FromBoxed(object? parameter, CommandEventType type)
        => new(default!, parameter, hasTypedParameter: false, type, null, null);

    /// <summary>
    /// The argument, in its own type, or the failure of the cast if it arrived through an <c>object?</c>-shaped
    /// entry point carrying something else.
    /// </summary>
    /// <remarks>
    /// The pipeline reads this at the one place it invokes the body, so a mismatched argument becomes a
    /// <see cref="CommandOutcome.Failed"/> execution rather than an exception at the call site.
    /// </remarks>
    /// <exception cref="System.InvalidCastException">The argument is not a <typeparamref name="TParam"/>.</exception>
    internal TParam ResolveParameter() => _hasTypedParameter ? _typedParameter : (TParam)_boxedParameter!;

    /// <summary>The argument the body received, in its own type.</summary>
    /// <inheritdoc cref="ResolveParameter()" path="/exception"/>
    public TParam TypedParameter => ResolveParameter();

    /// <summary>What the body returned, once the execution has finished.</summary>
    /// <remarks>
    /// Meaningful only once the body has run; before that it is <see langword="default"/>. The pipeline writes it
    /// on the origin instance of the execution, and <see cref="With"/> does not carry it — a projection reports a
    /// stage, not a result.
    /// </remarks>
    public TResult TypedResult { get; private set; } = default!;

    /// <summary>
    /// The argument, boxed if it is a value type.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> before the body resolves it when the execution entered through an
    /// <c>object?</c>-shaped entry point, because nothing has established that the boxed value is a
    /// <typeparamref name="TParam"/> yet.
    /// </remarks>
    public override object? Parameter => _hasTypedParameter ? _typedParameter : _boxedParameter;

    /// <summary>
    /// Projects this instance onto another lifecycle stage, staying in the typed domain.
    /// </summary>
    /// <param name="newType">The stage to report.</param>
    /// <param name="ex">
    /// The failure the new instance should carry. When omitted, the receiver's own failure is kept instead.
    /// </param>
    /// <returns>The new stage.</returns>
    /// <remarks>
    /// <see langword="new"/> rather than an override: covariant returns are not available on
    /// <c>netstandard2.0</c>/<c>net461</c>, which this assembly targets. The inherited
    /// <see cref="CommandEventArgs.With"/> stays reachable through a base reference and project onto the
    /// <c>object?</c>-shaped type, which is the right answer for a handler that arrived through the fallback face.
    /// </remarks>
    public new CommandEventArgs<TParam, TResult> With(CommandEventType newType, Exception? ex = null)
        => new(_typedParameter, _boxedParameter, _hasTypedParameter, newType, ex ?? Exception, Cts);

    // 强类型结果槽。与基类的装箱槽并存：走兜底接口（IVeloxCommandCompletion / IVeloxCommandResult）的调用方
    // 读基类那个，走强类型入口的读这个。With() 的副本同样不带 —— 副本不该能收尾。
    internal TaskCompletionSource<TypedCompletion<TResult>>? TypedCompletion { get; set; }

    // 恰好收尾一次；没有等的人在时是空操作。名字刻意与基类的 Complete 区分 ——
    // TResult = object? 时两者签名会撞，而那是强类型命令最常见的闭合。
    internal void CompleteTyped(CommandOutcome outcome, Exception? exception, TResult result)
    {
        TypedResult = result;
        TypedCompletion?.TrySetResult(new TypedCompletion<TResult>(outcome, exception, result));
    }
}

/// <summary>
/// How one typed execution ended, with the body's value in its own type.
/// </summary>
/// <typeparam name="TResult">The type the command body returns.</typeparam>
/// <param name="outcome">How the execution ended.</param>
/// <param name="exception">The failure, on <see cref="CommandOutcome.Failed"/> only.</param>
/// <param name="result">The body's value.</param>
internal readonly struct TypedCompletion<TResult>(CommandOutcome outcome, Exception? exception, TResult result)
{
    internal CommandOutcome Outcome { get; } = outcome;

    internal Exception? Exception { get; } = exception;

    internal TResult Result { get; } = result;
}
