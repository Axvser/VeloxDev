using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies the SlotEnumerator-based EnumSelector's per-credential selection memory and its
/// undo/redo behavior on the demo's bootstrap undo stack (one step at a time). These tests only touch
/// the selector + undo stack — the Compiler mechanism was removed for a from-scratch rewrite, so
/// routing/compile-time behavior is no longer exercised here.
/// </summary>
[TestClass]
public class DemoEnumSelectorSwitchRepro
{
    [TestMethod]
    public void Repro_DemoCredentialValuesPreserved()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");

        // The demo now defaults to NetworkRequestMethod (Get). First switch to VoltageRange to record High,
        // then verify that credentials A/B each remember their selected value independently.
        node.OutputSlots.SetSelector(vr!);
        node.SelectedValue = "High";
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Get", node.SelectedValue, "A remembers Get (from demo setup)");
        node.SelectedValue = "Post";
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "B remembers High");
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Post", node.SelectedValue, "A remembers Post");
    }

    [TestMethod]
    public void Repro_UndoRedo_DictRestoration()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");

        // The demo now defaults to NetworkRequestMethod; first switch to VoltageRange to record High, then verify each credential's dict restore.
        node.OutputSlots.SetSelector(vr!);
        node.SelectedValue = "High";
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Get", node.SelectedValue, "HTTP restores Get from dict");
        node.SelectedValue = "Post";
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "Voltage restores High from dict");

        // Undo → HTTP: must immediately restore Post from the dict.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual("Post", node.SelectedValue, "undo → HTTP immediately restores Post");
        // Undo → Voltage: High.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual("High", node.SelectedValue, "undo → Voltage immediately restores High");
        // Redo → HTTP: Post.
        session.Tree.GetHelper().Redo();
        Assert.AreEqual("Post", node.SelectedValue, "redo → HTTP immediately restores Post");
        // Redo → Voltage: High.
        session.Tree.GetHelper().Redo();
        Assert.AreEqual("High", node.SelectedValue, "redo → Voltage immediately restores High");
    }

    [TestMethod]
    public void Repro_MethodRouter_UndoSelectSwitch()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");
        Assert.IsNotNull(vr, "VoltageRange should exist in Lib");

        // The demo keeps its bootstrap on the undo stack (one step at a time), but the interactive
        // switches we perform below land on top of it — so a single Undo pops exactly our own
        // switch, deterministically. To exercise the undo path, perform switches of our own, then
        // undo one.
        // The demo now defaults to NetworkRequestMethod; first switch to VoltageRange to record High, then switch back to HTTP to verify GET.
        node.OutputSlots.SetSelector(vr!);
        node.SelectedValue = "High";
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));

        // Switch to HTTP → HTTP must show GET (the value the demo's initial setup selected).
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual(typeof(NetworkRequestMethod), node.EnumType);
        Assert.AreEqual("Get", node.SelectedValue, "HTTP shows GET selected");

        // Select an HTTP mode, then switch back to Voltage — Voltage must retain High.
        node.SelectedValue = "Post";
        Assert.AreEqual("Post", node.SelectedValue);
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "Voltage retains its previous selection");

        // Undo the Voltage switch → back to HTTP; HTTP must retain Post.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual(typeof(NetworkRequestMethod), node.EnumType, "undo returns to HTTP");
        Assert.AreEqual("Post", node.SelectedValue, "undo to HTTP shows POST selected");
    }
}
