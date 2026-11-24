using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Safety;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Safety;

/// <summary>
/// The permission matrix, the rules beneath it, and the one property that makes both worth having: a refusal
/// that says which mode refused and what to switch to.
/// </summary>
/// <remarks>
/// The matrix is asserted cell by cell rather than through a few representative calls. It is the whole
/// contract of a mode — a hole in one cell is a category of action that silently runs unasked — and it is five
/// values by five, so there is no reason to sample it.
/// </remarks>
[TestClass]
public class AgentPermissionPolicyTests
{
    // ── The matrix ──────────────────────────────────────────────────────────

    /// <summary>Every mode's answer for every category, written out.</summary>
    [TestMethod]
    public void TheMatrix_SaysWhatEachModeDoesWithEachKindOfAction()
    {
        var allow = PermissionDecision.Allow;
        var ask = PermissionDecision.Ask;
        var deny = PermissionDecision.Deny;

        var expected = new Dictionary<AgentPermissionMode, PermissionDecision[]>
        {
            //                             Read  Edit  Execute  Curate  Interact
            [AgentPermissionMode.Plan] = [allow, deny, deny, deny, allow],
            [AgentPermissionMode.Manual] = [allow, ask, ask, ask, allow],
            [AgentPermissionMode.AutoEdit] = [allow, allow, ask, ask, allow],
            [AgentPermissionMode.Auto] = [allow, allow, allow, ask, allow],
            [AgentPermissionMode.Bypass] = [allow, allow, allow, allow, allow],
        };

        foreach (var (mode, row) in expected)
        {
            var categories = (AgentActionCategory[])Enum.GetValues(typeof(AgentActionCategory));
            Assert.HasCount(row.Length, categories, "the table above has to cover every category");

            for (var i = 0; i < categories.Length; i++)
            {
                Assert.AreEqual(row[i], AgentPermissionPolicy.ModeDefault(mode, categories[i]),
                    $"{mode} × {categories[i]}");
            }
        }

        // Self-guard: the table is only a contract if every mode and every category is in it.
        Assert.HasCount(5, expected, "a mode with no row would silently fall to the default");
    }

    /// <summary>Reading is never put to the user, in any mode. Asking about one trains the user to click through.</summary>
    [TestMethod]
    public void ReadingIsNeverAskedAbout()
    {
        foreach (var mode in (AgentPermissionMode[])Enum.GetValues(typeof(AgentPermissionMode)))
        {
            Assert.AreEqual(PermissionDecision.Allow, AgentPermissionPolicy.ModeDefault(mode, AgentActionCategory.Read), mode.ToString());
            Assert.AreEqual(PermissionDecision.Allow, AgentPermissionPolicy.ModeDefault(mode, AgentActionCategory.Interact), mode.ToString());
        }
    }

    // ── The rules beneath the matrix ────────────────────────────────────────

    /// <summary>A deny rule holds in Bypass — that is the difference between "stop asking" and "stop caring".</summary>
    [TestMethod]
    public void ADenyRule_HoldsInBypass()
    {
        var policy = AgentPermissionPolicy.For(AgentPermissionMode.Bypass)
            .WithRule(new AgentPermissionRule(PermissionDecision.Deny, "DeleteNode"));

        Assert.AreEqual(PermissionDecision.Deny, policy.Evaluate(Call("DeleteNode", AgentActionCategory.Edit)));
        Assert.AreEqual(PermissionDecision.Allow, policy.Evaluate(Call("MoveNode", AgentActionCategory.Edit)),
            "and it is the pattern that is denied, not the category");
    }

    /// <summary>A deny beats an allow for the same call, whatever order they were added in.</summary>
    [TestMethod]
    public void ADenyRule_BeatsAnAllowRule()
    {
        var policy = AgentPermissionPolicy.For(AgentPermissionMode.Auto)
            .WithRule(new AgentPermissionRule(PermissionDecision.Allow, "DeleteNode"))
            .WithRule(new AgentPermissionRule(PermissionDecision.Deny, "DeleteNode"));

        Assert.AreEqual(PermissionDecision.Deny, policy.Evaluate(Call("DeleteNode", AgentActionCategory.Edit)));
    }

    /// <summary>Plan's refusals are as unconditional as a deny rule: an allow cannot open a graph edit.</summary>
    [TestMethod]
    public void PlansRefusal_IsNotOpenableByAnAllowRule()
    {
        var policy = AgentPermissionPolicy.For(AgentPermissionMode.Plan)
            .WithRule(new AgentPermissionRule(PermissionDecision.Allow, "category:Edit"));

        Assert.AreEqual(PermissionDecision.Deny, policy.Evaluate(Call("MoveNode", AgentActionCategory.Edit)),
            "'nothing changes while I plan' is the promise the mode makes, and a rule may not quietly break it");

        // But an allow rule still wins where the mode was only asking: that is what the rule layer is for.
        var manual = AgentPermissionPolicy.For(AgentPermissionMode.Manual)
            .WithRule(new AgentPermissionRule(PermissionDecision.Allow, "CompileWorkflow"));
        Assert.AreEqual(PermissionDecision.Allow, manual.Evaluate(Call("CompileWorkflow", AgentActionCategory.Execute)));
    }

    /// <summary>Patterns select on the axis their prefix names.</summary>
    [TestMethod]
    public void Patterns_SelectByNameCategoryAndOrigin()
    {
        var policy = AgentPermissionPolicy.For(AgentPermissionMode.Auto)
            .WithRule(new AgentPermissionRule(PermissionDecision.Ask, "Get*"))
            .WithRule(new AgentPermissionRule(PermissionDecision.Deny, "category:Curate"))
            .WithRule(new AgentPermissionRule(PermissionDecision.Ask, "source:mcp:filesystem"));

        Assert.AreEqual(PermissionDecision.Ask, policy.Evaluate(Call("GetNodeDetail", AgentActionCategory.Read)),
            "a bare pattern is the tool's name, with * as a wildcard");
        Assert.AreEqual(PermissionDecision.Deny, policy.Evaluate(Call("AddMcpServer", AgentActionCategory.Curate)));
        Assert.AreEqual(PermissionDecision.Ask, policy.Evaluate(Call("read_file", AgentActionCategory.Execute, "mcp:filesystem")),
            "a name two servers can share needs the origin to be aimed at");
        Assert.AreEqual(PermissionDecision.Allow, policy.Evaluate(Call("read_file", AgentActionCategory.Execute, "mcp:other")),
            "and a rule about one server says nothing about another");
    }

    /// <summary>The judge decides what the mode would otherwise settle, and a judge that throws denies.</summary>
    [TestMethod]
    public void TheAutoJudge_IsConsulted_AndAFailureDenies()
    {
        var asked = 0;
        var policy = AgentPermissionPolicy.For(AgentPermissionMode.Auto)
            .WithJudge(call =>
            {
                asked++;
                return call.Name == "RunCompiledWorkflow" ? PermissionDecision.Deny : PermissionDecision.Allow;
            });

        Assert.AreEqual(PermissionDecision.Deny, policy.Evaluate(Call("RunCompiledWorkflow", AgentActionCategory.Execute)));
        Assert.AreEqual(PermissionDecision.Allow, policy.Evaluate(Call("MoveNode", AgentActionCategory.Edit)));
        Assert.AreEqual(2, asked);

        var broken = AgentPermissionPolicy.For(AgentPermissionMode.Auto)
            .WithJudge(_ => throw new InvalidOperationException("no idea"));
        Assert.AreEqual(PermissionDecision.Deny, broken.Evaluate(Call("MoveNode", AgentActionCategory.Edit)),
            "a gate that cannot answer must not answer yes");

        // And with no judge at all the mode's own answer stands, rather than falling back to asking.
        Assert.AreEqual(PermissionDecision.Allow,
            AgentPermissionPolicy.For(AgentPermissionMode.Auto).Evaluate(Call("MoveNode", AgentActionCategory.Edit)));
    }

    // ── End to end: the gate is in the call, not in the prompt ──────────────

    /// <summary>Plan refuses a write <i>at the gate</i> — the tool is still there, and the message says why.</summary>
    /// <remarks>
    /// The tool being present is the point. The version this replaces kept names off the surface, so the model
    /// could only report that it had no such tool — a dead end for the user, who had nothing to switch.
    /// </remarks>
    [TestMethod]
    public void Plan_RefusesAWriteAtTheGate_AndNamesTheWayOut()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithPermissionMode(AgentPermissionMode.Plan);

        Assert.IsTrue(scope.ProvideTools().Any(t => t.Name == "CreateNode"),
            "the tool is on the surface; the mode refuses the call, it does not hide the capability");

        var refused = JObject.Parse(Invoke(scope, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("x", 0d), ("y", 0d)));

        Assert.AreEqual("error", refused["status"]?.Value<string>(), refused.ToString());
        var message = refused["message"]!.Value<string>()!;
        StringAssert.Contains(message, "Plan", "the refusal has to name the mode that refused it");
        StringAssert.Contains(message, "AutoEdit", "and what to switch to, or the user has nothing to press");
        Assert.IsEmpty(scope.Tree.Nodes, "and nothing ran");
    }

    /// <summary>A read runs in Plan; an edit runs in AutoEdit; execution still asks there.</summary>
    [TestMethod]
    public void EachMode_StopsWhereTheMatrixSaysItStops()
    {
        var planned = new WorkflowAgentScope(new TreeDefaultViewModel()).WithPermissionMode(AgentPermissionMode.Plan);
        Assert.IsEmpty(JArray.Parse(Invoke(planned, "ListNodes")),
            "reading is how a plan gets made — ListNodes answers with the (empty) node list, not a refusal");

        var editing = new WorkflowAgentScope(new TreeDefaultViewModel()).WithPermissionMode(AgentPermissionMode.AutoEdit);
        Assert.AreEqual("ok", JObject.Parse(Invoke(editing, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("x", 0d), ("y", 0d)))["status"]?.Value<string>(),
            "AutoEdit is the mode that trusts an edit and not an execution");

        Assert.AreEqual("error", JObject.Parse(Invoke(editing, "ExecuteNode", ("nodeIndex", 0)))["status"]?.Value<string>(),
            "execution is still put to a user this session does not have, and an unanswerable prompt denies");
    }

    /// <summary>A deny rule stops a write in AutoEdit, where the mode would otherwise run it.</summary>
    [TestMethod]
    public void ARule_StopsWhatTheModeWouldRun()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithPermissionMode(AgentPermissionMode.AutoEdit)
            .WithPermissionRule(PermissionDecision.Deny, "CreateNode");

        var refused = JObject.Parse(Invoke(scope, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("x", 0d), ("y", 0d)));

        var message = refused["message"]!.Value<string>()!;
        StringAssert.Contains(message, "deny rule",
            "a rule's refusal names the rule, because that is what the host removes");
        StringAssert.Contains(message, "CreateNode");
        Assert.IsEmpty(scope.Tree.Nodes);
    }

    /// <summary>
    /// The framework's behavioural mode is not the permission mode, and switching it changes nothing.
    /// </summary>
    /// <remarks>
    /// The two are easy to confuse and they answer different questions: the framework's mode is instruction
    /// text — build or plan — while this one is what may run. Nothing in the gate reads the former, so a model
    /// that sets itself to "build" has not widened anything.
    /// </remarks>
    [TestMethod]
    public void TheBehaviouralMode_DoesNotChangeWhatIsPermitted()
    {
        var options = new Microsoft.Agents.AI.AgentModeProviderOptions
        {
            Modes =
            [
                new Microsoft.Agents.AI.AgentModeProviderOptions.AgentMode("build", "You edit the graph."),
                new Microsoft.Agents.AI.AgentModeProviderOptions.AgentMode("plan", "You only describe."),
            ],
            DefaultMode = "build",
        };

        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithAgentModes(options)
            .WithPermissionMode(AgentPermissionMode.Plan);

        Assert.AreEqual("error", JObject.Parse(Invoke(scope, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("x", 0d), ("y", 0d)))["status"]?.Value<string>(),
            "the behavioural mode cannot widen the permission mode");
    }

    // ── Switching, at any time ──────────────────────────────────────────────

    /// <summary>
    /// A mode switched mid-session is in force for the very next call — no rebuild, no new agent.
    /// </summary>
    /// <remarks>
    /// This is the property a mode has that a construction-time setting does not, and the reason the gate reads
    /// the policy per call rather than capturing it. The prompt catches up on the next turn; the enforcement
    /// does not wait even that long.
    /// </remarks>
    [TestMethod]
    public void AModeSwitchedMidSession_GovernsTheVeryNextCall()
    {
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree);

        var create = new (string, object?)[]
        {
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("x", 0d), ("y", 0d),
        };

        Assert.AreEqual("ok", JObject.Parse(Invoke(scope, "CreateNode", create))["status"]?.Value<string>(),
            "the default mode runs graph edits");

        scope.WithPermissionMode(AgentPermissionMode.Plan);

        Assert.AreEqual("error", JObject.Parse(Invoke(scope, "CreateNode", create))["status"]?.Value<string>(),
            "and the same call is refused the moment the mode moves, with no re-render in between");

        scope.WithPermissionMode(AgentPermissionMode.AutoEdit);
        Assert.AreEqual("ok", JObject.Parse(Invoke(scope, "CreateNode", create))["status"]?.Value<string>(),
            "switching back takes effect just as immediately");
    }

    /// <summary>And the setter a host binds to reports whether it moved.</summary>
    [TestMethod]
    public void SetPermissionMode_ReportsWhetherItMoved()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel());

        Assert.IsTrue(scope.SetPermissionMode(AgentPermissionMode.Manual));
        Assert.IsFalse(scope.SetPermissionMode(AgentPermissionMode.Manual), "the second call moved nothing");
        Assert.AreEqual(AgentPermissionMode.Manual, scope.PermissionMode);
    }

    // ── The Agent's own two transitions ─────────────────────────────────────

    /// <summary>The Agent may narrow itself into Plan, and the narrowing is real once the user agrees.</summary>
    [TestMethod]
    public async Task TheAgent_CanEnterPlan_AndOutAgain()
    {
        var asked = new List<string>();
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree)
            .WithPermissionMode(AgentPermissionMode.AutoEdit)
            .WithConfirmationHandler(args => { asked.Add(args.OperationKey); args.Result = AgentConfirmationResult.AllowOnce; return Task.CompletedTask; });

        Assert.IsTrue(scope.ProvideTools().Any(t => t.Name == "EnterPlanMode"), "the tool is on the surface");

        var entered = JObject.Parse(Invoke(scope, "EnterPlanMode"));
        Assert.AreEqual("ok", entered["status"]?.Value<string>(), entered.ToString());
        Assert.AreEqual(AgentPermissionMode.Plan, scope.PermissionMode);
        CollectionAssert.Contains(asked, "mode:plan", "entering is a change the user is shown");

        Assert.AreEqual("error", JObject.Parse(Invoke(scope, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("x", 0d), ("y", 0d)))["status"]?.Value<string>(),
            "and it binds the Agent itself, not only the host's calls");
        Assert.IsEmpty(tree.Nodes);

        var exited = JObject.Parse(Invoke(scope, "ExitPlanMode"));
        Assert.AreEqual("ok", exited["status"]?.Value<string>(), exited.ToString());
        Assert.AreEqual(AgentPermissionMode.AutoEdit, scope.PermissionMode,
            "leaving restores where it was, rather than choosing a mode for the session");
        Assert.AreEqual("ok", JObject.Parse(Invoke(scope, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("x", 0d), ("y", 0d)))["status"]?.Value<string>());
    }

    /// <summary>A declined switch changes nothing — in either direction.</summary>
    [TestMethod]
    public void ADeclinedSwitch_ChangesNothing()
    {
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree)
            .WithPermissionMode(AgentPermissionMode.AutoEdit)
            .WithConfirmationHandler(args => { args.Result = AgentConfirmationResult.Deny; return Task.CompletedTask; });

        Assert.AreEqual("denied", JObject.Parse(Invoke(scope, "EnterPlanMode"))["status"]?.Value<string>());
        Assert.AreEqual(AgentPermissionMode.AutoEdit, scope.PermissionMode);
    }

    /// <summary>There is no tool that moves the Agent to a wider mode, in any session.</summary>
    [TestMethod]
    public void NoTool_MovesTheAgentToAWiderMode()
    {
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithPermissionMode(AgentPermissionMode.Plan)
            .WithConfirmationHandler(args => { args.Result = AgentConfirmationResult.AllowAlways; return Task.CompletedTask; });

        var names = scope.ProvideTools().Select(t => t.Name).ToArray();

        CollectionAssert.Contains(names, "ExitPlanMode", "leaving the restriction it put itself under is the one way out");
        foreach (var forbidden in new[] { "SetPermissionMode", "WithPermissionMode", "EnterAutoEditMode", "EnterBypassMode" })
            CollectionAssert.DoesNotContain(names, forbidden, "an Agent that can widen its own reach is the hole this design avoids");

        // And the one way out does not widen anything: it restores, and the rules beneath still hold.
        scope.WithPermissionRule(PermissionDecision.Deny, "CreateNode");
        Invoke(scope, "ExitPlanMode");

        Assert.AreEqual("error", JObject.Parse(Invoke(scope, "CreateNode",
            ("fullTypeName", typeof(NodeDefaultViewModel).FullName!), ("x", 0d), ("y", 0d)))["status"]?.Value<string>(),
            "a deny rule is not something a mode change can shake off");
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    private static ToolInvocation Call(string name, AgentActionCategory category, string? source = null)
        => new(name, category, source, new Dictionary<string, object?>());

    private static string Invoke(WorkflowAgentScope scope, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = scope.ProvideTools().OfType<AIFunction>()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Tool '{toolName}' was not registered.");

        var aiArgs = new AIFunctionArguments();
        foreach (var (name, value) in args) aiArgs[name] = value;

        var result = tool.InvokeAsync(aiArgs, System.Threading.CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }
}
