using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Proves that the strongly typed entry points do not box, by measuring rather than by asserting.
/// <para>
/// The two commands compared here are structurally identical — same pipeline, same body shape, same queue and
/// lock — and differ only in whether the parameter and the result are a value type. A value type that is boxed
/// anywhere on the path shows up as a per-execution allocation the reference-type run does not pay.
/// </para>
/// </summary>
/// <remarks>
/// Process-wide rather than per-thread allocation is measured on purpose: the pipeline completes continuations on
/// the thread pool, so a per-thread counter would miss the very allocation this is looking for.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class CommandBoxingTests
{
    private const int Warmup = 512;
    private const int Executions = 4096;

    /// <summary>The reference-typed control. Nothing is special about it beyond being a class.</summary>
    private sealed class Payload
    {
        internal static readonly Payload Shared = new();
    }

    [TestMethod]
    public void AValueTypedCommand_DoesNotAllocateMoreThanTheSameShapeOverAReferenceType()
    {
        var value = Measure<int>(payload: 7);
        var reference = Measure<Payload>(Payload.Shared);

        // 先确认两边都真的测到了东西 —— 否则 0 <= 0 也会让下面那条通过。
        Assert.IsGreaterThan(0L, reference, "the measurement has to be reading something");

        // 两边结构完全一样，唯一差别是值类型会不会装箱。一个 int 装箱约 24 B；每次执行一次就足以显形。
        Assert.IsTrue(
            value <= reference,
            $"a value-typed execution allocated {value} B over {Executions} runs, a reference-typed one {reference} B — the difference is what boxing costs");
    }

    [TestMethod]
    public void TheTypedEntryPoint_AllocatesLessThanTheBoxedOneOnTheSameInstance()
    {
        var command = new VeloxCommand<int, int>(static (parameter, _) => Task.FromResult(parameter));

        var typed = MeasureCalls(Executions, () =>
        {
            _ = command.ExecuteAsync(42, CancellationToken.None).GetAwaiter().GetResult();
        });

        var boxed = MeasureCalls(Executions, () =>
        {
            _ = ((IVeloxCommandResult)command).ExecuteAsync((object?)42, CancellationToken.None).GetAwaiter().GetResult();
        });

        Assert.IsGreaterThan(0L, typed, "the measurement has to be reading something");

        Assert.IsTrue(
            typed < boxed,
            $"the typed entry point allocated {typed} B and the boxed one {boxed} B — the typed path is supposed to be the cheaper one");
    }

    private static long Measure<T>(T payload)
    {
        // 每次测量都新建命令：第一次执行会惰性构造命令对象，那笔分配不该算进任何一侧。
        var command = new VeloxCommand<T, T>(static (parameter, _) => Task.FromResult(parameter));

        for (var i = 0; i < Warmup; i++)
        {
            _ = command.ExecuteAsync(payload, CancellationToken.None).GetAwaiter().GetResult();
        }

        return MeasureCalls(Executions, () =>
        {
            _ = command.ExecuteAsync(payload, CancellationToken.None).GetAwaiter().GetResult();
        });
    }

    private static long MeasureCalls(int executions, Action call)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);

        for (var i = 0; i < executions; i++)
        {
            call();
        }

        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }
}
