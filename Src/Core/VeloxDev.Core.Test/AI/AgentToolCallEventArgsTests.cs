using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentToolCallEventArgsTests
{
    [TestMethod]
    public void Constructor_SetsProperties()
    {
        var args = new AgentToolCallEventArgs("MyTool", "result", 5);
        Assert.AreEqual("MyTool", args.ToolName);
        Assert.AreEqual("result", args.Result);
        Assert.AreEqual(5, args.CallCount);
    }

    [TestMethod]
    public void Constructor_NullToolName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentToolCallEventArgs(null!, "result", 1));
    }

    [TestMethod]
    public void Constructor_NullResult_DefaultsToEmpty()
    {
        var args = new AgentToolCallEventArgs("Tool", null!, 0);
        Assert.AreEqual(string.Empty, args.Result);
    }

    [TestMethod]
    public void InheritsFromEventArgs()
    {
        var args = new AgentToolCallEventArgs("Tool", "r", 1);
        Assert.IsInstanceOfType<EventArgs>(args);
    }
}
