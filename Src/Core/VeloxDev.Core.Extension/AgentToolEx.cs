using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public static class AgentToolEx
{
    public static WorkflowAgentScope CreateWorkflowAgentScope(this IWorkflowTreeViewModel tree)
        => new(tree);

    public static IEnumerable<Delegate> ProvideWorkflowAgentTools()
    {
        foreach (var tool in WorkflowAgentToolProvider.ProvideWorkflowAgentTools())
        {
            yield return tool;
        }

        yield return WorkflowProtocolTools.GetWorkflowBootstrap;
        yield return WorkflowProtocolTools.GetWorkflowBootstrapInLanguage;
        yield return WorkflowProtocolTools.GetWorkflowContextSection;
        yield return WorkflowProtocolTools.OpenWorkflowSession;
        yield return WorkflowProtocolTools.QueryWorkflowGraph;
        yield return WorkflowProtocolTools.GetWorkflowTargetCapabilities;
        yield return WorkflowProtocolTools.GetWorkflowPropertyValue;
        yield return WorkflowProtocolTools.ValidateWorkflowPatch;
        yield return WorkflowProtocolTools.ApplyWorkflowPatch;
        yield return WorkflowProtocolTools.InvokeWorkflowActionAsync;
        yield return WorkflowProtocolTools.InvokeWorkflowCommandAsync;
        yield return WorkflowProtocolTools.InvokeWorkflowMethodAsync;
        yield return WorkflowProtocolTools.GetWorkflowChanges;
        yield return WorkflowProtocolTools.GetWorkflowDiagnostics;
        yield return WorkflowProtocolTools.ReleaseWorkflowProtocolSession;

        yield return WorkflowContextProvider.GetWorkflowHelper;
        yield return WorkflowContextProvider.GetComponentContext;
    }
}
