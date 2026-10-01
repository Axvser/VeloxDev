using System.Threading;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

/// <summary>
/// The payload every stage carries. <see cref="CommandEventArgs.With"/> is how one execution projects itself
/// onto its next stage, so it decides what a handler can still see once the stage changes — in particular,
/// whether a failure leaks onto stages that did not fail.
/// </summary>
[TestClass]
public class CommandEventArgsTests
{
    [TestMethod]
    public void With_KeepsTheParameterAndTheCancellationSource()
    {
        using var cts = new CancellationTokenSource();
        var args = new CommandEventArgs("payload", CommandEventType.Created, cts: cts);

        var next = args.With(CommandEventType.Started);

        Assert.AreEqual("payload", next.Parameter, "every stage of one execution carries the same argument");
        Assert.AreSame(cts, next.Cts, "and the same cancellation source");
        Assert.AreEqual(CommandEventType.Started, next.EventType);
    }

    [TestMethod]
    public void With_CarriesAnExplicitFailure()
    {
        var boom = new InvalidOperationException("boom");
        var args = new CommandEventArgs(null, CommandEventType.Created);

        Assert.AreSame(boom, args.With(CommandEventType.Failed, boom).Exception);
    }

    [TestMethod]
    public void With_KeepsTheReceiversFailure_UnlessAnotherIsGiven()
    {
        var boom = new InvalidOperationException("boom");
        var other = new InvalidOperationException("other");
        var args = new CommandEventArgs(null, CommandEventType.Failed, ex: boom);

        Assert.AreSame(boom, args.With(CommandEventType.Exited).Exception,
            "With projects the whole instance, so an omitted failure falls back to the receiver's");
        Assert.AreSame(other, args.With(CommandEventType.Exited, other).Exception);
    }

    [TestMethod]
    public void With_OnAnInstanceThatNeverFailed_ProducesNoFailure()
    {
        // 命令自己永远从这个形态投影：出厂 args 没有异常，且全程不被改写，
        // 所以只有 Failed 这一个阶段会带着异常出来。
        var args = new CommandEventArgs(null, CommandEventType.Created);

        Assert.IsNull(args.With(CommandEventType.Exited).Exception);
    }

    [TestMethod]
    public void With_ProjectsOntoANewInstance_LeavingTheReceiverUntouched()
    {
        var args = new CommandEventArgs(null, CommandEventType.Created);

        _ = args.With(CommandEventType.Completed);

        Assert.AreEqual(CommandEventType.Created, args.EventType, "With never mutates the receiver");
    }

    [TestMethod]
    public void AnArgumentFreeStage_CarriesNoExceptionAndNoSource()
    {
        var args = new CommandEventArgs(null, CommandEventType.Created);

        Assert.IsNull(args.Exception);
        Assert.IsNull(args.Cts, "a command whose body never receives a token hands out no source");
    }
}
