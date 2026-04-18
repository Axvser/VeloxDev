using VeloxDev.MVVM;

namespace VeloxDev.Core.Test.MVVM;

[TestClass]
public class VeloxCommandTests
{
    [TestMethod]
    public async Task Execute_SyncAction_Completes()
    {
        bool called = false;
        var cmd = new VeloxCommand(() => called = true);

        cmd.Execute(null);
        await Task.Delay(100);
        Assert.IsTrue(called);
    }

    [TestMethod]
    public async Task Execute_ActionWithParameter_ReceivesParameter()
    {
        object? received = null;
        var cmd = new VeloxCommand((Action<object?>)(p => received = p));

        cmd.Execute("hello");
        await Task.Delay(100);
        Assert.AreEqual("hello", received);
    }

    [TestMethod]
    public async Task Execute_AsyncFunc_Completes()
    {
        bool called = false;
        var cmd = new VeloxCommand(async () =>
        {
            await Task.Delay(10);
            called = true;
        });

        cmd.Execute(null);
        await Task.Delay(200);
        Assert.IsTrue(called);
    }

    [TestMethod]
    public void CanExecute_NoPredicate_ReturnsTrue()
    {
        var cmd = new VeloxCommand(() => { });
        Assert.IsTrue(cmd.CanExecute(null));
    }

    [TestMethod]
    public void CanExecute_WithPredicate_RespectsIt()
    {
        var cmd = new VeloxCommand(() => { }, canExecute: p => p is string s && s == "yes");
        Assert.IsTrue(cmd.CanExecute("yes"));
        Assert.IsFalse(cmd.CanExecute("no"));
        Assert.IsFalse(cmd.CanExecute(null));
    }

    [TestMethod]
    public void Constructor_NullCommand_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new VeloxCommand((Func<object?, CancellationToken, Task>)null!));
    }

    [TestMethod]
    public void CreateTaskOnlyWithParameter_Works()
    {
        object? received = null;
        var cmd = VeloxCommand.CreateTaskOnlyWithParameter(async p =>
        {
            received = p;
            await Task.CompletedTask;
        });

        cmd.Execute("test");
        // Give async a moment
        Thread.Sleep(100);
        Assert.AreEqual("test", received);
    }
}
