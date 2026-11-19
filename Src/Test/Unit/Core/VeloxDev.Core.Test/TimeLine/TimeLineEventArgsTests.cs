using System;
using VeloxDev.TimeLine;
using VeloxDev.TransitionSystem;

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

}
