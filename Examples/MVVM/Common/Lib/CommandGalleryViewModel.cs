using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace Demo.Commands;

/// <summary>
/// The argument a strongly typed command receives. A concrete type is the ordinary way to give a command
/// more than one value: one parameter, compile-time safe, and reusable without allocating a tuple.
/// </summary>
public sealed class NotePayload
{
    /// <summary>Creates a payload.</summary>
    /// <param name="title">The note's title.</param>
    /// <param name="weight">The note's weight; commands use it to decide whether they can run.</param>
    public NotePayload(string title, int weight)
    {
        Title = title;
        Weight = weight;
    }

    /// <summary>The note's title.</summary>
    public string Title { get; }

    /// <summary>The note's weight.</summary>
    public int Weight { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Title}({Weight})";
}

/// <summary>
/// One button in the gallery: a generated command, the name it was generated under, and the argument the UI
/// should pass to it.
/// </summary>
public sealed class CommandSample
{
    /// <summary>Creates a sample.</summary>
    /// <param name="name">The generated member's name, shown on the button.</param>
    /// <param name="command">The generated command.</param>
    /// <param name="parameter">The argument to pass, or <see langword="null"/> for a parameterless command.</param>
    public CommandSample(string name, IVeloxCommand command, object? parameter)
    {
        Name = name;
        Command = command;
        Parameter = parameter;
    }

    /// <summary>The generated member's name.</summary>
    public string Name { get; }

    /// <summary>The generated command.</summary>
    public IVeloxCommand Command { get; }

    /// <summary>The argument the UI passes to <see cref="Command"/>.</summary>
    public object? Parameter { get; }
}

/// <summary>
/// Every signature <c>[VeloxCommand]</c> accepts, one method each, plus the validators that go with them.
/// </summary>
/// <remarks>
/// <para>
/// The generated member's name is <c>methodName.Replace("Async", "") + "Command"</c>, and the replacement hits
/// every occurrence — so the methods here are named so that no two collapse to the same stem.
/// </para>
/// <para>
/// Nothing here inherits a view-model base: a class needs one only for <c>[VeloxProperty]</c>. Commands generate
/// on their own, and the generated file supplies the notification plumbing if the class has no base at all.
/// </para>
/// </remarks>
public partial class CommandGalleryViewModel
{
    private const int MaxLogEntries = 60;

    /// <summary>What the commands have done, newest last. Bound directly to the list in the window.</summary>
    public ObservableCollection<string> Log { get; } = [];

    /// <summary>One entry per generated command, for the window to render as buttons.</summary>
    public IReadOnlyList<CommandSample> Samples { get; }

    /// <summary>Builds the samples and seeds the log.</summary>
    public CommandGalleryViewModel()
    {
        var typed = new TypedGalleryViewModel<string>();

        Samples =
        [
            // ── 零个前导形参 ────────────────────────────────────────────────
            new CommandSample(nameof(NotifyCommand), NotifyCommand, null),
            new CommandSample(nameof(RunCommand), RunCommand, null),
            new CommandSample(nameof(QueryCommand), QueryCommand, null),
            new CommandSample(nameof(StepCommand), StepCommand, null),
            new CommandSample(nameof(MeasureCommand), MeasureCommand, null),

            // ── 只有 CancellationToken：唯一真正可取消的一类 ─────────────────
            new CommandSample(nameof(RunCancellableCommand), RunCancellableCommand, null),
            new CommandSample(nameof(StepCancellableCommand), StepCancellableCommand, null),

            // ── object? 形参：命令仍然是非强类型的 ───────────────────────────
            new CommandSample(nameof(HandleObjectCommand), HandleObjectCommand, "boxed"),
            new CommandSample(nameof(HandleObjectAndTokenCommand), HandleObjectAndTokenCommand, "boxed"),
            new CommandSample(nameof(NotifyObjectCommand), NotifyObjectCommand, "boxed"),
            new CommandSample(nameof(StepObjectCommand), StepObjectCommand, "boxed"),

            // ── 具体类型形参：属性类型变成 IVeloxCommand<P> ──────────────────
            new CommandSample(nameof(HandleNoteCommand), HandleNoteCommand, new NotePayload("strong", 2)),
            new CommandSample(nameof(HandleNoteAndTokenCommand), HandleNoteAndTokenCommand, new NotePayload("strong", 1)),
            new CommandSample(nameof(NotifyWeightCommand), NotifyWeightCommand, 3),
            new CommandSample(nameof(StepMagnitudeCommand), StepMagnitudeCommand, 1.5d),
            new CommandSample(nameof(MeasureNoteCommand), MeasureNoteCommand, new NotePayload("measure", 5)),

            // ── 方法的类型参数：访问器方法，每个封闭 T 一个命令 ───────────────
            new CommandSample("GetStoreCommand<string>()", GetStoreCommand<string>(), "generic"),
            new CommandSample("GetStorePairCommand<string>()", GetStorePairCommand<string>(), ("a", "b")),

            // ── 类的类型参数：属性就带着它 ──────────────────────────────────
            new CommandSample("TypedGallery<string>.AcceptCommand", typed.AcceptCommand, "class-scoped"),
        ];

        Record("Ready — press a button to run one generated command.");
    }

    // ── void, no parameters ────────────────────────────────────────────────

    /// <summary>Runs synchronously; the body never sees a token, so interrupting it only reports.</summary>
    [VeloxCommand]
    private void Notify() => Record("Notify(): void, no parameters");

    // ── Task, no parameters ────────────────────────────────────────────────

    /// <summary>Runs asynchronously with nothing to pass.</summary>
    [VeloxCommand]
    private Task Run()
    {
        Record("Run(): Task, no parameters");
        return Task.CompletedTask;
    }

    // ── Task<T>, no parameters ─────────────────────────────────────────────

    /// <summary>
    /// Returns a value. The one-argument <c>Execute</c> still returns as soon as the call is accepted; the value
    /// comes out of <c>((IVeloxCommandResult)QueryCommand).ExecuteAsync(null, ct)</c>, because this command is not
    /// strongly typed.
    /// </summary>
    [VeloxCommand]
    private Task<string> Query()
    {
        Record("Query(): Task<string>, no parameters");
        return Task.FromResult("queried");
    }

    // ── ValueTask, no parameters ───────────────────────────────────────────

    /// <summary>Returns a <see cref="ValueTask"/>; the generated thunk converts it with <c>AsTask()</c>.</summary>
    [VeloxCommand]
    private ValueTask Step()
    {
        Record("Step(): ValueTask, no parameters");
        return default;
    }

    // ── ValueTask<T>, no parameters ────────────────────────────────────────

    /// <summary>Returns a <see cref="ValueTask{TResult}"/>.</summary>
    [VeloxCommand]
    private ValueTask<int> Measure()
    {
        Record("Measure(): ValueTask<int>, no parameters");
        return new ValueTask<int>(Log.Count);
    }

    // ── CancellationToken only ────────────────────────────────────────────

    /// <summary>
    /// The only shape whose body really receives a token — and therefore the only one <c>Interrupt</c> and
    /// <c>Clear</c> can actually stop.
    /// </summary>
    [VeloxCommand]
    private async Task RunCancellable(CancellationToken ct)
    {
        Record("RunCancellable(ct): running");
        await Task.Delay(400, ct);
        Record("RunCancellable(ct): finished");
    }

    /// <summary>The same contract with a <see cref="ValueTask"/> body.</summary>
    [VeloxCommand]
    private async ValueTask StepCancellable(CancellationToken ct)
    {
        Record("StepCancellable(ct): running");
        await Task.Delay(400, ct);
        Record("StepCancellable(ct): finished");
    }

    // ── object? parameter ─────────────────────────────────────────────────

    /// <summary>An untyped command: the property stays <see cref="IVeloxCommand"/> and the argument is boxed.</summary>
    [VeloxCommand]
    private Task HandleObject(object? value)
    {
        Record($"HandleObject(object?): {value}");
        return Task.CompletedTask;
    }

    /// <summary>Untyped, cancellable.</summary>
    [VeloxCommand]
    private async Task HandleObjectAndToken(object? value, CancellationToken ct)
    {
        Record($"HandleObjectAndToken(object?, ct): {value}");
        await Task.Delay(200, ct);
    }

    /// <summary>A synchronous untyped body.</summary>
    [VeloxCommand]
    private void NotifyObject(object? value) => Record($"NotifyObject(object?): {value}");

    /// <summary>Untyped with a <see cref="ValueTask"/> body.</summary>
    [VeloxCommand]
    private ValueTask StepObject(object? value)
    {
        Record($"StepObject(object?): {value}");
        return default;
    }

    // ── Concrete parameter type: the property becomes IVeloxCommand<P> ────

    /// <summary>
    /// A concrete parameter makes the command strongly typed <em>and</em> makes the validator strongly typed:
    /// the generated declaration is <c>CanExecuteHandleNoteCommand(NotePayload parameter)</c>.
    /// </summary>
    [VeloxCommand(canValidate: true)]
    private Task HandleNote(NotePayload note)
    {
        Record($"HandleNote(NotePayload): {note}");
        return Task.CompletedTask;
    }

    // Two things must match the generated declaration exactly: the parameter type follows the command's
    // parameter, and the name mirrors the source method's own parameter — HandleNote(NotePayload note)
    // means the validator takes `note`. Any other name is CS8826.
    //
    // The null check is not redundant. A strongly typed validator is still reachable through the *untyped*
    // CanExecute(object?), and the frameworks use it: WPF calls CanExecute(null) once while it applies the
    // button template. Without the check this dereference throws on the UI thread and takes the app down —
    // which is the concrete form of "strong typing is type information, not a guarantee".
    private partial bool CanExecuteHandleNoteCommand(NotePayload note) => note is not null && note.Weight > 0;

    /// <summary>Strongly typed and cancellable; <c>Interrupt</c> really stops this one.</summary>
    [VeloxCommand]
    private async Task HandleNoteAndToken(NotePayload note, CancellationToken ct)
    {
        Record($"HandleNoteAndToken(NotePayload, ct): {note}");
        await Task.Delay(200, ct);
    }

    /// <summary>
    /// A strongly typed body that returns a value: the property is <c>IVeloxCommand&lt;NotePayload, int&gt;</c>,
    /// so both the argument and the result are typed and <c>await MeasureNoteCommand.ExecuteAsync(note, ct)</c>
    /// yields the weight directly. This is the shape the demo's "MeasureNoteCommand" button uses.
    /// </summary>
    [VeloxCommand]
    private Task<int> MeasureNote(NotePayload note)
    {
        Record($"MeasureNote(NotePayload): {note}");
        return Task.FromResult(note.Weight);
    }

    /// <summary>
    /// A value-type parameter is strongly typed too — and still boxes on the way through the pipeline, because
    /// the queue carries its argument as <see cref="object"/>.
    /// </summary>
    [VeloxCommand(canValidate: true)]
    private void NotifyWeight(int weight) => Record($"NotifyWeight(int): {weight}");

    private partial bool CanExecuteNotifyWeightCommand(int weight) => weight > 0;

    /// <summary>A strongly typed <see cref="ValueTask"/> body.</summary>
    [VeloxCommand]
    private ValueTask StepMagnitude(double magnitude)
    {
        Record($"StepMagnitude(double): {magnitude}");
        return default;
    }

    // ── Method type parameter: a generated accessor, not a property ────────

    /// <summary>
    /// The type argument belongs to the method, so a property cannot carry it — the generator emits
    /// <c>IVeloxCommand&lt;T&gt; GetStoreCommand&lt;T&gt;()</c> instead. Each closed <c>T</c> gets its own
    /// command, with its own queue, lock and concurrency cap.
    /// </summary>
    /// <typeparam name="T">The type to store.</typeparam>
    [VeloxCommand(canValidate: true)]
    private Task Store<T>(T value)
    {
        Record($"GetStoreCommand<{typeof(T).Name}>(): {value}");
        return Task.CompletedTask;
    }

    // The validator of a generic command carries the same type parameter and constraint,
    // and mirrors the source method's parameter name (Store<T>(T value) ⇒ value).
    private partial bool CanExecuteStoreCommand<T>(T value) => value is not null;

    /// <summary>A method type parameter nested inside the parameter type — the array and tuple forms both work.</summary>
    /// <typeparam name="T">The element type of the pair.</typeparam>
    [VeloxCommand]
    private Task StorePair<T>((T, T) pair)
    {
        Record($"GetStorePairCommand<{typeof(T).Name}>(): {pair}");
        return Task.CompletedTask;
    }

    private void Record(string message)
    {
        Log.Add(message);

        while (Log.Count > MaxLogEntries)
        {
            Log.RemoveAt(0);
        }
    }
}

/// <summary>
/// The type argument comes from the containing class, so the generated property keeps it:
/// <c>IVeloxCommand&lt;T&gt; AcceptCommand</c>.
/// </summary>
/// <typeparam name="T">The type the command body receives.</typeparam>
public partial class TypedGalleryViewModel<T>
{
    /// <summary>Runs with the class's own type argument, with no box in the signature.</summary>
    [VeloxCommand]
    private Task Accept(T value)
    {
        _ = value;
        return Task.CompletedTask;
    }
}
