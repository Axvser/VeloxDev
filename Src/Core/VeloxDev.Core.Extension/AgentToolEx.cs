using System;
using System.Collections.Generic;
using VeloxDev.Core.Extension.Agent.Workflow;

namespace VeloxDev.Core.Extension;

public static class AgentToolEx
{
    public static IEnumerable<Delegate> ProvideWorkflowAgentTools()
    {
        yield return WorkflowContextProvider.GetWorkflowHelper;
        yield return WorkflowContextProvider.GetComponentContext;
        yield return WorkflowAgentTools.CreateWorkflowSession;
        yield return WorkflowAgentTools.GetWorkflowSessionState;
        yield return WorkflowAgentTools.ReleaseWorkflowSession;
        yield return WorkflowAgentTools.NormalizeWorkflowTreeJson;
        yield return WorkflowAgentTools.CloseWorkflowTreeAsync;
        yield return WorkflowAgentTools.GetWorkflowNodeJson;
        yield return WorkflowAgentTools.CreateWorkflowNode;
        yield return WorkflowAgentTools.DeleteWorkflowNode;
        yield return WorkflowAgentTools.MoveWorkflowNode;
        yield return WorkflowAgentTools.SetWorkflowNodeAnchor;
        yield return WorkflowAgentTools.SetWorkflowNodeSize;
        yield return WorkflowAgentTools.SetWorkflowNodeBroadcastMode;
        yield return WorkflowAgentTools.SetWorkflowNodeReverseBroadcastMode;
        yield return WorkflowAgentTools.InvokeWorkflowNodeWorkAsync;
        yield return WorkflowAgentTools.InvokeWorkflowNodeBroadcastAsync;
        yield return WorkflowAgentTools.InvokeWorkflowNodeReverseBroadcastAsync;
        yield return WorkflowAgentTools.GetWorkflowSlotJson;
        yield return WorkflowAgentTools.CreateWorkflowSlot;
        yield return WorkflowAgentTools.DeleteWorkflowSlot;
        yield return WorkflowAgentTools.SetWorkflowSlotSize;
        yield return WorkflowAgentTools.SetWorkflowSlotChannel;
        yield return WorkflowAgentTools.ValidateWorkflowConnection;
        yield return WorkflowAgentTools.ConnectWorkflowSlots;
        yield return WorkflowAgentTools.GetWorkflowLinkJson;
        yield return WorkflowAgentTools.DeleteWorkflowLink;
        yield return WorkflowAgentTools.SetWorkflowLinkVisibility;
        yield return WorkflowAgentTools.SetWorkflowPointer;
        yield return WorkflowAgentTools.ResetWorkflowVirtualLink;
        yield return WorkflowAgentTools.UndoWorkflowTree;
        yield return WorkflowAgentTools.RedoWorkflowTree;
        yield return WorkflowAgentTools.ClearWorkflowTreeHistory;
    }
}
