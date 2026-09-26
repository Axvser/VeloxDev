using Microsoft.Extensions.AI;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using VeloxDev.AI.Workflow;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Invokes a workflow tool the way an AI host does — by name, through the public registration path
/// (<c>scope.ProvideTools()</c> → <c>AIFunction.InvokeAsync</c>) — so a test exercises the wrapper's gates
/// and marshalling rather than a tool body called directly.
/// <para>
/// Shared because a third copy of this helper was about to be written; <c>WorkflowLifecycleFidelityTests</c>
/// and <c>ToolApprovalTests</c> still carry their own and can be folded in whenever either is next touched.
/// </para>
/// </summary>
internal static class WorkflowToolInvoker
{
    /// <summary>Arguments are bound by parameter name; null values are omitted so the method's defaults apply.</summary>
    internal static string Invoke(
        WorkflowAgentScope scope, string toolName, params (string Name, object? Value)[] args)
    {
        var tool = scope.ProvideTools()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase)) as AIFunction
            ?? throw new InvalidOperationException($"Tool '{toolName}' was not registered.");

        var aiArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null)
                aiArgs[name] = value;

        var result = tool.InvokeAsync(aiArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result switch
        {
            string s => s,
            JsonElement je => je.GetString() ?? string.Empty,
            _ => result?.ToString() ?? string.Empty,
        };
    }
}
