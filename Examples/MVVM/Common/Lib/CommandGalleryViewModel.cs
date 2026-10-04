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

    // 同步运行；函数体看不到 token，中断它只能报告。
    [VeloxCommand]
    private void Notify() => Record("Notify(): void, no parameters");

    // ── Task, no parameters ────────────────────────────────────────────────

    // 异步运行，没有参数可传。
    [VeloxCommand]
    private Task Run()
    {
        Record("Run(): Task, no parameters");
        return Task.CompletedTask;
    }

    // ── Task<T>, no parameters ─────────────────────────────────────────────

    // 返回值：一参的 Execute 在调用被接受后立即返回，值要从
    // ((IVeloxCommandResult)QueryCommand).ExecuteAsync(null, ct) 取出，因为这条命令不是强类型的。
    [VeloxCommand]
    private Task<string> Query()
    {
        Record("Query(): Task<string>, no parameters");
        return Task.FromResult("queried");
    }

    // ── ValueTask, no parameters ───────────────────────────────────────────

    // 返回 ValueTask；生成的 thunk 用 AsTask() 转换它。
    [VeloxCommand]
    private ValueTask Step()
    {
        Record("Step(): ValueTask, no parameters");
        return default;
    }

    // ── ValueTask<T>, no parameters ────────────────────────────────────────

    // 返回 ValueTask<T>。
    [VeloxCommand]
    private ValueTask<int> Measure()
    {
        Record("Measure(): ValueTask<int>, no parameters");
        return new ValueTask<int>(Log.Count);
    }

    // ── CancellationToken only ────────────────────────────────────────────

    // 唯一真正把 token 交给函数体的形状，因此也是 Interrupt 与 Clear 唯一能真正停下的。
    [VeloxCommand]
    private async Task RunCancellable(CancellationToken ct)
    {
        Record("RunCancellable(ct): running");
        await Task.Delay(400, ct);
        Record("RunCancellable(ct): finished");
    }

    // 同样的契约，函数体换成 ValueTask。
    [VeloxCommand]
    private async ValueTask StepCancellable(CancellationToken ct)
    {
        Record("StepCancellable(ct): running");
        await Task.Delay(400, ct);
        Record("StepCancellable(ct): finished");
    }

    // ── object? parameter ─────────────────────────────────────────────────

    // 非强类型命令：属性仍是 IVeloxCommand，实参被装箱。
    [VeloxCommand]
    private Task HandleObject(object? value)
    {
        Record($"HandleObject(object?): {value}");
        return Task.CompletedTask;
    }

    // 非强类型，可取消。
    [VeloxCommand]
    private async Task HandleObjectAndToken(object? value, CancellationToken ct)
    {
        Record($"HandleObjectAndToken(object?, ct): {value}");
        await Task.Delay(200, ct);
    }

    // 非强类型的同步函数体。
    [VeloxCommand]
    private void NotifyObject(object? value) => Record($"NotifyObject(object?): {value}");

    // 非强类型，函数体是 ValueTask。
    [VeloxCommand]
    private ValueTask StepObject(object? value)
    {
        Record($"StepObject(object?): {value}");
        return default;
    }

    // ── Concrete parameter type: the property becomes IVeloxCommand<P> ────

    // 具体形参让命令变成强类型，校验器也跟着变强类型：生成的声明是
    // CanExecuteHandleNoteCommand(NotePayload parameter)。
    [VeloxCommand(canValidate: true)]
    private Task HandleNote(NotePayload note)
    {
        Record($"HandleNote(NotePayload): {note}");
        return Task.CompletedTask;
    }

    // 两处必须与生成的声明完全一致：形参类型跟随命令的形参，名字照抄源方法的形参名
    // （HandleNote(NotePayload note) ⇒ 校验器收到 note），否则 CS8826。
    // null 检查不是多余的：强类型校验器仍能被*非强类型*的 CanExecute(object?) 调到，
    // WPF 在套用按钮模板时会以 CanExecute(null) 调一次；没有这个检查，UI 线程上的解引用会把程序带崩。
    private partial bool CanExecuteHandleNoteCommand(NotePayload note) => note is not null && note.Weight > 0;

    // 强类型且可取消；Interrupt 能真正停下这一条。
    [VeloxCommand]
    private async Task HandleNoteAndToken(NotePayload note, CancellationToken ct)
    {
        Record($"HandleNoteAndToken(NotePayload, ct): {note}");
        await Task.Delay(200, ct);
    }

    // 返回值的强类型函数体：属性是 IVeloxCommand<NotePayload, int>，实参与结果都有类型，
    // await MeasureNoteCommand.ExecuteAsync(note, ct) 直接得到重量。
    [VeloxCommand]
    private Task<int> MeasureNote(NotePayload note)
    {
        Record($"MeasureNote(NotePayload): {note}");
        return Task.FromResult(note.Weight);
    }

    // 值类型形参同样是强类型的，但经过管线时仍会装箱，因为队列以 object 携带实参。
    [VeloxCommand(canValidate: true)]
    private void NotifyWeight(int weight) => Record($"NotifyWeight(int): {weight}");

    private partial bool CanExecuteNotifyWeightCommand(int weight) => weight > 0;

    // 强类型，函数体是 ValueTask。
    [VeloxCommand]
    private ValueTask StepMagnitude(double magnitude)
    {
        Record($"StepMagnitude(double): {magnitude}");
        return default;
    }

    // ── Method type parameter: a generated accessor, not a property ────────

    // 类型参数属于方法，属性带不了它，生成器改为发出 IVeloxCommand<T> GetStoreCommand<T>()。
    // 每个封闭的 T 各有一条命令，各有自己的队列、锁与并发上限。
    [VeloxCommand(canValidate: true)]
    private Task Store<T>(T value)
    {
        Record($"GetStoreCommand<{typeof(T).Name}>(): {value}");
        return Task.CompletedTask;
    }

    // 泛型命令的校验器带相同的类型参数与约束，并照抄源方法的形参名（Store<T>(T value) ⇒ value）。
    private partial bool CanExecuteStoreCommand<T>(T value) => value is not null;

    // 类型参数嵌在形参类型里 —— 数组与元组两种写法都可以。
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
    // 用类自己的类型实参运行，签名里不装箱。
    [VeloxCommand]
    private Task Accept(T value)
    {
        _ = value;
        return Task.CompletedTask;
    }
}
