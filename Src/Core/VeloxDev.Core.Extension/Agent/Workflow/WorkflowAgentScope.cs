using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Extension.Agent.Workflow;

public sealed class WorkflowAgentScope(IWorkflowTreeViewModel tree) : IDisposable
{
    private const string ScopedNodeRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex`. Do not provide `sessionId` or `tree`.";
    private const string ScopedCreateNodeRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `node` JSON. Do not provide `sessionId` or `tree`.";
    private const string ScopedMoveNodeRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex` and required `offset` JSON. Do not provide `sessionId` or `tree`.";
    private const string ScopedSetNodeAnchorRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex` and required `anchor` JSON. Do not provide `sessionId` or `tree`.";
    private const string ScopedSetNodeSizeRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex` and required `size` JSON. Do not provide `sessionId` or `tree`.";
    private const string ScopedSetNodeBroadcastModeRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex` and required `broadcastMode` enum value. Do not provide `sessionId` or `tree`.";
    private const string ScopedNodeOperationRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex` and optional `parameter`. Do not provide `sessionId` or `tree`.";
    private const string ScopedSlotRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex` and required `slotIndex`. Do not provide `sessionId` or `tree`.";
    private const string ScopedCreateSlotRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex` and required `slot` JSON. Do not provide `sessionId` or `tree`.";
    private const string ScopedSetSlotSizeRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex`, required `slotIndex`, and required `size` JSON. Do not provide `sessionId` or `tree`.";
    private const string ScopedSetSlotChannelRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `nodeIndex`, required `slotIndex`, and required `channel` enum value. Do not provide `sessionId` or `tree`.";
    private const string ScopedConnectSlotsRequestJsonDescription = "A JSON request string for the bound workflow tree containing `senderNodeIndex`, `senderSlotIndex`, `receiverNodeIndex`, and `receiverSlotIndex`. Do not provide `sessionId` or `tree`.";
    private const string ScopedLinkRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `linkIndex`. Do not provide `sessionId` or `tree`.";
    private const string ScopedSetLinkVisibilityRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `linkIndex` and required `isVisible`. Do not provide `sessionId` or `tree`.";
    private const string ScopedSetPointerRequestJsonDescription = "A JSON request string for the bound workflow tree containing required `anchor` JSON. Do not provide `sessionId` or `tree`.";

    private readonly string _sessionId = WorkflowAgentTools.CreateBoundScopeSession(tree ?? throw new ArgumentNullException(nameof(tree)));
    private bool _disposed;

    public IWorkflowTreeViewModel Tree { get; } = tree;

    public IEnumerable<Delegate> ProvideWorkflowAgentTools()
    {
        ThrowIfDisposed();
        yield return WorkflowContextProvider.GetWorkflowHelper;
        yield return WorkflowContextProvider.GetComponentContext;
        yield return GetWorkflowTreeState;
        yield return NormalizeWorkflowTreeJson;
        yield return CloseWorkflowTreeAsync;
        yield return GetWorkflowNodeJson;
        yield return CreateWorkflowNode;
        yield return DeleteWorkflowNode;
        yield return MoveWorkflowNode;
        yield return SetWorkflowNodeAnchor;
        yield return SetWorkflowNodeSize;
        yield return SetWorkflowNodeBroadcastMode;
        yield return SetWorkflowNodeReverseBroadcastMode;
        yield return InvokeWorkflowNodeWorkAsync;
        yield return InvokeWorkflowNodeBroadcastAsync;
        yield return InvokeWorkflowNodeReverseBroadcastAsync;
        yield return GetWorkflowSlotJson;
        yield return CreateWorkflowSlot;
        yield return DeleteWorkflowSlot;
        yield return SetWorkflowSlotSize;
        yield return SetWorkflowSlotChannel;
        yield return ValidateWorkflowConnection;
        yield return ConnectWorkflowSlots;
        yield return GetWorkflowLinkJson;
        yield return DeleteWorkflowLink;
        yield return SetWorkflowLinkVisibility;
        yield return SetWorkflowPointer;
        yield return ResetWorkflowVirtualLink;
        yield return UndoWorkflowTree;
        yield return RedoWorkflowTree;
        yield return ClearWorkflowTreeHistory;
    }

    [Description("Get the current workflow tree snapshot for the bound workflow tree scope.")]
    public string GetWorkflowTreeState()
        => StripSessionId(WorkflowAgentTools.GetWorkflowSessionState(BuildScopedRequest(null)));

    [Description("Normalize and return the current workflow tree snapshot for the bound workflow tree scope.")]
    public string NormalizeWorkflowTreeJson()
        => StripSessionId(WorkflowAgentTools.NormalizeWorkflowTreeJson(BuildScopedRequest(null)));

    [Description("Close the bound workflow tree safely by invoking the runtime close pipeline on the tree, its nodes, its slots, and its links.")]
    public async Task<string> CloseWorkflowTreeAsync()
        => StripSessionId(await WorkflowAgentTools.CloseWorkflowTreeAsync(BuildScopedRequest(null)).ConfigureAwait(false));

    [Description("Get one node JSON snapshot from the bound workflow tree by zero-based nodeIndex.")]
    public string GetWorkflowNodeJson([Description(ScopedNodeRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.GetWorkflowNodeJson, requestJson);

    [Description("Create a node inside the bound workflow tree from request.node.")]
    public string CreateWorkflowNode([Description(ScopedCreateNodeRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.CreateWorkflowNode, requestJson);

    [Description("Delete the node identified by nodeIndex from the bound workflow tree.")]
    public string DeleteWorkflowNode([Description(ScopedNodeRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.DeleteWorkflowNode, requestJson);

    [Description("Move the node identified by nodeIndex with the request.offset value inside the bound workflow tree.")]
    public string MoveWorkflowNode([Description(ScopedMoveNodeRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.MoveWorkflowNode, requestJson);

    [Description("Set the anchor of the node identified by nodeIndex inside the bound workflow tree.")]
    public string SetWorkflowNodeAnchor([Description(ScopedSetNodeAnchorRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.SetWorkflowNodeAnchor, requestJson);

    [Description("Set the size of the node identified by nodeIndex inside the bound workflow tree.")]
    public string SetWorkflowNodeSize([Description(ScopedSetNodeSizeRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.SetWorkflowNodeSize, requestJson);

    [Description("Set the forward broadcast mode of the node identified by nodeIndex inside the bound workflow tree.")]
    public string SetWorkflowNodeBroadcastMode([Description(ScopedSetNodeBroadcastModeRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.SetWorkflowNodeBroadcastMode, requestJson);

    [Description("Set the reverse broadcast mode of the node identified by nodeIndex inside the bound workflow tree.")]
    public string SetWorkflowNodeReverseBroadcastMode([Description(ScopedSetNodeBroadcastModeRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.SetWorkflowNodeReverseBroadcastMode, requestJson);

    [Description("Invoke WorkAsync on the node identified by nodeIndex inside the bound workflow tree.")]
    public Task<string> InvokeWorkflowNodeWorkAsync([Description(ScopedNodeOperationRequestJsonDescription)] string requestJson)
        => ExecuteAsync(WorkflowAgentTools.InvokeWorkflowNodeWorkAsync, requestJson);

    [Description("Invoke BroadcastAsync on the node identified by nodeIndex inside the bound workflow tree.")]
    public Task<string> InvokeWorkflowNodeBroadcastAsync([Description(ScopedNodeOperationRequestJsonDescription)] string requestJson)
        => ExecuteAsync(WorkflowAgentTools.InvokeWorkflowNodeBroadcastAsync, requestJson);

    [Description("Invoke ReverseBroadcastAsync on the node identified by nodeIndex inside the bound workflow tree.")]
    public Task<string> InvokeWorkflowNodeReverseBroadcastAsync([Description(ScopedNodeOperationRequestJsonDescription)] string requestJson)
        => ExecuteAsync(WorkflowAgentTools.InvokeWorkflowNodeReverseBroadcastAsync, requestJson);

    [Description("Get one slot JSON snapshot by zero-based nodeIndex and slotIndex from the bound workflow tree.")]
    public string GetWorkflowSlotJson([Description(ScopedSlotRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.GetWorkflowSlotJson, requestJson);

    [Description("Create a slot inside the bound workflow tree from request.slot.")]
    public string CreateWorkflowSlot([Description(ScopedCreateSlotRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.CreateWorkflowSlot, requestJson);

    [Description("Delete the slot identified by nodeIndex and slotIndex from the bound workflow tree.")]
    public string DeleteWorkflowSlot([Description(ScopedSlotRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.DeleteWorkflowSlot, requestJson);

    [Description("Set the size of the slot identified by nodeIndex and slotIndex inside the bound workflow tree.")]
    public string SetWorkflowSlotSize([Description(ScopedSetSlotSizeRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.SetWorkflowSlotSize, requestJson);

    [Description("Set the channel of the slot identified by nodeIndex and slotIndex inside the bound workflow tree.")]
    public string SetWorkflowSlotChannel([Description(ScopedSetSlotChannelRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.SetWorkflowSlotChannel, requestJson);

    [Description("Validate whether two slots can be connected inside the bound workflow tree.")]
    public string ValidateWorkflowConnection([Description(ScopedConnectSlotsRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.ValidateWorkflowConnection, requestJson);

    [Description("Create a connection between two slots inside the bound workflow tree.")]
    public string ConnectWorkflowSlots([Description(ScopedConnectSlotsRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.ConnectWorkflowSlots, requestJson);

    [Description("Get one link JSON snapshot by zero-based linkIndex from the bound workflow tree.")]
    public string GetWorkflowLinkJson([Description(ScopedLinkRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.GetWorkflowLinkJson, requestJson);

    [Description("Delete the link identified by linkIndex from the bound workflow tree.")]
    public string DeleteWorkflowLink([Description(ScopedLinkRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.DeleteWorkflowLink, requestJson);

    [Description("Set the IsVisible value of the link identified by linkIndex inside the bound workflow tree.")]
    public string SetWorkflowLinkVisibility([Description(ScopedSetLinkVisibilityRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.SetWorkflowLinkVisibility, requestJson);

    [Description("Set the current workflow pointer inside the bound workflow tree.")]
    public string SetWorkflowPointer([Description(ScopedSetPointerRequestJsonDescription)] string requestJson)
        => Execute(WorkflowAgentTools.SetWorkflowPointer, requestJson);

    [Description("Reset the virtual link preview state inside the bound workflow tree.")]
    public string ResetWorkflowVirtualLink()
        => StripSessionId(WorkflowAgentTools.ResetWorkflowVirtualLink(BuildScopedRequest(null)));

    [Description("Undo the latest workflow action inside the bound workflow tree.")]
    public string UndoWorkflowTree()
        => StripSessionId(WorkflowAgentTools.UndoWorkflowTree(BuildScopedRequest(null)));

    [Description("Redo the latest workflow action inside the bound workflow tree.")]
    public string RedoWorkflowTree()
        => StripSessionId(WorkflowAgentTools.RedoWorkflowTree(BuildScopedRequest(null)));

    [Description("Clear runtime undo and redo history inside the bound workflow tree.")]
    public string ClearWorkflowTreeHistory()
        => StripSessionId(WorkflowAgentTools.ClearWorkflowTreeHistory(BuildScopedRequest(null)));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        WorkflowAgentTools.ReleaseBoundScopeSession(_sessionId);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private string Execute(Func<string, string> tool, string requestJson)
        => StripSessionId(tool.Invoke(BuildScopedRequest(requestJson)));

    private async Task<string> ExecuteAsync(Func<string, Task<string>> tool, string requestJson)
        => StripSessionId(await tool.Invoke(BuildScopedRequest(requestJson)).ConfigureAwait(false));

    private string BuildScopedRequest(string? requestJson)
    {
        ThrowIfDisposed();
        var request = string.IsNullOrWhiteSpace(requestJson)
            ? new JObject()
            : JObject.Parse(requestJson);

        if (request.Property("sessionId", StringComparison.OrdinalIgnoreCase) is not null)
        {
            throw new ArgumentException("Scoped workflow agent requests must not contain `sessionId`.", nameof(requestJson));
        }

        if (request.Property("tree", StringComparison.OrdinalIgnoreCase) is not null)
        {
            throw new ArgumentException("Scoped workflow agent requests must not contain `tree`.", nameof(requestJson));
        }

        request["sessionId"] = _sessionId;
        return request.ToString(Formatting.None);
    }

    private static string StripSessionId(string responseJson)
    {
        var response = JObject.Parse(responseJson);
        response.Remove("sessionId");
        return response.ToString(Formatting.Indented);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorkflowAgentScope));
        }
    }
}
