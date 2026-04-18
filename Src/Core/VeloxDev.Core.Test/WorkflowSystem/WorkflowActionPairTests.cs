using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class WorkflowActionPairTests
{
    [TestMethod]
    public void Constructor_StoresActions()
    {
        int counter = 0;
        var pair = new WorkflowActionPair(() => counter++, () => counter--);

        pair.Redo();
        Assert.AreEqual(1, counter);

        pair.Undo();
        Assert.AreEqual(0, counter);
    }

    [TestMethod]
    public void Redo_And_Undo_CanBeCalledMultipleTimes()
    {
        int counter = 0;
        var pair = new WorkflowActionPair(() => counter += 10, () => counter -= 10);

        pair.Redo();
        pair.Redo();
        Assert.AreEqual(20, counter);

        pair.Undo();
        Assert.AreEqual(10, counter);
    }
}
