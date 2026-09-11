using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test.TransitionSystem;

/// <summary>
/// Non-mutual schedulers register and unregister themselves from whichever thread started the animation — a
/// background <c>Task.Run</c> and a UI-thread click do it at the same time. Losing a registration under that
/// concurrency leaves an animation that <c>Transition.Exit</c> can no longer find and cancel, so it keeps running
/// after a reset.
/// </summary>
[TestClass]
public class NoMutualSchedulerRegistryTests
{
    private sealed class FakeScheduler : ITransitionSchedulerCore
    {
        public Task Execute(InterpolatorCore producer, IFrameState state, ITransitionEffectCore effect, CancellationTokenSource? externCts = default)
            => Task.CompletedTask;

        public void Exit() { }
    }

    [TestMethod]
    public void AddNoMutual_ConcurrentRegistrations_NoneAreLost()
    {
        var target = new object();
        const int count = 512;
        var schedulers = Enumerable.Range(0, count).Select(_ => new FakeScheduler()).Cast<ITransitionSchedulerCore>().ToArray();

        Parallel.For(0, count, index => TransitionCore.AddNoMutual(target, [schedulers[index]]));

        Assert.IsTrue(TransitionSchedulerCore.TryGetNoMutualScheduler(target, out var registered));
        Assert.AreEqual(count, registered.Length);
    }

    [TestMethod]
    public void RemoveNoMutual_InterleavedWithAdds_LeavesTheRestIntact()
    {
        var target = new object();
        const int count = 512;
        var schedulers = Enumerable.Range(0, count).Select(_ => new FakeScheduler()).Cast<ITransitionSchedulerCore>().ToArray();

        Parallel.For(0, count, index =>
        {
            TransitionCore.AddNoMutual(target, [schedulers[index]]);
            if (index % 2 == 0)
            {
                TransitionCore.RemoveNoMutual(target, [schedulers[index]]);
            }
        });

        Assert.IsTrue(TransitionSchedulerCore.TryGetNoMutualScheduler(target, out var registered));
        Assert.AreEqual(count / 2, registered.Length);
    }
}
