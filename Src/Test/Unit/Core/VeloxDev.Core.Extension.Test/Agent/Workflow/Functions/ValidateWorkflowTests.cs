using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>A selector-driven node that never had its selector configured.</summary>
public enum UnsetRouteKind { Alpha, Beta }

/// <summary>Mirrors the demo's EnumSelector node, minus the constructor call that sets the selector.</summary>
[WorkflowBuilder.Node<NodeHelper<UnsetSelectorNode>>(workSemaphore: 1)]
public partial class UnsetSelectorNode
{
    public UnsetSelectorNode() => InitializeWorkflow();

    [VeloxProperty]
    [SlotSelectors(typeof(UnsetRouteKind))]
    public partial SlotEnumerator<SlotDefaultViewModel> OutputSlots { get; set; }
}

/// <summary>
/// The structural half of validation. A cycle cannot compile, and before this check the lint said nothing
/// about it — a model could build one and only find out when the compiler refused.
/// </summary>
[TestClass]
public class ValidateWorkflowTests
{
    private static (TreeDefaultViewModel Tree, WorkflowAgentScope Scope) TwoNodes()
    {
        var tree = new TreeDefaultViewModel();
        var first = new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(0, 0, 0),
            Size = new Size(100, 40),
        };
        var second = new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(300, 0, 0),
            Size = new Size(100, 40),
        };
        tree.Nodes.Add(first);
        tree.Nodes.Add(second);
        return (tree, new WorkflowAgentScope(tree));
    }

    [TestMethod]
    public void ValidateWorkflow_ReportsACycleAsAnError()
    {
        var (tree, scope) = TwoNodes();
        var first = tree.Nodes[0];
        var second = tree.Nodes[1];

        // IsVisible defaults to false; the lint reasons about the links a user can see.
        tree.Links.Add(new LinkDefaultViewModel
        {
            IsVisible = true,
            Sender = new SlotDefaultViewModel { Parent = first },
            Receiver = new SlotDefaultViewModel { Parent = second },
        });
        tree.Links.Add(new LinkDefaultViewModel
        {
            IsVisible = true,
            Sender = new SlotDefaultViewModel { Parent = second },
            Receiver = new SlotDefaultViewModel { Parent = first },
        });

        var result = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ValidateWorkflow"));
        var issues = (JArray)result["warnings"]!;

        Assert.IsTrue(
            issues.Any(i => i["level"]?.Value<string>() == "error"
                         && (i["msg"]?.Value<string>() ?? string.Empty).Contains("Cycle", StringComparison.Ordinal)),
            "a back edge must be reported as an error, not as one more warning: " + result);
    }

    [TestMethod]
    public void ValidateWorkflow_AnAcyclicGraph_ReportsNoCycle()
    {
        var (tree, scope) = TwoNodes();

        tree.Links.Add(new LinkDefaultViewModel
        {
            IsVisible = true,
            Sender = new SlotDefaultViewModel { Parent = tree.Nodes[0] },
            Receiver = new SlotDefaultViewModel { Parent = tree.Nodes[1] },
        });

        var result = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ValidateWorkflow"));
        var issues = (JArray)result["warnings"]!;

        Assert.IsFalse(
            issues.Any(i => (i["msg"]?.Value<string>() ?? string.Empty).Contains("Cycle", StringComparison.Ordinal)),
            "one forward edge is not a cycle: " + result);
    }

    /// <summary>
    /// A selector-driven node without a selector has no ports yet. The old lint said nothing, so a model could
    /// connect to a node that visually had no ports and only learn the truth from the connection tool.
    /// </summary>
    [TestMethod]
    public void ValidateWorkflow_ReportsAnUnconfiguredSlotEnumerator()
    {
        var tree = new TreeDefaultViewModel();
        var node = new UnsetSelectorNode { Parent = tree };
        tree.Nodes.Add(node);

        var result = JObject.Parse(WorkflowToolInvoker.Invoke(new WorkflowAgentScope(tree), "ValidateWorkflow"));
        var issues = (JArray)result["warnings"]!;

        Assert.IsTrue(
            issues.Any(i => i["level"]?.Value<string>() == "warn"
                         && (i["msg"]?.Value<string>() ?? string.Empty).Contains("no selector", StringComparison.Ordinal)
                         && (i["msg"]?.Value<string>() ?? string.Empty).Contains("SetEnumSlotCollection", StringComparison.Ordinal)),
            "an unconfigured SlotEnumerator must be a warning that names the configuring tool: " + result);
    }
}
