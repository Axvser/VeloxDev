using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The workflow-side command path: what it lists, and how it reports a miss.
/// </summary>
/// <remarks>
/// This path is its own copy — it does not share a line with Core's <c>AgentCommandDiscoverer</c> — and it
/// reads the context tree now, so the interface-declared command annotations have to survive the hop from the
/// interface onto the implementing type's entry.
/// </remarks>
[TestClass]
public class CommandInvokerTests
{
    [TestMethod]
    public void DiscoveryCarriesTheParameterTypeTheInterfaceDeclared()
    {
        var commands = CommandInvoker.DiscoverCommands(new TreeDefaultViewModel());

        var create = commands.First(c => c.Name == nameof(TreeDefaultViewModel.CreateNodeCommand));
        Assert.AreEqual(typeof(IWorkflowNodeViewModel), create.ParameterType);

        // 说明文字写在 IWorkflowTreeViewModel 上，实现类的那个属性自己是空的。
        Assert.IsTrue(
            create.Descriptions.Any(d => d.Value.Contains("Create node command")),
            "interface-declared descriptions must reach the implementing type's command entry");
    }

    [TestMethod]
    public void ACommandWithoutAParameterTypeReportsNone()
    {
        var commands = CommandInvoker.DiscoverCommands(new TreeDefaultViewModel());

        // [AgentCommandParameter] 不带参数 = 「不吃参数」，与「没标注」在这一列上同样是 null。
        var redo = commands.First(c => c.Name == "RedoCommand");
        Assert.IsNull(redo.ParameterType);
    }

    [TestMethod]
    public void ATargetOutsideTheTreeListsNothing()
    {
        Assert.AreEqual(0, CommandInvoker.DiscoverCommands(new object()).Count);
    }

    [TestMethod]
    public void AnUnknownCommandIsReportedRatherThanThrown()
    {
        var result = JObject.Parse(CommandInvoker.Invoke(new TreeDefaultViewModel(), "NoSuchCommand", null));

        Assert.AreEqual("error", result["status"]?.Value<string>());
        StringAssert.Contains(result["message"]?.Value<string>() ?? string.Empty, "not found");
    }
}
