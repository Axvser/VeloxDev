using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// Guards the one allocation property the command pipeline is supposed to have: a stage nobody subscribes to must
/// not cost anything. The lifecycle args are built per stage, so making that build unconditional again would undo
/// it silently — nothing else in the suite would notice.
/// <para>
/// Deliberately relative (fewer subscribers ⇒ fewer bytes) rather than an absolute figure, so it does not go
/// stale when the runtime or the surrounding code changes shape.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]   // 量的是进程级 GC.GetTotalAllocatedBytes，并行时别的方法的分配会落进测量窗口
public class CommandAllocationTests
{
    private const int Iterations = 200_000;

    [TestMethod]
    public void ACommandWithNoSubscribers_AllocatesLessThanOneWithEveryEventSubscribed()
    {
        var quiet = new VeloxCommand(() => { });
        var loud = new VeloxCommand(() => { });
        loud.Created += Noop;
        loud.Enqueued += Noop;
        loud.Dequeued += Noop;
        loud.Started += Noop;
        loud.Completed += Noop;
        loud.Failed += Noop;
        loud.Canceled += Noop;
        loud.Exited += Noop;

        var quietBytes = Measure(quiet);
        var loudBytes = Measure(loud);

        Assert.IsTrue(
            quietBytes < loudBytes,
            $"a stage with no subscriber must not build its args: {quietBytes} B/execution vs {loudBytes} B");
    }

    private static long Measure(VeloxCommand command)
    {
        command.Execute(null);                                  // 预热，避开 JIT 与首次分配
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < Iterations; i++)
        {
            command.Execute(null);
        }

        return (GC.GetTotalAllocatedBytes(precise: true) - before) / Iterations;
    }

    private static void Noop(CommandEventArgs e) => _ = e;
}
