using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Agent.Workflow;

public static class WorkflowAgentTools
{
    private const string SessionRequestJsonDescription = "A JSON request string containing `sessionId`.";
    private const string TreeRequestJsonDescription = "A JSON request string containing optional `sessionId` and required `tree`, or both when refreshing a live workflow session with the latest workflow tree snapshot.";
    private const string NodeRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, and required `nodeIndex`.";
    private const string CreateNodeRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, and required `node` JSON for the node to create.";
    private const string MoveNodeRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `nodeIndex`, and required `offset` JSON.";
    private const string SetNodeAnchorRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `nodeIndex`, and required `anchor` JSON.";
    private const string SetNodeSizeRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `nodeIndex`, and required `size` JSON.";
    private const string NodeOperationRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `nodeIndex`, and optional `parameter` JSON passed to the node runtime operation.";
    private const string SlotRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `nodeIndex`, and required `slotIndex`.";
    private const string CreateSlotRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `nodeIndex`, and required `slot` JSON for the slot to create.";
    private const string SetSlotSizeRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `nodeIndex`, required `slotIndex`, and required `size` JSON.";
    private const string SetSlotChannelRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `nodeIndex`, required `slotIndex`, and required `channel` enum value.";
    private const string ConnectSlotsRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, `senderNodeIndex`, `senderSlotIndex`, `receiverNodeIndex`, and `receiverSlotIndex`.";
    private const string LinkRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, and required `linkIndex`.";
    private const string SetLinkVisibilityRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, required `linkIndex`, and required `isVisible`.";
    private const string SetPointerRequestJsonDescription = "A JSON request string containing optional `sessionId`, optional `tree`, and required `anchor` JSON for the workflow pointer.";

    private static readonly object SessionSyncRoot = new();
    private static readonly Dictionary<string, IWorkflowTreeViewModel> Sessions = [];

    internal static string CreateBoundScopeSession(IWorkflowTreeViewModel tree)
    {
        if (tree is null)
        {
            throw new ArgumentNullException(nameof(tree));
        }

        var sessionId = Guid.NewGuid().ToString("N");
        SetSession(sessionId, tree);
        return sessionId;
    }

    internal static bool ReleaseBoundScopeSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return RemoveSession(sessionId);
    }

    [Description("Create or refresh a live workflow runtime session from a workflow tree JSON payload. Use session mode for runtime-only operations such as undo and redo.")]
    public static string CreateWorkflowSession([Description(TreeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowTreeRequest>(requestJson);
        var tree = DeserializeTree(request.Tree, nameof(request.Tree));
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId;

        SetSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Get the current workflow tree snapshot from an existing live workflow session.")]
    public static string GetWorkflowSessionState([Description(SessionRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSessionRequest>(requestJson);
        var sessionId = RequireSessionId(request.SessionId);
        var tree = GetSession(sessionId);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Release a live workflow session and report whether the session was removed.")]
    public static string ReleaseWorkflowSession([Description(SessionRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSessionRequest>(requestJson);
        var sessionId = RequireSessionId(request.SessionId);
        var released = RemoveSession(sessionId);

        return new JObject
        {
            ["sessionId"] = sessionId,
            ["released"] = released
        }.ToString(Formatting.Indented);
    }

    [Description("Load a workflow tree from JSON, validate that it can run, and return a normalized workflow tree snapshot.")]
    public static string NormalizeWorkflowTreeJson([Description(TreeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowTreeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Close a workflow tree safely by invoking the runtime close pipeline on the tree, its nodes, its slots, and its links.")]
    public static async Task<string> CloseWorkflowTreeAsync([Description(TreeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowTreeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        await tree.GetHelper().CloseAsync().ConfigureAwait(false);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Get one node JSON snapshot from a workflow tree by zero-based nodeIndex.")]
    public static string GetWorkflowNodeJson([Description(NodeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowNodeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var node = GetNode(tree, request.NodeIndex);
        return CreateNodeResponse(tree, node, sessionId);
    }

    [Description("Create a node from request.node and append it into the workflow tree.")]
    public static string CreateWorkflowNode([Description(CreateNodeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowCreateNodeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var node = DeserializeNode(request.Node, nameof(request.Node));
        tree.GetHelper().CreateNode(node);
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Delete the node identified by nodeIndex from the workflow tree.")]
    public static string DeleteWorkflowNode([Description(NodeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowNodeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        GetNode(tree, request.NodeIndex).GetHelper().Delete();
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Move the node identified by nodeIndex with the request.offset value.")]
    public static string MoveWorkflowNode([Description(MoveNodeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowMoveNodeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var node = GetNode(tree, request.NodeIndex);
        node.GetHelper().Move(DeserializeValue<Offset>(request.Offset, nameof(request.Offset)));
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Set the anchor of the node identified by nodeIndex with the request.anchor value.")]
    public static string SetWorkflowNodeAnchor([Description(SetNodeAnchorRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSetNodeAnchorRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var node = GetNode(tree, request.NodeIndex);
        node.GetHelper().SetAnchor(DeserializeValue<Anchor>(request.Anchor, nameof(request.Anchor)));
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Set the size of the node identified by nodeIndex with the request.size value.")]
    public static string SetWorkflowNodeSize([Description(SetNodeSizeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSetNodeSizeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var node = GetNode(tree, request.NodeIndex);
        node.GetHelper().SetSize(DeserializeValue<Size>(request.Size, nameof(request.Size)));
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Invoke WorkAsync on the node identified by nodeIndex and pass request.parameter as the runtime parameter.")]
    public static async Task<string> InvokeWorkflowNodeWorkAsync([Description(NodeOperationRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowNodeOperationRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var node = GetNode(tree, request.NodeIndex);
        await node.GetHelper().WorkAsync(ConvertParameter(request.Parameter), default).ConfigureAwait(false);
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Invoke BroadcastAsync on the node identified by nodeIndex and pass request.parameter as the runtime parameter.")]
    public static async Task<string> InvokeWorkflowNodeBroadcastAsync([Description(NodeOperationRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowNodeOperationRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var node = GetNode(tree, request.NodeIndex);
        await node.GetHelper().BroadcastAsync(ConvertParameter(request.Parameter), default).ConfigureAwait(false);
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Get one slot JSON snapshot by zero-based nodeIndex and slotIndex.")]
    public static string GetWorkflowSlotJson([Description(SlotRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSlotRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var slot = GetSlot(tree, request.NodeIndex, request.SlotIndex);
        return CreateSlotResponse(tree, slot, sessionId);
    }

    [Description("Create a slot from request.slot and append it into the node identified by nodeIndex.")]
    public static string CreateWorkflowSlot([Description(CreateSlotRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowCreateSlotRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var node = GetNode(tree, request.NodeIndex);
        var slot = DeserializeSlot(request.Slot, nameof(request.Slot));
        node.GetHelper().CreateSlot(slot);
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Delete the slot identified by nodeIndex and slotIndex.")]
    public static string DeleteWorkflowSlot([Description(SlotRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSlotRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        GetSlot(tree, request.NodeIndex, request.SlotIndex).GetHelper().Delete();
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Set the size of the slot identified by nodeIndex and slotIndex with the request.size value.")]
    public static string SetWorkflowSlotSize([Description(SetSlotSizeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSetSlotSizeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var slot = GetSlot(tree, request.NodeIndex, request.SlotIndex);
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Set the channel of the slot identified by nodeIndex and slotIndex.")]
    public static string SetWorkflowSlotChannel([Description(SetSlotChannelRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSetSlotChannelRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var slot = GetSlot(tree, request.NodeIndex, request.SlotIndex);
        slot.GetHelper().SetChannel(DeserializeEnum<SlotChannel>(request.Channel, nameof(request.Channel)));
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Validate whether the sender slot and receiver slot can be connected according to the current tree helper.")]
    public static string ValidateWorkflowConnection([Description(ConnectSlotsRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowConnectSlotsRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var sender = GetSlot(tree, request.SenderNodeIndex, request.SenderSlotIndex);
        var receiver = GetSlot(tree, request.ReceiverNodeIndex, request.ReceiverSlotIndex);
        var isValid = tree.GetHelper().ValidateConnection(sender, receiver);

        return new JObject
        {
            ["sessionId"] = CreateSessionToken(sessionId),
            ["isValid"] = isValid,
            ["tree"] = ParseJson(tree.Serialize())
        }.ToString(Formatting.Indented);
    }

    [Description("Create a connection between the sender slot and the receiver slot identified by the request indices.")]
    public static string ConnectWorkflowSlots([Description(ConnectSlotsRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowConnectSlotsRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var sender = GetSlot(tree, request.SenderNodeIndex, request.SenderSlotIndex);
        var receiver = GetSlot(tree, request.ReceiverNodeIndex, request.ReceiverSlotIndex);
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Get one link JSON snapshot from the workflow tree by zero-based linkIndex.")]
    public static string GetWorkflowLinkJson([Description(LinkRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowLinkRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        var link = GetLink(tree, request.LinkIndex);
        return CreateLinkResponse(tree, link, sessionId);
    }

    [Description("Delete the link identified by linkIndex from the workflow tree.")]
    public static string DeleteWorkflowLink([Description(LinkRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowLinkRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        GetLink(tree, request.LinkIndex).GetHelper().Delete();
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Set the IsVisible value of the link identified by linkIndex.")]
    public static string SetWorkflowLinkVisibility([Description(SetLinkVisibilityRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSetLinkVisibilityRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        GetLink(tree, request.LinkIndex).IsVisible = request.IsVisible;
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Set the current workflow pointer by assigning request.anchor to tree.VirtualLink.Receiver.Anchor.")]
    public static string SetWorkflowPointer([Description(SetPointerRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowSetPointerRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        tree.GetHelper().SetPointer(DeserializeValue<Anchor>(request.Anchor, nameof(request.Anchor)));
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Reset the virtual link preview state in the workflow tree.")]
    public static string ResetWorkflowVirtualLink([Description(TreeRequestJsonDescription)] string requestJson)
    {
        var request = DeserializeRequest<WorkflowTreeRequest>(requestJson);
        var (tree, sessionId) = ResolveTree(request);
        tree.GetHelper().ResetVirtualLink();
        PersistSession(sessionId, tree);
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Undo the latest workflow action in a live runtime session. This tool requires session mode because history is not serialized into tree JSON.")]
    public static string UndoWorkflowTree([Description(SessionRequestJsonDescription)] string requestJson)
    {
        var sessionId = GetSessionRequestId(requestJson);
        var tree = GetSession(sessionId);
        tree.GetHelper().Undo();
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Redo the latest workflow action in a live runtime session. This tool requires session mode because history is not serialized into tree JSON.")]
    public static string RedoWorkflowTree([Description(SessionRequestJsonDescription)] string requestJson)
    {
        var sessionId = GetSessionRequestId(requestJson);
        var tree = GetSession(sessionId);
        tree.GetHelper().Redo();
        return CreateTreeResponse(tree, sessionId);
    }

    [Description("Clear runtime undo and redo history in a live workflow session. This tool requires session mode.")]
    public static string ClearWorkflowTreeHistory([Description(SessionRequestJsonDescription)] string requestJson)
    {
        var sessionId = GetSessionRequestId(requestJson);
        var tree = GetSession(sessionId);
        tree.GetHelper().ClearHistory();
        return CreateTreeResponse(tree, sessionId);
    }

    private static TRequest DeserializeRequest<TRequest>(string requestJson)
        where TRequest : class
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            throw new ArgumentException("Request JSON cannot be null or empty.", nameof(requestJson));
        }

        var request = JsonConvert.DeserializeObject<TRequest>(requestJson);
        return request ?? throw new JsonSerializationException($"Failed to deserialize request JSON to {typeof(TRequest).Name}.");
    }

    private static (IWorkflowTreeViewModel Tree, string? SessionId) ResolveTree(WorkflowSessionBoundRequest request)
    {
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId;

        if (request.Tree is not null)
        {
            var treeFromPayload = DeserializeTree(request.Tree, nameof(request.Tree));
            PersistSession(sessionId, treeFromPayload);
            return (treeFromPayload, sessionId);
        }

        if (sessionId is not null)
        {
            return (GetSession(sessionId), sessionId);
        }

        throw new ArgumentException("Request must contain either sessionId or tree.");
    }

    private static string GetSessionRequestId(string requestJson)
    {
        var request = DeserializeRequest<WorkflowSessionRequest>(requestJson);
        return RequireSessionId(request.SessionId);
    }

    private static string RequireSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId cannot be null or empty.", nameof(sessionId));
        }

        return sessionId;
    }

    private static IWorkflowTreeViewModel GetSession(string sessionId)
    {
        lock (SessionSyncRoot)
        {
            if (Sessions.TryGetValue(sessionId, out var tree))
            {
                return tree;
            }
        }

        throw new KeyNotFoundException($"Workflow session '{sessionId}' was not found.");
    }

    private static void SetSession(string sessionId, IWorkflowTreeViewModel tree)
    {
        lock (SessionSyncRoot)
        {
            Sessions[sessionId] = tree;
        }
    }

    private static bool RemoveSession(string sessionId)
    {
        lock (SessionSyncRoot)
        {
            return Sessions.Remove(sessionId);
        }
    }

    private static void PersistSession(string? sessionId, IWorkflowTreeViewModel tree)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            SetSession(sessionId, tree);
        }
    }

    private static IWorkflowTreeViewModel DeserializeTree(JToken? token, string propertyName)
    {
        var json = ExtractJson(token, propertyName);
        return json.TryDeserialize<IWorkflowTreeViewModel>(out var tree) && tree is not null
            ? tree
            : throw new JsonSerializationException("Failed to deserialize workflow tree JSON.");
    }

    private static IWorkflowNodeViewModel DeserializeNode(JToken? token, string propertyName)
    {
        var json = ExtractJson(token, propertyName);
        return json.TryDeserialize<IWorkflowNodeViewModel>(out var node) && node is not null
            ? node
            : throw new JsonSerializationException("Failed to deserialize workflow node JSON.");
    }

    private static IWorkflowSlotViewModel DeserializeSlot(JToken? token, string propertyName)
    {
        var json = ExtractJson(token, propertyName);
        return json.TryDeserialize<IWorkflowSlotViewModel>(out var slot) && slot is not null
            ? slot
            : throw new JsonSerializationException("Failed to deserialize workflow slot JSON.");
    }

    private static T DeserializeValue<T>(JToken? token, string propertyName)
    {
        var json = ExtractJson(token, propertyName);
        var value = JsonConvert.DeserializeObject<T>(json);
        return value is not null
            ? value
            : throw new JsonSerializationException($"Failed to deserialize '{propertyName}' to {typeof(T).Name}.");
    }

    private static TEnum DeserializeEnum<TEnum>(JToken? token, string propertyName)
        where TEnum : struct
    {
        if (token is null)
        {
            throw new ArgumentException($"{propertyName} cannot be null.", propertyName);
        }

        if (token.Type == JTokenType.String)
        {
            var text = token.Value<string>();
            if (Enum.TryParse(text, true, out TEnum enumValue))
            {
                return enumValue;
            }
        }

        if (token.Type == JTokenType.Integer)
        {
            var numericValue = token.Value<int>();
            if (Enum.IsDefined(typeof(TEnum), numericValue))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
            }
        }

        throw new ArgumentException($"{propertyName} contains an invalid enum value for {typeof(TEnum).Name}.", propertyName);
    }

    private static object? ConvertParameter(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        return token.ToObject<object>();
    }

    private static string ExtractJson(JToken? token, string propertyName)
    {
        if (token is null)
        {
            throw new ArgumentException($"{propertyName} cannot be null.", propertyName);
        }

        return token.Type == JTokenType.String
            ? token.Value<string>() ?? throw new ArgumentException($"{propertyName} cannot be an empty JSON string.", propertyName)
            : token.ToString(Formatting.None);
    }

    private static IWorkflowNodeViewModel GetNode(IWorkflowTreeViewModel tree, int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= tree.Nodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeIndex), $"nodeIndex {nodeIndex} is out of range. Nodes.Count = {tree.Nodes.Count}.");
        }

        return tree.Nodes[nodeIndex];
    }

    private static IWorkflowSlotViewModel GetSlot(IWorkflowTreeViewModel tree, int nodeIndex, int slotIndex)
    {
        var node = GetNode(tree, nodeIndex);
        if (slotIndex < 0 || slotIndex >= node.Slots.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), $"slotIndex {slotIndex} is out of range. Slots.Count = {node.Slots.Count}.");
        }

        return node.Slots[slotIndex];
    }

    private static IWorkflowLinkViewModel GetLink(IWorkflowTreeViewModel tree, int linkIndex)
    {
        if (linkIndex < 0 || linkIndex >= tree.Links.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(linkIndex), $"linkIndex {linkIndex} is out of range. Links.Count = {tree.Links.Count}.");
        }

        return tree.Links[linkIndex];
    }

    private static string CreateTreeResponse(IWorkflowTreeViewModel tree, string? sessionId)
        => new JObject
        {
            ["sessionId"] = CreateSessionToken(sessionId),
            ["tree"] = ParseJson(tree.Serialize())
        }.ToString(Formatting.Indented);

    private static string CreateNodeResponse(IWorkflowTreeViewModel tree, IWorkflowNodeViewModel node, string? sessionId)
        => new JObject
        {
            ["sessionId"] = CreateSessionToken(sessionId),
            ["tree"] = ParseJson(tree.Serialize()),
            ["node"] = ParseJson(node.Serialize())
        }.ToString(Formatting.Indented);

    private static string CreateSlotResponse(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel slot, string? sessionId)
        => new JObject
        {
            ["sessionId"] = CreateSessionToken(sessionId),
            ["tree"] = ParseJson(tree.Serialize()),
            ["slot"] = ParseJson(slot.Serialize())
        }.ToString(Formatting.Indented);

    private static string CreateLinkResponse(IWorkflowTreeViewModel tree, IWorkflowLinkViewModel link, string? sessionId)
        => new JObject
        {
            ["sessionId"] = CreateSessionToken(sessionId),
            ["tree"] = ParseJson(tree.Serialize()),
            ["link"] = ParseJson(link.Serialize())
        }.ToString(Formatting.Indented);

    private static JToken CreateSessionToken(string? sessionId)
        => string.IsNullOrWhiteSpace(sessionId)
            ? JValue.CreateNull()
            : new JValue(sessionId);

    private static JToken ParseJson(string json)
        => JToken.Parse(json);

    private class WorkflowSessionBoundRequest
    {
        public string? SessionId { get; set; }
        public JToken? Tree { get; set; }
    }

    private class WorkflowTreeRequest : WorkflowSessionBoundRequest
    {
    }

    private sealed class WorkflowSessionRequest
    {
        public string? SessionId { get; set; }
    }

    private class WorkflowNodeRequest : WorkflowSessionBoundRequest
    {
        public int NodeIndex { get; set; }
    }

    private sealed class WorkflowCreateNodeRequest : WorkflowSessionBoundRequest
    {
        public JToken? Node { get; set; }
    }

    private sealed class WorkflowMoveNodeRequest : WorkflowNodeRequest
    {
        public JToken? Offset { get; set; }
    }

    private sealed class WorkflowSetNodeAnchorRequest : WorkflowNodeRequest
    {
        public JToken? Anchor { get; set; }
    }

    private sealed class WorkflowSetNodeSizeRequest : WorkflowNodeRequest
    {
        public JToken? Size { get; set; }
    }

    private sealed class WorkflowNodeOperationRequest : WorkflowNodeRequest
    {
        public JToken? Parameter { get; set; }
    }

    private class WorkflowSlotRequest : WorkflowNodeRequest
    {
        public int SlotIndex { get; set; }
    }

    private sealed class WorkflowCreateSlotRequest : WorkflowNodeRequest
    {
        public JToken? Slot { get; set; }
    }

    private sealed class WorkflowSetSlotSizeRequest : WorkflowSlotRequest
    {
        public JToken? Size { get; set; }
    }

    private sealed class WorkflowSetSlotChannelRequest : WorkflowSlotRequest
    {
        public JToken? Channel { get; set; }
    }

    private sealed class WorkflowConnectSlotsRequest : WorkflowSessionBoundRequest
    {
        public int SenderNodeIndex { get; set; }
        public int SenderSlotIndex { get; set; }
        public int ReceiverNodeIndex { get; set; }
        public int ReceiverSlotIndex { get; set; }
    }

    private class WorkflowLinkRequest : WorkflowSessionBoundRequest
    {
        public int LinkIndex { get; set; }
    }

    private sealed class WorkflowSetLinkVisibilityRequest : WorkflowLinkRequest
    {
        public bool IsVisible { get; set; }
    }

    private sealed class WorkflowSetPointerRequest : WorkflowSessionBoundRequest
    {
        public JToken? Anchor { get; set; }
    }
}
