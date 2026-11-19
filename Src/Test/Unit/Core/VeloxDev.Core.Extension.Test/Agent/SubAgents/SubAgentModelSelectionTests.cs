using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VeloxDev.AI.SubAgents;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.SubAgents;

/// <summary>
/// The per-spawn model hint. A host that registers several clients through
/// <see cref="SubAgentScope.ForClientFactory"/> must see the name the spawn asked for, and a name it did not
/// register must fail the child rather than silently falling back.
/// </summary>
[TestClass]
public class SubAgentModelSelectionTests
{
    private static WorkflowAgentScope ScopeWith(SubAgentScope subAgents)
    {
        var tree = new TreeDefaultViewModel();
        tree.GetHelper().CreateNode(new NodeDefaultViewModel());
        return new WorkflowAgentScope(tree).WithSubAgents(subAgents);
    }

    private static string Spawn(WorkflowAgentScope scope, string? model)
    {
        var tool = SubAgentFixture.SubAgentToolOf(scope, "SpawnSubAgent");
        var arguments = new AIFunctionArguments { ["task"] = "count the nodes" };
        if (model is not null) arguments["model"] = model;

        return tool.InvokeAsync(arguments, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult()?.ToString() ?? string.Empty;
    }

    [TestMethod]
    public void ASpawnedChild_RunsOnTheModelItAskedFor()
    {
        var requested = new List<string?>();
        var scope = ScopeWith(SubAgentScope.ForClientFactory(model =>
        {
            lock (requested) requested.Add(model);
            return new InstantChatClient();
        }));
        var subAgents = scope.SubAgents!;

        var reply = Spawn(scope, "cheap");
        Assert.Contains("\"status\":\"ok\"", reply, reply);
        SubAgentFixture.WaitFor(() => subAgents.Snapshot.All(r => r.IsFinished));

        Assert.AreEqual(1, requested.Count, "the factory runs once per spawned child");
        Assert.AreEqual("cheap", requested[0], "the spawn's model reaches the host's factory unchanged");
    }

    [TestMethod]
    public void AModelTheHostDidNotRegister_FailsTheChild_WithoutFallingBack()
    {
        var scope = ScopeWith(SubAgentScope.ForClientFactory(model => model is null
            ? new InstantChatClient()
            : throw new ArgumentException($"unknown model '{model}'")));
        var subAgents = scope.SubAgents!;

        Spawn(scope, "expensive");
        SubAgentFixture.WaitFor(() => subAgents.Snapshot.All(r => r.IsFinished));

        var row = subAgents.Snapshot.Single();
        Assert.AreEqual(SubAgentState.Failed, row.State);
        StringAssert.Contains(row.Error!, "expensive");
    }
}
