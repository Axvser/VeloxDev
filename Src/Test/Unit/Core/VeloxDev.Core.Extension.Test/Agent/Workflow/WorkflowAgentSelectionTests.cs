using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// Coverage for the node selection that feeds the Agent's dynamic instructions: it is set-like and per tree,
/// changes no tools, re-renders the envelope when it moves, and releases nodes whose parent changes or that
/// leave the tree.
/// </summary>
[TestClass]
public class WorkflowAgentSelectionTests
{
    private static (TreeDefaultViewModel Tree, NodeDefaultViewModel First, NodeDefaultViewModel Second) TreeWithTwoNodes()
    {
        var tree = new TreeDefaultViewModel();
        var first = new NodeDefaultViewModel();
        var second = new NodeDefaultViewModel();
        var helper = tree.GetHelper();
        helper.CreateNode(first);
        helper.CreateNode(second);
        return (tree, first, second);
    }

    [TestMethod]
    public void SelectingOneNode_PutsItInTheDynamicInstructions()
    {
        var (tree, first, _) = TreeWithTwoNodes();

        Assert.IsTrue(first.SelectForAgent(), "a first registration must report success");

        var text = new WorkflowAgentScope(tree).BuildDynamicInstructions()!;
        StringAssert.Contains(text, "Current Node Selection");
        StringAssert.Contains(text, first.RuntimeId);
        StringAssert.Contains(text, nameof(NodeDefaultViewModel));
        StringAssert.Contains(text, "**1** node");
    }

    [TestMethod]
    public void SelectingSeveralNodes_KeepsSelectionOrder()
    {
        var (tree, first, second) = TreeWithTwoNodes();

        Assert.AreEqual(2, new List<IWorkflowNodeViewModel> { first, second }.SelectForAgent());

        var text = new WorkflowAgentScope(tree).BuildDynamicInstructions()!;
        var firstAt = text.IndexOf(first.RuntimeId, StringComparison.Ordinal);
        var secondAt = text.IndexOf(second.RuntimeId, StringComparison.Ordinal);

        Assert.IsTrue(firstAt >= 0 && secondAt >= 0, "both selected nodes must be listed");
        Assert.IsTrue(firstAt < secondAt, "the prompt must preserve the selection order");
        StringAssert.Contains(text, "**2** nodes");
    }

    [TestMethod]
    public void SelectingAgain_AddsToTheSelection()
    {
        var (tree, first, second) = TreeWithTwoNodes();
        var scope = new WorkflowAgentScope(tree);

        Assert.IsTrue(first.SelectForAgent());
        Assert.AreEqual(1, new List<IWorkflowNodeViewModel> { second }.SelectForAgent());

        var text = scope.BuildDynamicInstructions()!;
        StringAssert.Contains(text, first.RuntimeId);
        StringAssert.Contains(text, second.RuntimeId);
        StringAssert.Contains(text, "**2** nodes");
    }

    [TestMethod]
    public void SelectingTheSameNodeAgain_ReportsNoChange()
    {
        var (_, first, _) = TreeWithTwoNodes();

        Assert.IsTrue(first.SelectForAgent());
        Assert.IsFalse(first.SelectForAgent(), "an already-selected node is a HashSet-style no-op");
        Assert.AreEqual(0, new List<IWorkflowNodeViewModel> { first }.SelectForAgent());
    }

    [TestMethod]
    public void DuplicateNodes_AreCollapsed()
    {
        var (tree, first, _) = TreeWithTwoNodes();

        Assert.AreEqual(1, new List<IWorkflowNodeViewModel> { first, first }.SelectForAgent());

        Assert.AreEqual(1, tree.GetAgentSelection().Count);
    }

    [TestMethod]
    public void ClearingTheSelection_RemovesTheSection()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        var scope = new WorkflowAgentScope(tree);

        first.SelectForAgent();
        Assert.AreEqual(1, tree.ClearAgentSelection());
        Assert.AreEqual(0, tree.ClearAgentSelection());

        var text = scope.BuildDynamicInstructions()!;
        Assert.IsFalse(text.Contains("Current Node Selection", StringComparison.Ordinal));
        Assert.AreEqual(0, tree.GetAgentSelection().Count);
    }

    [TestMethod]
    public void TheSelectionIsPerTree()
    {
        var (firstTree, first, _) = TreeWithTwoNodes();
        var (secondTree, _, _) = TreeWithTwoNodes();

        first.SelectForAgent();

        var otherText = new WorkflowAgentScope(secondTree).BuildDynamicInstructions()!;
        Assert.IsFalse(otherText.Contains("Current Node Selection", StringComparison.Ordinal),
            "a selection on one tree must not leak into another");
    }

    [TestMethod]
    public void MixingTrees_AddsEachToItsOwnTree()
    {
        var (firstTree, first, _) = TreeWithTwoNodes();
        var (secondTree, second, _) = TreeWithTwoNodes();

        Assert.AreEqual(2, new List<IWorkflowNodeViewModel> { first, second }.SelectForAgent());

        Assert.AreEqual(1, firstTree.GetAgentSelection().Count);
        Assert.AreEqual(1, secondTree.GetAgentSelection().Count);
    }

    [TestMethod]
    public void AnUnattachedNode_CannotBeAdded()
    {
        var orphan = new NodeDefaultViewModel();

        Assert.IsFalse(orphan.SelectForAgent());
        Assert.AreEqual(0, new List<IWorkflowNodeViewModel> { orphan }.SelectForAgent());
    }

    [TestMethod]
    public void ANodeNotMountedInTheTree_CannotBeAdded()
    {
        var (tree, _, _) = TreeWithTwoNodes();
        var detached = new NodeDefaultViewModel { Parent = tree };

        Assert.IsFalse(detached.SelectForAgent());
    }

    [TestMethod]
    public void AnEmptySelection_ReturnsZero()
    {
        Assert.AreEqual(0, new List<IWorkflowNodeViewModel>().SelectForAgent());
    }

    [TestMethod]
    public void ANullCollection_ReturnsZero()
    {
        IReadOnlyCollection<IWorkflowNodeViewModel>? none = null;

        Assert.AreEqual(0, none!.SelectForAgent());
        Assert.AreEqual(0, none!.DeselectForAgent());
    }

    [TestMethod]
    public void AClearWithNoTree_ReturnsZero()
    {
        IWorkflowTreeViewModel? none = null;

        Assert.AreEqual(0, none!.ClearAgentSelection());
    }

    [TestMethod]
    public void AThrowingCollection_ReturnsMinusOne()
    {
        Assert.AreEqual(-1, new ThrowingCollection().SelectForAgent());
        Assert.AreEqual(-1, new ThrowingCollection().DeselectForAgent());
    }

    [TestMethod]
    public void Selecting_ChangesTheContextWithoutChangingTheToolSet()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        var scope = new WorkflowAgentScope(tree);

        var version = scope.Version;
        var tools = scope.BuildDynamicTools().Select(t => t.Name).ToArray();

        first.SelectForAgent();

        Assert.AreEqual(version, scope.Version,
            "a selection is context, not configuration; it must not invalidate the tool render");
        CollectionAssert.AreEqual(tools, scope.BuildDynamicTools().Select(t => t.Name).ToArray(),
            "a selection must not add or remove tools");
    }

    [TestMethod]
    public void ASelectionMove_ReRendersTheContextProvider()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        var scope = new WorkflowAgentScope(tree);
        var provider = new WorkflowAgentContextProvider(scope);

        var before = provider.BuildContext();
        first.SelectForAgent();
        var after = provider.BuildContext();

        Assert.AreNotSame(before, after, "the selection revision must invalidate the cached context");
        StringAssert.Contains(after.Instructions!, first.RuntimeId);
    }

    [TestMethod]
    public void ReApplyingTheSameSelection_ReusesTheRender()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        var scope = new WorkflowAgentScope(tree);
        var provider = new WorkflowAgentContextProvider(scope);

        first.SelectForAgent();
        var firstContext = provider.BuildContext();

        first.SelectForAgent();
        var secondContext = provider.BuildContext();

        Assert.AreSame(firstContext, secondContext,
            "re-applying the same set must not advance the selection revision");
    }

    [TestMethod]
    public void ThePromptFollowsThePromptLanguage()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        var scope = new WorkflowAgentScope(tree).WithPromptLanguage(AgentLanguages.Chinese);

        first.SelectForAgent();

        var text = scope.BuildDynamicInstructions()!;
        StringAssert.Contains(text, "当前选中的节点");
        StringAssert.Contains(text, first.RuntimeId);
    }

    [TestMethod]
    public void ALargeSelection_ReportsTheFullCountAndCapsTheList()
    {
        var tree = new TreeDefaultViewModel();
        var helper = tree.GetHelper();
        var nodes = new List<IWorkflowNodeViewModel>();

        for (var i = 0; i <= WorkflowAgentSelection.MaxRenderedNodes; i++)
        {
            var node = new NodeDefaultViewModel();
            helper.CreateNode(node);
            nodes.Add(node);
        }

        Assert.AreEqual(nodes.Count, nodes.SelectForAgent());

        var text = new WorkflowAgentScope(tree).BuildDynamicInstructions()!;
        StringAssert.Contains(text, $"**{nodes.Count}** nodes");
        StringAssert.Contains(text, "1 more are in the selection");
        var lastId = ((IWorkflowIdentifiable)nodes[^1]).RuntimeId;
        Assert.IsFalse(text.Contains(lastId, StringComparison.Ordinal),
            "an identity beyond the cap must not be listed");
    }

    [TestMethod]
    public void DeselectingOneNode_RemovesOnlyThatNode()
    {
        var (tree, first, second) = TreeWithTwoNodes();
        new List<IWorkflowNodeViewModel> { first, second }.SelectForAgent();
        var scope = new WorkflowAgentScope(tree);

        Assert.IsTrue(first.DeselectForAgent());
        Assert.IsFalse(first.DeselectForAgent(), "a second release reports that nothing was present");

        var text = scope.BuildDynamicInstructions()!;
        Assert.IsFalse(text.Contains(first.RuntimeId, StringComparison.Ordinal),
            "deselecting one node must not clear the rest");
        StringAssert.Contains(text, second.RuntimeId);
        StringAssert.Contains(text, "**1** node");
        Assert.AreEqual(1, tree.GetAgentSelection().Count);
    }

    [TestMethod]
    public void DeselectingSeveralNodes_PreservesTheRemainingOrder()
    {
        var (tree, first, second) = TreeWithTwoNodes();
        var third = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(third);
        new List<IWorkflowNodeViewModel> { first, second, third }.SelectForAgent();

        Assert.IsTrue(second.DeselectForAgent());

        var text = new WorkflowAgentScope(tree).BuildDynamicInstructions()!;
        var firstAt = text.IndexOf(first.RuntimeId, StringComparison.Ordinal);
        var thirdAt = text.IndexOf(third.RuntimeId, StringComparison.Ordinal);

        Assert.IsFalse(text.Contains(second.RuntimeId, StringComparison.Ordinal));
        Assert.IsTrue(firstAt >= 0 && thirdAt >= 0 && firstAt < thirdAt,
            "the nodes that remain must keep their relative order");
    }

    [TestMethod]
    public void DeselectingANodeNotInTheSelection_DoesNotReRender()
    {
        var (tree, first, second) = TreeWithTwoNodes();
        first.SelectForAgent();

        var provider = new WorkflowAgentContextProvider(new WorkflowAgentScope(tree));
        var before = provider.BuildContext();

        Assert.IsFalse(second.DeselectForAgent());

        var after = provider.BuildContext();
        Assert.AreSame(before, after,
            "removing a node that was never selected must not advance the revision");
    }

    [TestMethod]
    public void DetachingANode_RemovesItFromTheSelection()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        first.SelectForAgent();

        first.Parent = null;

        Assert.AreEqual(0, tree.GetAgentSelection().Count,
            "the Parent change must release the node without an explicit deselect");
        Assert.IsFalse(new WorkflowAgentScope(tree).BuildDynamicInstructions()!
            .Contains("Current Node Selection", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReparentingANode_RemovesItFromTheOldTree()
    {
        var (firstTree, first, _) = TreeWithTwoNodes();
        var (secondTree, _, _) = TreeWithTwoNodes();
        first.SelectForAgent();

        first.Parent = secondTree;

        Assert.AreEqual(0, firstTree.GetAgentSelection().Count,
            "moving the node to another tree must release the old tree's selection");
    }

    [TestMethod]
    public void RemovingANodeFromTheTreeCollection_RemovesItFromTheSelection()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        first.SelectForAgent();

        tree.Nodes.Remove(first);

        Assert.AreEqual(0, tree.GetAgentSelection().Count,
            "removing the node from the parent's Nodes collection must release it");
    }

    [TestMethod]
    public void ClearingTheTreeNodes_ClearsTheSelection()
    {
        var (tree, first, second) = TreeWithTwoNodes();
        first.SelectForAgent();
        new List<IWorkflowNodeViewModel> { first, second }.SelectForAgent();

        tree.Nodes.Clear();

        Assert.AreEqual(0, tree.GetAgentSelection().Count);
    }

    [TestMethod]
    public void DeletingANode_RemovesItFromTheSelection()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        first.SelectForAgent();

        first.DeleteCommand.Execute(null);

        Assert.AreEqual(0, tree.GetAgentSelection().Count,
            "the standard delete path must release the node");
        Assert.IsFalse(new WorkflowAgentScope(tree).BuildDynamicInstructions()!
            .Contains("Current Node Selection", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReplacingTheNodesCollection_ClearsTheSelection()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        first.SelectForAgent();

        tree.Nodes = new ObservableCollection<IWorkflowNodeViewModel>();

        Assert.AreEqual(0, tree.GetAgentSelection().Count,
            "replacing the parent's Nodes collection makes the old identities meaningless");
    }

    [TestMethod]
    public void TheDeletedEvent_RemovesTheSelection()
    {
        var (tree, first, _) = TreeWithTwoNodes();
        first.SelectForAgent();

        ((IWorkflowNodeEvents)first.GetHelper()).RaiseDeleted(new WorkflowEventHandle());

        Assert.AreEqual(0, tree.GetAgentSelection().Count,
            "the semantic delete event must release the node as a last line of defence");
    }

    [TestMethod]
    public void DeselectingAcrossTrees_RemovesEachFromItsOwnTree()
    {
        var (firstTree, first, _) = TreeWithTwoNodes();
        var (secondTree, second, _) = TreeWithTwoNodes();
        first.SelectForAgent();
        second.SelectForAgent();

        Assert.AreEqual(2, new List<IWorkflowNodeViewModel> { first, second }.DeselectForAgent());

        Assert.IsFalse(new WorkflowAgentScope(firstTree).BuildDynamicInstructions()!
            .Contains("Current Node Selection", StringComparison.Ordinal));
        Assert.IsFalse(new WorkflowAgentScope(secondTree).BuildDynamicInstructions()!
            .Contains("Current Node Selection", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DeselectingAnUnattachedNode_IsIgnored()
    {
        var orphan = new NodeDefaultViewModel();

        Assert.IsFalse(orphan.DeselectForAgent());
        Assert.AreEqual(0, new List<IWorkflowNodeViewModel> { orphan }.DeselectForAgent());
        Assert.IsNull(orphan.Parent);
    }

    [TestMethod]
    public void NullEntries_AreIgnored()
    {
        Assert.AreEqual(0, new List<IWorkflowNodeViewModel> { null! }.DeselectForAgent());
        Assert.AreEqual(0, new List<IWorkflowNodeViewModel> { null! }.SelectForAgent());
    }

    [TestMethod]
    public void GetAgentSelection_ReturnsASnapshot()
    {
        var (tree, first, second) = TreeWithTwoNodes();
        var third = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(third);
        new List<IWorkflowNodeViewModel> { first, second }.SelectForAgent();

        var snapshot = tree.GetAgentSelection();
        third.SelectForAgent();

        Assert.AreEqual(2, snapshot.Count, "the returned list must not follow later selections");
        Assert.AreSame(first, snapshot[0]);
        Assert.AreSame(second, snapshot[1]);
    }

    /// <summary>A collection whose own shape throws, used to prove the silent <c>-1</c> path.</summary>
    private sealed class ThrowingCollection : IReadOnlyCollection<IWorkflowNodeViewModel>
    {
        public int Count => throw new InvalidOperationException("boom");

        public IEnumerator<IWorkflowNodeViewModel> GetEnumerator() => throw new InvalidOperationException("boom");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
