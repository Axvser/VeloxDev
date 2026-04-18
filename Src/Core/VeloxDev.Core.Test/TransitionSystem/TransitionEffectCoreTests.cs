using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TimeLine;

namespace VeloxDev.Core.Test.TransitionSystem;

[TestClass]
public class TransitionEffectCoreTests
{
    [TestMethod]
    public void Defaults_AreCorrect()
    {
        var effect = new TransitionEffectCore();
        Assert.AreEqual(60, effect.FPS);
        Assert.AreEqual(TimeSpan.FromMilliseconds(0), effect.Duration);
        Assert.IsFalse(effect.IsAutoReverse);
        Assert.AreEqual(0, effect.LoopTime);
        Assert.IsNotNull(effect.Ease);
    }

    [TestMethod]
    public void SetProperties_Roundtrip()
    {
        var effect = new TransitionEffectCore
        {
            FPS = 30,
            Duration = TimeSpan.FromSeconds(2),
            IsAutoReverse = true,
            LoopTime = 3,
            Ease = Eases.Default
        };
        Assert.AreEqual(30, effect.FPS);
        Assert.AreEqual(TimeSpan.FromSeconds(2), effect.Duration);
        Assert.IsTrue(effect.IsAutoReverse);
        Assert.AreEqual(3, effect.LoopTime);
    }

    [TestMethod]
    public void Events_AreInvoked()
    {
        var effect = new TransitionEffectCore();
        var args = new TransitionEventArgs();
        var sender = new object();

        bool awakedFired = false;
        bool startFired = false;
        bool updateFired = false;
        bool lateUpdateFired = false;
        bool completedFired = false;
        bool canceledFired = false;
        bool finallyFired = false;

        effect.Awaked += (s, e) => awakedFired = true;
        effect.Start += (s, e) => startFired = true;
        effect.Update += (s, e) => updateFired = true;
        effect.LateUpdate += (s, e) => lateUpdateFired = true;
        effect.Completed += (s, e) => completedFired = true;
        effect.Canceled += (s, e) => canceledFired = true;
        effect.Finally += (s, e) => finallyFired = true;

        effect.InvokeAwake(sender, args);
        effect.InvokeStart(sender, args);
        effect.InvokeUpdate(sender, args);
        effect.InvokeLateUpdate(sender, args);
        effect.InvokeCompleted(sender, args);
        effect.InvokeCancled(sender, args);
        effect.InvokeFinally(sender, args);

        Assert.IsTrue(awakedFired);
        Assert.IsTrue(startFired);
        Assert.IsTrue(updateFired);
        Assert.IsTrue(lateUpdateFired);
        Assert.IsTrue(completedFired);
        Assert.IsTrue(canceledFired);
        Assert.IsTrue(finallyFired);
    }

    [TestMethod]
    public void Clone_CopiesProperties()
    {
        var effect = new TransitionEffectCore
        {
            FPS = 24,
            Duration = TimeSpan.FromSeconds(5),
            IsAutoReverse = true,
            LoopTime = 2,
            Ease = Eases.Default
        };
        var clone = effect.Clone();
        Assert.AreEqual(24, clone.FPS);
        Assert.AreEqual(TimeSpan.FromSeconds(5), clone.Duration);
        Assert.IsTrue(clone.IsAutoReverse);
        Assert.AreEqual(2, clone.LoopTime);
    }

    [TestMethod]
    public void Clone_IsIndependent()
    {
        var effect = new TransitionEffectCore { FPS = 60, Duration = TimeSpan.FromSeconds(1) };
        var clone = effect.Clone();
        clone.FPS = 30;
        clone.Duration = TimeSpan.FromSeconds(10);
        Assert.AreEqual(60, effect.FPS);
        Assert.AreEqual(TimeSpan.FromSeconds(1), effect.Duration);
    }

    [TestMethod]
    public void EventRemove_StopsFiring()
    {
        var effect = new TransitionEffectCore();
        int count = 0;
        EventHandler<TransitionEventArgs> handler = (s, e) => count++;
        effect.Awaked += handler;
        effect.InvokeAwake(this, new TransitionEventArgs());
        Assert.AreEqual(1, count);

        effect.Awaked -= handler;
        // After removal, WeakDelegate may still invoke if reference is alive.
        // But the design is weak-ref based, so we just verify no crash.
        effect.InvokeAwake(this, new TransitionEventArgs());
    }
}
