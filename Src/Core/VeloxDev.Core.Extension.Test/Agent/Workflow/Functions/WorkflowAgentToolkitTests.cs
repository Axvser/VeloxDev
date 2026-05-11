using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

[TestClass]
public class WorkflowAgentToolkitTests
{
    [TestMethod]
    public void MarkDirty_MarksTreeDirty()
    {
        var tree = new TreeViewModelBase();
        var helper = new TestTreeHelper();
        tree.SetHelper(helper);
        var toolkit = new WorkflowAgentToolkit(new WorkflowAgentScope(tree));

        var result = InvokeTool(toolkit, "MarkDirty");
        var json = JObject.Parse(result);

        Assert.AreEqual("ok", json["status"]?.Value<string>());
        Assert.AreEqual(1, helper.MarkDirtyCount);
    }

    private static string InvokeTool(WorkflowAgentToolkit toolkit, string toolName, params (string Name, object? Value)[] args)
    {
        var method = typeof(WorkflowAgentToolkit)
            .GetMethod(toolName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(method, $"Tool method '{toolName}' was not found.");

        var parameters = method.GetParameters();
        var invocationArgs = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var match = args.FirstOrDefault(a => string.Equals(a.Name, parameters[i].Name, StringComparison.OrdinalIgnoreCase));
            invocationArgs[i] = match == default ? parameters[i].DefaultValue : match.Value;
        }

        var raw = method.Invoke(toolkit, invocationArgs);
        Assert.IsInstanceOfType<string>(raw);

        var trackMethod = typeof(WorkflowAgentToolkit)
            .GetMethod("Track", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(trackMethod, "Track method was not found.");

        var tracked = trackMethod.Invoke(toolkit, [toolName, (string)raw!]);
        Assert.IsInstanceOfType<string>(tracked);
        return (string)tracked!;
    }

    private sealed class TestTreeHelper : TreeHelper
    {
        public int MarkDirtyCount { get; private set; }

        public override void MarkDirty()
        {
            MarkDirtyCount++;
            base.MarkDirty();
        }
    }
}
