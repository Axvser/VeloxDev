using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The layout tools are the Agent's access to the tree's registered solution: they must report that solver,
/// preview it without writing, and apply it through the same <c>Tree.Arrange</c> path the GUI uses.
/// </summary>
[TestClass]
public class LayoutToolTests
{
    private static (TreeDefaultViewModel Tree, NodeDefaultViewModel Parent, NodeDefaultViewModel Child)
        ParentAndChild()
    {
        var tree = new TreeDefaultViewModel();
        var parent = new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(0, 0, 0),
            Size = new Size(100, 40),
        };
        var child = new NodeDefaultViewModel
        {
            Parent = tree,
            Anchor = new Anchor(300, 200, 0),
            Size = new Size(100, 20),
        };
        tree.Nodes.Add(parent);
        tree.Nodes.Add(child);
        tree.Links.Add(new LinkDefaultViewModel
        {
            Sender = new SlotDefaultViewModel { Parent = parent },
            Receiver = new SlotDefaultViewModel { Parent = child },
        });
        return (tree, parent, child);
    }

    [TestMethod]
    public void GetLayoutInfo_ReportsTheRegisteredSolverAndDefaults()
    {
        var (tree, _, _) = ParentAndChild();
        var scope = new WorkflowAgentScope(tree);

        var info = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "GetLayoutInfo"));

        Assert.AreEqual(nameof(DefaultWorkflowLayoutSolver), info["solver"]!.Value<string>());
        Assert.IsTrue(info["host"]!.Value<bool>());
        Assert.IsFalse(info["custom"]!.Value<bool>());
        Assert.AreEqual(2, info["nodes"]!.Value<int>());
        Assert.AreEqual(8d, info["defaults"]!["positionTolerance"]!.Value<double>(), 1e-9);
    }

    [TestMethod]
    public void PreviewLayout_DoesNotWriteAnchors_AndReturnsThePlacements()
    {
        var (tree, parent, child) = ParentAndChild();
        var scope = new WorkflowAgentScope(tree);
        var before = child.Anchor;

        var result = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "PreviewLayout",
            ("nodeIndex", 0), ("traversal", "Downstream"), ("positionTolerance", 0.0)));

        Assert.AreEqual("preview", result["mode"]!.Value<string>());
        Assert.IsFalse(result["applied"]!.Value<bool>());
        Assert.IsTrue(result["dryRun"]!.Value<bool>());
        Assert.AreEqual(2, result["placements"]!.Count());
        Assert.AreEqual(before.Horizontal, child.Anchor.Horizontal, 1e-9);
        Assert.AreEqual(before.Vertical, child.Anchor.Vertical, 1e-9);

        var solvedChild = result["placements"]!.Single(p => p["i"]!.Value<int>() == 1)!;
        Assert.AreEqual(164d, solvedChild["x"]!.Value<double>(), 1e-6,
            "the preview reports the canonical child column");
        Assert.AreEqual(10d, solvedChild["y"]!.Value<double>(), 1e-6);
        Assert.AreEqual(0d, parent.Anchor.Horizontal, 1e-9, "the pinned start node stays put in a preview");
    }

    [TestMethod]
    public void ArrangeLayout_AppliesTheRegisteredSolution()
    {
        var (tree, parent, child) = ParentAndChild();
        var scope = new WorkflowAgentScope(tree);

        var result = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ArrangeLayout",
            ("nodeIndex", 0), ("traversal", "Downstream"), ("positionTolerance", 0.0)));

        Assert.AreEqual("apply", result["mode"]!.Value<string>());
        Assert.IsTrue(result["applied"]!.Value<bool>());
        Assert.AreEqual(1, result["appliedCount"]!.Value<int>());
        Assert.AreEqual(164d, child.Anchor.Horizontal, 1e-6);
        Assert.AreEqual(10d, child.Anchor.Vertical, 1e-6);
        Assert.AreEqual(0d, parent.Anchor.Horizontal, 1e-9, "the pinned start node stays put");
    }

    [TestMethod]
    public void ArrangeLayout_UsesTheSolverRegisteredOnTheTree()
    {
        var (tree, _, _) = ParentAndChild();
        var scope = new WorkflowAgentScope(tree);
        var host = (IWorkflowLayoutHost)tree.GetHelper();
        var solver = new RecordingSolver();
        host.LayoutSolver = solver;

        var info = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "GetLayoutInfo"));
        Assert.AreEqual(nameof(RecordingSolver), info["solver"]!.Value<string>());
        Assert.IsTrue(info["custom"]!.Value<bool>());

        WorkflowToolInvoker.Invoke(scope, "ArrangeLayout", ("positionTolerance", 0.0));

        Assert.IsTrue(solver.Called, "the tool must reach the tree's registered solver");
        Assert.IsNotNull(solver.Options);
    }

    [TestMethod]
    public void LayoutTools_AreRegisteredUnderTheLayoutCategory()
    {
        var (tree, _, _) = ParentAndChild();
        var scope = new WorkflowAgentScope(tree);

        var names = scope.ProvideTools(WorkflowToolCategory.Layout)
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.IsTrue(names.Contains("GetLayoutInfo"));
        Assert.IsTrue(names.Contains("PreviewLayout"));
        Assert.IsTrue(names.Contains("ArrangeLayout"));
        Assert.IsFalse(names.Contains("MoveNode"), "MoveNode belongs to Mutation, not Layout");
    }

    [TestMethod]
    public void ArrangeLayout_CanPinNodesByIndex()
    {
        var (tree, _, child) = ParentAndChild();
        var scope = new WorkflowAgentScope(tree);
        var before = child.Anchor;

        WorkflowToolInvoker.Invoke(scope, "ArrangeLayout",
            ("nodeIndex", 0), ("traversal", "Downstream"), ("positionTolerance", 0.0),
            ("pinnedNodeIndicesJson", "[1]"));

        Assert.AreEqual(before.Horizontal, child.Anchor.Horizontal, 1e-9);
        Assert.AreEqual(before.Vertical, child.Anchor.Vertical, 1e-9);
    }

    private sealed class RecordingSolver : IWorkflowLayoutSolver
    {
        private readonly DefaultWorkflowLayoutSolver _inner = new();

        public bool Called { get; private set; }
        public WorkflowLayoutOptions? Options { get; private set; }

        public WorkflowLayoutResult Arrange(IWorkflowTreeViewModel tree, WorkflowLayoutOptions options)
        {
            Called = true;
            Options = options;
            return _inner.Arrange(tree, options);
        }
    }
}
