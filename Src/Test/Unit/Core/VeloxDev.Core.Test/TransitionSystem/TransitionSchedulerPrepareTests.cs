using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// A Prepare that is stuck costs only its own animation.
/// </summary>
/// <remarks>
/// Prepare reads the target, and a read can block — a host marshals it to a UI thread that may be busy. That read
/// runs inside <c>TransitionSchedulerCore.Execute</c>, under that scheduler's gate but <em>not</em> under the target
/// lock, which <c>CoreExecute</c> has already released. A non-mutual animation on the same target has a scheduler of
/// its own, and therefore a gate of its own, which is what this pins.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class TransitionSchedulerPrepareTests
{
    private sealed class Target
    {
        public double Value { get; set; }
    }

    private sealed class TestInterpreter : TransitionInterpreterCore<TransitionEffectCore>
    {
    }

    private sealed class TestInterpolator : InterpolatorCore
    {
    }

    /// <summary>Parks the first read it is asked for, and lets every later one through.</summary>
    private sealed class BlockingOnceHost : ImmediateHost
    {
        private static readonly SemaphoreSlim Entered = new(0);
        private static readonly SemaphoreSlim Released = new(0);
        private static int _calls;

        public static void Reset()
        {
            Interlocked.Exchange(ref _calls, 0);
            while (Entered.Wait(0)) { }
            while (Released.Wait(0)) { }
        }

        public static bool WaitUntilParked(TimeSpan timeout) => Entered.Wait(timeout);

        public static void Unblock() => Released.Release();

        public override T Run<T>(object target, Func<T> body)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.Release();
                Released.Wait(TimeSpan.FromSeconds(10));
            }

            return body();
        }
    }

    private static TransitionSchedulerCore<BlockingOnceHost, TestInterpreter, NonPriority> SchedulerFor(Target target, bool mutual)
        => (TransitionSchedulerCore<BlockingOnceHost, TestInterpreter, NonPriority>)
           TransitionSchedulerCore<BlockingOnceHost, TestInterpreter, NonPriority>.FindOrCreate(target, mutual);

    private static StateCore StateFor()
    {
        var state = new StateCore();
        state.SetValue<Target, double>(t => t.Value, 100d);
        return state;
    }

    [TestMethod]
    public async Task AStuckPrepareDoesNotHoldUpANonMutualAnimationOnTheSameTarget()
    {
        BlockingOnceHost.Reset();

        var target = new Target();
        var effect = new TransitionEffectCore { Duration = TimeSpan.Zero };

        using var firstToken = new CancellationTokenSource();
        var parked = SchedulerFor(target, mutual: true).Execute(new TestInterpolator(), StateFor(), effect, firstToken);

        Assert.IsTrue(BlockingOnceHost.WaitUntilParked(TimeSpan.FromSeconds(5)),
            "the first animation must be parked inside its Prepare");

        using var secondToken = new CancellationTokenSource();
        var other = SchedulerFor(target, mutual: false).Execute(new TestInterpolator(), StateFor(), effect, secondToken);

        // 它走自己的调度器、自己的闸门，所以哪怕第一条还卡在里面，这一条也该跑完。
        await other.WaitAsync(TimeSpan.FromSeconds(5));

        BlockingOnceHost.Unblock();
        await parked.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
