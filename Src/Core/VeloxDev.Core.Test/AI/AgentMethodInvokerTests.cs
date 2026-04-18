using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentMethodInvokerTests
{
    private sealed class Calculator
    {
        public int Add(int a, int b) => a + b;
        public string Greet(string name) => $"Hello, {name}!";
        public void SideEffect() { WasCalled = true; }
        public bool WasCalled { get; private set; }
        public int WithDefault(int a, int b = 10) => a + b;
    }

    [TestMethod]
    public void DiscoverMethods_ReturnsPublicMethods()
    {
        var target = new Calculator();
        var methods = AgentMethodInvoker.DiscoverMethods(target);
        Assert.IsTrue(methods.Any(m => m.Name == "Add"));
        Assert.IsTrue(methods.Any(m => m.Name == "Greet"));
        Assert.IsTrue(methods.Any(m => m.Name == "SideEffect"));
    }

    [TestMethod]
    public void DiscoverMethods_ExcludesObjectMethods()
    {
        var target = new Calculator();
        var methods = AgentMethodInvoker.DiscoverMethods(target);
        Assert.IsFalse(methods.Any(m => m.Name == "ToString"));
        Assert.IsFalse(methods.Any(m => m.Name == "GetHashCode"));
        Assert.IsFalse(methods.Any(m => m.Name == "Equals"));
        Assert.IsFalse(methods.Any(m => m.Name == "GetType"));
    }

    [TestMethod]
    public void DiscoverMethods_NullTarget_ReturnsEmpty()
    {
        var methods = AgentMethodInvoker.DiscoverMethods(null!);
        Assert.AreEqual(0, methods.Count);
    }

    [TestMethod]
    public void DiscoverMethods_WithFilter_Filters()
    {
        var target = new Calculator();
        var methods = AgentMethodInvoker.DiscoverMethods(target, filter: m => m.Name == "Add");
        Assert.AreEqual(1, methods.Count);
        Assert.AreEqual("Add", methods[0].Name);
    }

    [TestMethod]
    public void DiscoverMethods_ParameterInfo_IsCorrect()
    {
        var target = new Calculator();
        var methods = AgentMethodInvoker.DiscoverMethods(target);
        var add = methods.First(m => m.Name == "Add");
        Assert.AreEqual(2, add.Parameters.Count);
        Assert.AreEqual("a", add.Parameters[0].Name);
        Assert.AreEqual(typeof(int), add.Parameters[0].ParameterType);
    }

    [TestMethod]
    public void Invoke_ValidMethod_Succeeds()
    {
        var target = new Calculator();
        var result = AgentMethodInvoker.Invoke(target, "Add", 3, 7);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(10, result.ReturnValue);
    }

    [TestMethod]
    public void Invoke_StringReturn_Works()
    {
        var target = new Calculator();
        var result = AgentMethodInvoker.Invoke(target, "Greet", "World");
        Assert.IsTrue(result.Success);
        Assert.AreEqual("Hello, World!", result.ReturnValue);
    }

    [TestMethod]
    public void Invoke_VoidMethod_Succeeds()
    {
        var target = new Calculator();
        var result = AgentMethodInvoker.Invoke(target, "SideEffect");
        Assert.IsTrue(result.Success);
        Assert.IsTrue(target.WasCalled);
    }

    [TestMethod]
    public void Invoke_NonExistentMethod_Fails()
    {
        var target = new Calculator();
        var result = AgentMethodInvoker.Invoke(target, "NotAMethod");
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Invoke_NullTarget_Fails()
    {
        var result = AgentMethodInvoker.Invoke(null!, "Add");
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void Invoke_WithDefaultParam_PadsDefaults()
    {
        var target = new Calculator();
        var result = AgentMethodInvoker.Invoke(target, "WithDefault", 5);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(15, result.ReturnValue);
    }

    [TestMethod]
    public void InvokeStatic_NullType_Fails()
    {
        var result = AgentMethodInvoker.InvokeStatic(null!, "Whatever");
        Assert.IsFalse(result.Success);
    }
}
