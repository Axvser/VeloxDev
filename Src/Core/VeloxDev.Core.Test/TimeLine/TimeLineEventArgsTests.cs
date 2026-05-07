using System;
using VeloxDev.TimeLine;

namespace VeloxDev.Core.Test.TimeLine;

[TestClass]
public class TimeLineEventArgsTests
{
    // ───────── TransitionEventArgs ─────────

    [TestMethod]
    public void TransitionEventArgs_Handled_DefaultFalse()
    {
        var args = new TransitionEventArgs();
        Assert.IsFalse(args.Handled);
    }

    [TestMethod]
    public void TransitionEventArgs_Handled_SetTrue()
    {
        var args = new TransitionEventArgs { Handled = true };
        Assert.IsTrue(args.Handled);
    }

    // ───────── FrameEventArgs ─────────

    [TestMethod]
    public void FrameEventArgs_DefaultValues()
    {
        var args = new FrameEventArgs();
        Assert.AreEqual(TimeSpan.Zero, args.DeltaTime);
        Assert.AreEqual(TimeSpan.Zero, args.TotalTime);
        Assert.AreEqual(0, args.CurrentFPS);
        Assert.AreEqual(0, args.TargetFPS);
        Assert.IsFalse(args.Handled);
    }

    // ───────── ThreadSafeFrameEventArgs ─────────

    [TestMethod]
    public void ThreadSafeFrameEventArgs_Handled_DefaultFalse()
    {
        var args = new ThreadSafeFrameEventArgs();
        Assert.IsFalse(args.Handled);
    }

    [TestMethod]
    public void ThreadSafeFrameEventArgs_Handled_ThreadSafe()
    {
        var args = new ThreadSafeFrameEventArgs();
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                args.Handled = true;
                _ = args.Handled;
                args.Handled = false;
            }));
        }
        Task.WaitAll(tasks.ToArray());
        // No exception = thread-safe access works
    }

    [TestMethod]
    public void ThreadSafeFrameEventArgs_Handled_SetAndGet()
    {
        var args = new ThreadSafeFrameEventArgs();
        args.Handled = true;
        Assert.IsTrue(args.Handled);
        args.Handled = false;
        Assert.IsFalse(args.Handled);
    }
}
