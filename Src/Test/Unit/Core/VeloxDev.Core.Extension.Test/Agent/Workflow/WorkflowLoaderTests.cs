using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// <c>LoadWorkflow</c> replaces the scope's tree with the host's own restored object and then tells the host
/// through <c>TreeChanged</c> so it can rebind its surface. Both halves are the contract: without the loader
/// call the host's concrete tree type is lost, and without the event the canvas keeps pointing at the old one.
/// </summary>
[TestClass]
public class WorkflowLoaderTests
{
    [TestMethod]
    public void LoadWorkflow_UsesTheHostLoader_AndRaisesTreeChanged()
    {
        var original = new TreeDefaultViewModel();
        original.GetHelper().CreateNode(new NodeDefaultViewModel());
        original.GetHelper().CreateNode(new NodeDefaultViewModel());
        var document = original.Serialize();

        var replacement = new TreeDefaultViewModel();
        replacement.GetHelper().CreateNode(new NodeDefaultViewModel());

        string? seen = null;
        var scope = new WorkflowAgentScope(original).WithWorkflowLoader(json =>
        {
            seen = json;
            return replacement;
        });

        var changed = 0;
        scope.TreeChanged += (_, _) => changed++;

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "LoadWorkflow", ("json", document)));

        Assert.AreEqual("ok", answer["status"]?.Value<string>(), answer.ToString());
        Assert.AreEqual(document, seen, "the host's loader receives the document unchanged");
        Assert.AreSame(replacement, scope.Tree, "the scope adopts the loader's object, not a second deserialize");
        Assert.AreEqual(1, changed, "the host is told exactly once so it can rebind its surface");
        Assert.HasCount(1, scope.Tree.Nodes, "the tools follow the replacement immediately");
    }

    [TestMethod]
    public void LoadWorkflow_WithoutALoader_FallsBackToTreeDefaultViewModel()
    {
        var original = new TreeDefaultViewModel();
        original.GetHelper().CreateNode(new NodeDefaultViewModel());
        var document = original.Serialize();

        var scope = new WorkflowAgentScope(original);
        var changed = 0;
        scope.TreeChanged += (_, _) => changed++;

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "LoadWorkflow", ("json", document)));

        Assert.AreEqual("ok", answer["status"]?.Value<string>(), answer.ToString());
        Assert.IsInstanceOfType<TreeDefaultViewModel>(scope.Tree);
        Assert.AreEqual(1, changed);
        Assert.HasCount(1, scope.Tree.Nodes);
    }

    [TestMethod]
    public void LoadWorkflow_ReportsTheNodeCountOfTheRestoredTree()
    {
        var original = new TreeDefaultViewModel();
        original.GetHelper().CreateNode(new NodeDefaultViewModel());
        original.GetHelper().CreateNode(new NodeDefaultViewModel());
        original.GetHelper().CreateNode(new NodeDefaultViewModel());
        var scope = new WorkflowAgentScope(original);

        var answer = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "LoadWorkflow", ("json", original.Serialize())));

        Assert.AreEqual(3, answer["nodeCount"]?.Value<int>(), answer.ToString());
        Assert.AreEqual(true, answer["requiresHostRebind"]?.Value<bool>());
    }
}
