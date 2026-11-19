using Microsoft.Extensions.AI;
using System.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// Coverage for the scope's per-tool switches, independent of the dashboard that drives them: what they do
/// to every path that hands tools out, and what <see cref="WorkflowAgentScope.Changed"/> is allowed to mean.
/// </summary>
[TestClass]
public class ToolSwitchTests
{
    [TestMethod]
    public void SwitchedOffTool_LeavesEveryToolPath()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        Assert.IsTrue(scope.SetToolEnabled("ListNodes", false));

        foreach (var name in new[] { "ListNodes" })
        {
            CollectionAssert.DoesNotContain(scope.ProvideTools().Select(t => t.Name).ToArray(), name);
            CollectionAssert.DoesNotContain(
                scope.ProvideTools(WorkflowToolCategory.Query).Select(t => t.Name).ToArray(), name);
            CollectionAssert.DoesNotContain(scope.BuildDynamicTools().Select(t => t.Name).ToArray(), name);
        }

        Assert.IsTrue(scope.SetToolEnabled("ListNodes", true));
        CollectionAssert.Contains(scope.ProvideTools().Select(t => t.Name).ToArray(), "ListNodes");
    }

    [TestMethod]
    public void SameValueSwitch_ReportsNoChange_AndRaisesNoEvent()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithToolEnabled("ListNodes", false);
        var events = 0;
        scope.Changed += (_, _) => events++;

        Assert.IsFalse(scope.SetToolEnabled("ListNodes", false), "the switch was already off");
        Assert.AreSame(scope, scope.WithToolEnabled("ListNodes", false), "the fluent form stays chainable");
        Assert.IsFalse(scope.IsToolEnabled("ListNodes"));

        Assert.AreEqual(0, events, "a no-op switch must not look like a change");
        Assert.HasCount(1, scope.DisabledToolNames);
    }

    [TestMethod]
    public void RealSwitch_RaisesExactlyOneEvent_AndIsIdempotentOnTheWayBack()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var events = 0;
        scope.Changed += (_, _) => events++;

        scope.SetToolEnabled("ListNodes", false);
        Assert.AreEqual(1, events);

        scope.SetToolEnabled("ListNodes", true);
        Assert.AreEqual(2, events);

        Assert.IsEmpty(scope.DisabledToolNames);
    }

    [TestMethod]
    public void FluentForm_ConfiguresBeforeUse()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithToolEnabled("ListNodes", false);

        Assert.IsFalse(scope.IsToolEnabled("ListNodes"));
        Assert.IsTrue(scope.IsToolEnabled("GetWorkflowSummary"), "only the named tool is affected");
    }

    [TestMethod]
    public void SwitchedOffTool_IsRefusedAtCallTime_NotJustHidden()
    {
        // Captured before the switch, so this pins the refusal rather than the omission: a tool handed out
        // earlier must stop working the moment the host switches it off.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var tool = (AIFunction)scope.ProvideTools().Single(t => t.Name == "ListNodes");

        scope.SetToolEnabled("ListNodes", false);

        var result = tool.InvokeAsync(new AIFunctionArguments(), System.Threading.CancellationToken.None)
            .AsTask().GetAwaiter().GetResult()?.ToString() ?? string.Empty;
        StringAssert.Contains(result, "disabled by host policy");
    }

    [TestMethod]
    public void AnUnknownName_IsHarmless()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());
        var before = scope.ProvideTools().Count;

        Assert.IsTrue(scope.SetToolEnabled("no-such-tool", false), "the switch is recorded; nothing owns it yet");
        Assert.AreEqual(before, scope.ProvideTools().Count, "no tool was actually removed");

        Assert.IsFalse(scope.SetToolEnabled("", false));
        Assert.IsFalse(scope.SetToolEnabled(null!, false));
    }
}
