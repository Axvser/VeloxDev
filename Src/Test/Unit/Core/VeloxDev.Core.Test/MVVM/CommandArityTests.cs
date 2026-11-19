using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Commands with more than one parameter, one per arity the generator can produce.
/// </summary>
public partial class CommandArityViewModel
{
    internal List<string> Ran { get; } = [];

    [VeloxCommand]
    private Task<int> Add(int left, int right)
    {
        Ran.Add($"{left}+{right}");
        return Task.FromResult(left + right);
    }

    [VeloxCommand]
    private void Record(string label, int count) => Ran.Add($"{label}:{count}");

    [VeloxCommand]
    private Task Combine(int a, string b, CancellationToken ct)
    {
        _ = ct;
        Ran.Add($"{a}{b}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Commands with more than one parameter — the arity family in <c>CommandArities.cs</c>.
/// </summary>
/// <remarks>
/// The public surface stays tuple-free: the caller passes the arguments, and the packing into a
/// <see cref="ValueTuple"/> happens between the facade and the pipeline. These tests hold each command through
/// its arity-typed interface, so a facade that stopped compiling would fail here rather than in a string
/// comparison.
/// </remarks>
[DoNotParallelize]
[TestClass]
public class CommandArityTests
{
    [TestMethod]
    public async Task ATwoParameterCommand_ReturnsItsValueThroughTheTypedInterface()
    {
        var vm = new CommandArityViewModel();
        IVeloxCommand<int, int, int> typed = vm.AddCommand;

        var sum = await typed.ExecuteAsync(2, 3, CancellationToken.None);

        Assert.AreEqual(5, sum);
        CollectionAssert.AreEqual(new[] { "2+3" }, vm.Ran);
    }

    [TestMethod]
    public void ATwoParameterVoidCommand_RunsThroughTheTypedInterface()
    {
        var vm = new CommandArityViewModel();
        IVeloxCommand<string, int, object?> typed = vm.RecordCommand;

        typed.Execute("label", 7);

        Assert.IsTrue(typed.CanExecute("label", 7));
        Assert.IsNotNull(typed);
    }

    [TestMethod]
    public async Task AThreeParameterCommandWithAToken_Runs()
    {
        var vm = new CommandArityViewModel();
        IVeloxCommand<int, string, object?> typed = vm.CombineCommand;

        await typed.ExecuteAsync(4, "x", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "4x" }, vm.Ran);
    }

    [TestMethod]
    public void EveryArityCommandIsAlsoABindableFallbackCommand()
    {
        // 每个门面都还是 ICommand —— 绑定走的是同一条管道的那副 object? 面孔。
        var vm = new CommandArityViewModel();
        System.Windows.Input.ICommand bindable = vm.AddCommand;

        Assert.IsNotNull(bindable);
        Assert.IsFalse(bindable.CanExecute(42),
            "a boxed argument that is not the command's tuple is not executable, and reading it must not throw");
    }

    [TestMethod]
    public void ATwoParameterValueCommand_DoesNotAllocateMoreThanTheSameShapeOverReferenceTypes()
    {
        var value = Measure(static () =>
            new VeloxCommand<int, int, int>(static (a, b, _) => Task.FromResult(a + b)), 3, 4);
        var reference = Measure(static () =>
            new VeloxCommand<Payload, Payload, Payload>(static (a, b, _) => Task.FromResult(a)), Payload.Shared, Payload.Shared);

        Assert.IsGreaterThan(0L, reference, "the measurement has to be reading something");
        Assert.IsTrue(
            value <= reference,
            $"the value-typed pair allocated {value} B, the reference-typed one {reference} B — the facade is not supposed to box");
    }

    private sealed class Payload
    {
        internal static readonly Payload Shared = new();
    }

    private const int Executions = 4096;

    private static long Measure<T1, T2, TR>(Func<VeloxCommand<T1, T2, TR>> factory, T1 a, T2 b)
    {
        // 每次测量都新建命令：第一次执行会惰性构造命令对象，那笔分配不该算进任何一侧。
        var command = factory();

        for (var i = 0; i < 512; i++)
        {
            _ = command.ExecuteAsync(a, b, CancellationToken.None).GetAwaiter().GetResult();
        }

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < Executions; i++)
        {
            _ = command.ExecuteAsync(a, b, CancellationToken.None).GetAwaiter().GetResult();
        }

        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }
}
