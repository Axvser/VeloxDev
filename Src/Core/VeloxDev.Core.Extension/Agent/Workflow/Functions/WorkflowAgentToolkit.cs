using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Provides MAF-compatible <see cref="AITool"/> instances that give an Agent
/// full operational control over a single <see cref="IWorkflowTreeViewModel"/>.
/// All JSON output uses <see cref="Formatting.None"/> to minimize token consumption.
/// </summary>
public sealed class WorkflowAgentToolkit
{
    private readonly WorkflowAgentScope _scope;
    private readonly WorkflowStateTracker _tracker;
    private int _toolCallCount;
    private IWorkflowTreeViewModel Tree => _scope.Tree;

    public WorkflowAgentToolkit(WorkflowAgentScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _tracker = new WorkflowStateTracker(scope.Tree);
    }

    /// <summary>
    /// Creates all AI tools for workflow operations within the scoped tree.
    /// Every tool is wrapped with <see cref="TrackedAIFunction"/> so that
    /// <see cref="Track"/> is invoked after each call.
    /// </summary>
    public IList<AITool> CreateTools()
    {
        AITool T(Delegate method, string name)
            => new TrackedAIFunction(AIFunctionFactory.Create(method, name), this);

        var tools = new List<AITool>
        {
            // ── Query ──
            T(ListNodes, nameof(ListNodes)),
            T(GetNodeDetail, nameof(GetNodeDetail)),
            T(GetNodeDetailById, nameof(GetNodeDetailById)),
            T(ListConnections, nameof(ListConnections)),
            T(GetTypeSchema, nameof(GetTypeSchema)),
            // ── Progressive Context ──
            T(GetWorkflowSummary, nameof(GetWorkflowSummary)),
            T(GetComponentContext, nameof(GetComponentContext)),
            T(ListComponentCommands, nameof(ListComponentCommands)),
            // ── State Tracking / Diff ──
            T(TakeSnapshot, nameof(TakeSnapshot)),
            T(GetChangesSinceSnapshot, nameof(GetChangesSinceSnapshot)),
            // ── Mutation ──
            T(MoveNode, nameof(MoveNode)),
            T(SetNodePosition, nameof(SetNodePosition)),
            T(ResizeNode, nameof(ResizeNode)),
            T(DeleteNode, nameof(DeleteNode)),
            T(DeleteSlot, nameof(DeleteSlot)),
            T(ConnectSlots, nameof(ConnectSlots)),
            T(ConnectSlotsById, nameof(ConnectSlotsById)),
            T(DisconnectSlots, nameof(DisconnectSlots)),
            T(ExecuteWork, nameof(ExecuteWork)),
            T(BroadcastNode, nameof(BroadcastNode)),
            T(Undo, nameof(Undo)),
            T(Redo, nameof(Redo)),
            T(PatchNodeProperties, nameof(PatchNodeProperties)),
            T(PatchComponentById, nameof(PatchComponentById)),
            // ── Generic Command Execution ──
            T(ExecuteCommandOnNode, nameof(ExecuteCommandOnNode)),
            T(ExecuteCommandById, nameof(ExecuteCommandById)),
            T(CreateNode, nameof(CreateNode)),
            T(CreateSlotOnNode, nameof(CreateSlotOnNode)),
            // ── Batch ──
            T(BatchExecute, nameof(BatchExecute)),
            // ── Clone ──
            T(CloneNodes, nameof(CloneNodes)),
            // ── Slot Collections ──
            T(ListSlotProperties, nameof(ListSlotProperties)),
            T(AddSlotToCollection, nameof(AddSlotToCollection)),
            T(RemoveSlotFromCollection, nameof(RemoveSlotFromCollection)),
            T(SetEnumSlotCollection, nameof(SetEnumSlotCollection)),
            // ── Search / Shortcut ──
            T(FindNodes, nameof(FindNodes)),
            T(ResolveSlotId, nameof(ResolveSlotId)),
            // ── Graph Traversal ──
            T(SearchForward, nameof(SearchForward)),
            T(SearchReverse, nameof(SearchReverse)),
            T(SearchAllRelative, nameof(SearchAllRelative)),
            T(IsConnected, nameof(IsConnected)),
            T(FindPath, nameof(FindPath)),
            // ── Reverse Broadcast ──
            T(ReverseBroadcastNode, nameof(ReverseBroadcastNode)),
            // ── Connection Management ──
            T(DisconnectSlotsById, nameof(DisconnectSlotsById)),
            T(DisconnectAllFromSlot, nameof(DisconnectAllFromSlot)),
            T(DisconnectAllFromNode, nameof(DisconnectAllFromNode)),
            T(ReplaceConnection, nameof(ReplaceConnection)),
            // ── Slot Channel ──
            T(SetSlotChannel, nameof(SetSlotChannel)),
            // ── Link Inspection ──
            T(GetLinkDetail, nameof(GetLinkDetail)),
            // ── Bulk Operations ──
            T(ExecuteWorkOnNodes, nameof(ExecuteWorkOnNodes)),
            T(BulkPatchNodes, nameof(BulkPatchNodes)),
            // ── Layout ──
            T(AlignNodes, nameof(AlignNodes)),
            T(DistributeNodes, nameof(DistributeNodes)),
            T(AutoLayout, nameof(AutoLayout)),
            // ── Analytics ──
            T(GetNodeStatistics, nameof(GetNodeStatistics)),
            T(ListCreatableTypes, nameof(ListCreatableTypes)),
            T(ValidateWorkflow, nameof(ValidateWorkflow)),
            // ── Composite (reduce round-trips) ──
            T(ConnectByProperty, nameof(ConnectByProperty)),
            T(CreateAndConfigureNode, nameof(CreateAndConfigureNode)),
            T(DeleteNodes, nameof(DeleteNodes)),
            T(ArrangeNodes, nameof(ArrangeNodes)),
            T(GetFullTopology, nameof(GetFullTopology)),
        };

        // Merge developer-registered custom tools
        foreach (var tool in _scope.CustomTools)
            tools.Add(tool);

        return tools;
    }

    /// <summary>
    /// Wraps an <see cref="AIFunction"/> so that <see cref="Track"/> is called
    /// after every invocation, ensuring call counting and callback dispatch.
    /// </summary>
    private sealed class TrackedAIFunction : DelegatingAIFunction
    {
        private readonly WorkflowAgentToolkit _toolkit;

        public TrackedAIFunction(AIFunction inner, WorkflowAgentToolkit toolkit) : base(inner)
        {
            _toolkit = toolkit;
        }

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var result = await base.InvokeCoreAsync(arguments, cancellationToken);
            _toolkit.Track(Name, result?.ToString() ?? string.Empty);
            return result;
        }
    }

    /// <summary>
    /// Wraps a tool result with call counting, callback invocation, and max-call enforcement.
    /// </summary>
    private string Track(string toolName, string result)
    {
        _toolCallCount++;
        _scope.RaiseToolCalled(toolName, result, _toolCallCount);
        if (_scope.MaxToolCalls.HasValue && _toolCallCount > _scope.MaxToolCalls.Value)
            return Error($"Tool call limit ({_scope.MaxToolCalls.Value}) exceeded.");
        return result;
    }

    // ────────────────────────── Query Functions ──────────────────────────

    [Description("Lists all nodes. Returns compact JSON: [{i,id,t,x,y,l,w,h,slots,...props}]. Use GetNodeDetail for full info.")]
    private string ListNodes()
    {
        var nodes = Tree.Nodes;
        var result = new JArray();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var obj = new JObject
            {
                ["i"] = i,
                ["id"] = GetComponentId(node),
                ["t"] = node.GetType().Name,
                ["x"] = node.Anchor.Horizontal,
                ["y"] = node.Anchor.Vertical,
                ["l"] = node.Anchor.Layer,
                ["w"] = node.Size.Width,
                ["h"] = node.Size.Height,
                ["slots"] = node.Slots.Count,
            };
            AppendScalarProperties(obj, node);
            result.Add(obj);
        }
        return result.ToString(Formatting.None);
    }

    [Description("Gets full detail of a node by index: properties, slots with connections. Use ListComponentCommands for commands.")]
    private string GetNodeDetail(
        [Description("Zero-based index of the node.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        return BuildNodeDetailJson(node!, nodeIndex);
    }

    [Description("Gets full detail of a node by runtime ID. Stable across add/remove.")]
    private string GetNodeDetailById(
        [Description("Runtime ID of the node.")] string runtimeId)
    {
        var (node, index) = FindNodeById(runtimeId);
        if (node == null) return Error($"Node '{runtimeId}' not found.");
        return BuildNodeDetailJson(node, index);
    }

    private string BuildNodeDetailJson(IWorkflowNodeViewModel node, int nodeIndex)
    {
        var obj = new JObject
        {
            ["i"] = nodeIndex,
            ["id"] = GetComponentId(node),
            ["t"] = node.GetType().Name,
            ["fullType"] = node.GetType().FullName,
            ["x"] = node.Anchor.Horizontal,
            ["y"] = node.Anchor.Vertical,
            ["l"] = node.Anchor.Layer,
            ["w"] = node.Size.Width,
            ["h"] = node.Size.Height,
        };

        AppendScalarProperties(obj, node);

        // Build slot→property name mapping for richer context
        var slotPropertyMap = BuildSlotPropertyMap(node);

        var slotsArr = new JArray();
        for (int s = 0; s < node.Slots.Count; s++)
        {
            var slot = node.Slots[s];
            var slotObj = new JObject
            {
                ["si"] = s,
                ["id"] = GetComponentId(slot),
                ["ch"] = slot.Channel.ToString(),
                ["st"] = slot.State.ToString(),
            };
            if (slotPropertyMap.TryGetValue(slot, out var propName))
                slotObj["prop"] = propName;

            if (slot.Targets.Count > 0)
            {
                var targets = new JArray();
                foreach (var t in slot.Targets)
                {
                    if (t.Parent != null)
                        targets.Add($"{GetComponentId(t.Parent)}:{GetComponentId(t)}");
                }
                slotObj["tgt"] = targets;
            }

            if (slot.Sources.Count > 0)
            {
                var sources = new JArray();
                foreach (var src in slot.Sources)
                {
                    if (src.Parent != null)
                        sources.Add($"{GetComponentId(src.Parent)}:{GetComponentId(src)}");
                }
                slotObj["src"] = sources;
            }

            AppendScalarProperties(slotObj, slot);
            slotsArr.Add(slotObj);
        }
        obj["slots"] = slotsArr;

        return obj.ToString(Formatting.None);
    }

    [Description("Lists all visible connections. Compact: [{id,sid,rid}] where sid/rid are slot runtime IDs.")]
    private string ListConnections()
    {
        var links = Tree.Links;
        var result = new JArray();
        for (int i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (!link.IsVisible) continue;

            result.Add(new JObject
            {
                ["id"] = GetComponentId(link),
                ["sid"] = link.Sender != null ? GetComponentId(link.Sender) : null,
                ["rid"] = link.Receiver != null ? GetComponentId(link.Receiver) : null,
            });
        }
        return result.ToString(Formatting.None);
    }

    // ────────────────────────── Mutation Functions ──────────────────────────

    [Description("Moves a node by relative offset. Coordinate system: +offsetX = rightward, +offsetY = downward (origin is top-left). Undo-able.")]
    private string MoveNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Horizontal offset px.")] double offsetX,
        [Description("Vertical offset px.")] double offsetY)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        node!.MoveCommand.Execute(new Offset(offsetX, offsetY));
        return Ok($"Moved {nodeIndex} by ({offsetX},{offsetY}).");
    }

    [Description("Sets absolute position of a node. Coordinate system: origin (0,0) is top-left; left (X) increases rightward, top (Y) increases downward.")]
    private string SetNodePosition(
        [Description("Node index.")] int nodeIndex,
        [Description("Left px.")] double left,
        [Description("Top px.")] double top,
        [Description("Layer (z-order).")] int layer = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        node!.SetAnchorCommand.Execute(new Anchor(left, top, layer));
        return Ok($"Position {nodeIndex} → ({left},{top},{layer}).");
    }

    [Description("Resizes a node.")]
    private string ResizeNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Width px.")] double width,
        [Description("Height px.")] double height)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        node!.SetSizeCommand.Execute(new Size(width, height));
        return Ok($"Resized {nodeIndex} → ({width},{height}).");
    }

    [Description("Deletes a node. Cascade: auto-deletes all child slots and their connections — no need to delete them first.")]
    private string DeleteNode(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        node!.DeleteCommand.Execute(null);
        return Ok($"Node {nodeIndex} deleted.");
    }

    [Description("Deletes a slot and its connections.")]
    private string DeleteSlot(
        [Description("Node index.")] int nodeIndex,
        [Description("Slot index within the node.")] int slotIndex)
    {
        if (!TryGetSlot(nodeIndex, slotIndex, out var slot, out var error)) return error;
        slot!.DeleteCommand.Execute(null);
        return Ok($"Slot [{nodeIndex}][{slotIndex}] deleted.");
    }

    [Description("Connects two slots by node/slot indices. Uses Tree.SendConnectionCommand + ReceiveConnectionCommand. The framework may silently reject: check 'connected' in the response. Rejection reasons: channel incompatibility, same-node connection, or developer ValidateConnection rule.")]
    private string ConnectSlots(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot index.")] int senderSlotIndex,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot index.")] int receiverSlotIndex)
    {
        if (!TryGetSlot(senderNodeIndex, senderSlotIndex, out var senderSlot, out var error)) return error;
        if (!TryGetSlot(receiverNodeIndex, receiverSlotIndex, out var receiverSlot, out error)) return error;

        Tree.SendConnectionCommand.Execute(senderSlot!);
        Tree.ReceiveConnectionCommand.Execute(receiverSlot!);

        bool connected = VerifyConnection(senderSlot!, receiverSlot!);
        if (!connected)
            return ConnectionRejected(senderSlot!, receiverSlot!,
                $"[{senderNodeIndex}][{senderSlotIndex}]", $"[{receiverNodeIndex}][{receiverSlotIndex}]");
        return Ok($"Connected [{senderNodeIndex}][{senderSlotIndex}]→[{receiverNodeIndex}][{receiverSlotIndex}].");
    }

    [Description("Connects two slots by their runtime IDs. Stable across add/remove. The framework may silently reject: check 'connected' in the response.")]
    private string ConnectSlotsById(
        [Description("Runtime ID of the sender slot.")] string senderSlotId,
        [Description("Runtime ID of the receiver slot.")] string receiverSlotId)
    {
        if (FindComponentById(senderSlotId) is not IWorkflowSlotViewModel sender) return Error($"Sender slot '{senderSlotId}' not found.");
        if (FindComponentById(receiverSlotId) is not IWorkflowSlotViewModel receiver) return Error($"Receiver slot '{receiverSlotId}' not found.");

        Tree.SendConnectionCommand.Execute(sender);
        Tree.ReceiveConnectionCommand.Execute(receiver);

        bool connected = VerifyConnection(sender, receiver);
        if (!connected)
            return ConnectionRejected(sender, receiver, senderSlotId, receiverSlotId);
        return Ok($"Connected {senderSlotId}→{receiverSlotId}.");
    }

    [Description("Removes a connection between two slots by node/slot indices.")]
    private string DisconnectSlots(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot index.")] int senderSlotIndex,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot index.")] int receiverSlotIndex)
    {
        if (!TryGetSlot(senderNodeIndex, senderSlotIndex, out var senderSlot, out var error)) return error;
        if (!TryGetSlot(receiverNodeIndex, receiverSlotIndex, out var receiverSlot, out error)) return error;

        if (Tree.LinksMap.TryGetValue(senderSlot!, out var dic) && dic.TryGetValue(receiverSlot!, out var link))
        {
            link.DeleteCommand.Execute(null);
            return Ok($"Disconnected [{senderNodeIndex}][{senderSlotIndex}]✕[{receiverNodeIndex}][{receiverSlotIndex}].");
        }
        return Error("No connection found between the specified slots.");
    }

    [Description("Executes WorkCommand on a node.")]
    private string ExecuteWork(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter.")] string? parameter = null)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        node!.WorkCommand.Execute(parameter);
        return Ok($"Work executed on node {nodeIndex}.");
    }

    [Description("Executes BroadcastCommand on a node to forward data along connections.")]
    private string BroadcastNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter.")] string? parameter = null)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        node!.BroadcastCommand.Execute(parameter);
        return Ok($"Broadcast executed on node {nodeIndex}.");
    }

    [Description("Undoes the last action.")]
    private string Undo()
    {
        Tree.UndoCommand.Execute(null);
        return Ok("Undo.");
    }

    [Description("Redoes the last undone action.")]
    private string Redo()
    {
        Tree.RedoCommand.Execute(null);
        return Ok("Redo.");
    }

    // ────────────────────────── Introspection Functions ──────────────────────────

    [Description("Gets JSON schema of a .NET type by full name. Returns properties, types, defaults.")]
    private string GetTypeSchema(
        [Description("Fully-qualified type name.")] string fullTypeName)
    {
        var type = TypeIntrospector.ResolveType(fullTypeName);
        if (type == null)
            return Error($"Type '{fullTypeName}' not found.");

        return TypeIntrospector.GetTypeSchema(type);
    }

    [Description("Patches custom properties on a node. Rejects: command-backed props (Anchor,Size), framework-managed props (Parent,Slots,RuntimeId,Helper), and source-gen slot props (InputSlot,OutputSlot etc). Use dedicated tools for those.")]
    private string PatchNodeProperties(
        [Description("Node index.")] int nodeIndex,
        [Description("JSON patch object, e.g. '{\"Title\":\"New\"}'.")] string jsonPatch)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var result = ComponentPatcher.ApplyPatch(node!, jsonPatch);
        NudgeIfEnumSlotNode(node!);
        return result;
    }

    [Description("Patches custom properties on any component by runtime ID. Same rejection rules as PatchNodeProperties.")]
    private string PatchComponentById(
        [Description("Runtime ID of the component.")] string runtimeId,
        [Description("JSON patch object.")] string jsonPatch)
    {
        var component = FindComponentById(runtimeId);
        if (component == null) return Error($"Component '{runtimeId}' not found.");
        var result = ComponentPatcher.ApplyPatch(component, jsonPatch);
        if (component is IWorkflowNodeViewModel patchedNode)
            NudgeIfEnumSlotNode(patchedNode);
        return result;
    }

    // ────────────────────────── Progressive Context Functions ──────────────────────────

    [Description("High-level summary: node/link counts, distinct types, tree ID. Call first to orient.")]
    private string GetWorkflowSummary()
    {
        var nodeTypes = Tree.Nodes.Select(n => n.GetType().Name).Distinct().ToArray();
        var obj = new JObject
        {
            ["treeId"] = GetComponentId(Tree),
            ["treeType"] = Tree.GetType().Name,
            ["nodeCount"] = Tree.Nodes.Count,
            ["linkCount"] = Tree.Links.Count(l => l.IsVisible),
            ["nodeTypes"] = new JArray(nodeTypes),
        };
        return obj.ToString(Formatting.None);
    }

    [Description("Gets AgentContext docs for a .NET type. Use to learn about properties/commands on demand.")]
    private string GetComponentContext(
        [Description("Fully-qualified type name.")] string fullTypeName,
        [Description("'English' or 'Chinese'.")] string language = "English")
    {
        var lang = language.Contains("Chinese") || language.Contains("chinese")
            ? AgentLanguages.Chinese
            : AgentLanguages.English;

        var type = TypeIntrospector.ResolveType(fullTypeName);
        if (type == null)
            return Error($"Type '{fullTypeName}' not found.");

        if (type.IsEnum) return AgentContextCollector.GetEnumContext(type, lang);
        if (type.IsInterface) return AgentContextCollector.GetInterfaceContext(type, lang);
        return AgentContextCollector.GetClassContext(type, lang);
    }

    [Description("Lists commands on a node: name and parameter type.")]
    private string ListComponentCommands(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;

        var cmds = CommandInvoker.DiscoverCommands(node!);
        var arr = new JArray();
        foreach (var cmd in cmds)
        {
            arr.Add(new JObject
            {
                ["n"] = cmd.Name,
                ["p"] = cmd.ParameterType?.Name,
            });
        }
        return arr.ToString(Formatting.None);
    }

    // ────────────────────────── State Tracking / Diff Functions ──────────────────────────

    [Description("Takes a state snapshot. Returns version number + summary counts only. Use GetChangesSinceSnapshot for diffs.")]
    private string TakeSnapshot()
    {
        _tracker.TakeSnapshot();
        return JsonConvert.SerializeObject(new
        {
            status = "ok",
            version = _tracker.Version,
            nodeCount = Tree.Nodes.Count,
            linkCount = Tree.Links.Count(l => l.IsVisible),
        }, Formatting.None);
    }

    [Description("Returns diff since last snapshot: added/removed/modified nodes and links only.")]
    private string GetChangesSinceSnapshot()
    {
        return _tracker.GetChangesSinceLastSnapshot();
    }

    // ────────────────────────── Generic Command Execution ──────────────────────────

    [Description("Executes any command on a node by index. Use ListComponentCommands to discover available commands.")]
    private string ExecuteCommandOnNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Command name, e.g. 'WorkCommand'. 'Command' suffix optional.")] string commandName,
        [Description("JSON parameter, or null.")] string? jsonParameter = null)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var result = CommandInvoker.Invoke(node!, commandName, jsonParameter);
        NudgeIfEnumSlotNode(node!);
        return result;
    }

    [Description("Executes any command on a component by runtime ID. Works for nodes, slots, links.")]
    private string ExecuteCommandById(
        [Description("Runtime ID.")] string runtimeId,
        [Description("Command name.")] string commandName,
        [Description("JSON parameter, or null.")] string? jsonParameter = null)
    {
        var component = FindComponentById(runtimeId);
        if (component == null)
            return Error($"Component '{runtimeId}' not found.");
        var result = CommandInvoker.Invoke(component, commandName, jsonParameter);
        if (component is IWorkflowNodeViewModel cmdNode)
            NudgeIfEnumSlotNode(cmdNode);
        return result;
    }

    [Description("Creates a node and adds it to the tree via CreateNodeCommand. This is the ONLY correct way to add nodes — NEVER add nodes by directly modifying the Nodes collection. IMPORTANT: Nodes must NEVER have Size(0,0). Always provide width/height, or use GetComponentContext first to discover the type's documented default size. If you cannot determine the default, use width=300 height=260 as a safe fallback. The tool automatically offsets the position if it overlaps an existing node.")]
    private string CreateNode(
        [Description("Fully-qualified type name.")] string fullTypeName,
        [Description("Left px. Consider existing node positions to avoid overlap.")] double left = 0,
        [Description("Top px. Consider existing node positions to avoid overlap.")] double top = 0,
        [Description("Width px. Must be > 0. Use GetComponentContext to discover defaults.")] double width = 0,
        [Description("Height px. Must be > 0. Use GetComponentContext to discover defaults.")] double height = 0)
    {
        var type = TypeIntrospector.ResolveType(fullTypeName);
        if (type == null)
            return Error($"Type '{fullTypeName}' not found.");
        if (!typeof(IWorkflowNodeViewModel).IsAssignableFrom(type))
            return Error($"'{fullTypeName}' does not implement IWorkflowNodeViewModel.");

        // Resolve non-zero size: prefer caller value > AgentContext default > random fallback
        if (width <= 0 || height <= 0)
        {
            var resolved = ResolveDefaultSize(type);
            if (width <= 0) width = resolved.Width;
            if (height <= 0) height = resolved.Height;
        }

        // Auto-offset to avoid overlapping existing nodes using spatial query
        const double padding = 30;
        bool moved = false;
        try
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                // Query only nodes that intersect with the candidate region (padded)
                var queryViewport = new Viewport(
                    left - padding, top - padding,
                    width + padding * 2, height + padding * 2);
                var nearby = Tree.QueryNodes(queryViewport);

                bool overlap = false;
                foreach (var existing in nearby)
                {
                    double ex = existing.Anchor.Horizontal;
                    double ew = existing.Size.Width;

                    // Shift right of the overlapping node
                    left = ex + ew + padding;
                    moved = true;
                    overlap = true;
                    break;
                }
                if (!overlap) break;
            }
        }
        catch
        {
            // Spatial map not enabled — fall back to linear scan
            for (int attempt = 0; attempt < 100; attempt++)
            {
                bool overlap = false;
                foreach (var existing in Tree.Nodes)
                {
                    double ex = existing.Anchor.Horizontal;
                    double ey = existing.Anchor.Vertical;
                    double ew = existing.Size.Width;
                    double eh = existing.Size.Height;

                    if (left < ex + ew + padding && left + width + padding > ex &&
                        top < ey + eh + padding && top + height + padding > ey)
                    {
                        overlap = true;
                        left = ex + ew + padding;
                        moved = true;
                        break;
                    }
                }
                if (!overlap) break;
            }
        }

        try
        {
            var node = (IWorkflowNodeViewModel)Activator.CreateInstance(type);
            node.Anchor = new Anchor(left, top, 0);
            Tree.CreateNodeCommand.Execute(node);
            node.SetSizeCommand.Execute(new Size(width, height));
            var result = new JObject
            {
                ["status"] = "ok",
                ["id"] = GetComponentId(node),
                ["i"] = IndexOfNode(node),
                ["x"] = left,
                ["y"] = top,
                ["w"] = width,
                ["h"] = height,
            };
            if (moved)
                result["repositioned"] = true;
            return result.ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            return Error($"Failed to create node: {ex.Message}");
        }
    }

    private static Size ResolveDefaultSize(Type nodeType)
    {
        // Try to extract size from AgentContext attributes (any language)
        foreach (var lang in new[] { AgentLanguages.English, AgentLanguages.Chinese })
        {
            var contexts = AgentContextReader.GetContexts(nodeType, lang);
            foreach (var ctx in contexts)
            {
                // Look for patterns like "300×260", "300*260", "300x260"
                var match = System.Text.RegularExpressions.Regex.Match(ctx, @"(\d{2,4})\s*[×xX\*]\s*(\d{2,4})");
                if (match.Success
                    && double.TryParse(match.Groups[1].Value, out var w) && w > 0
                    && double.TryParse(match.Groups[2].Value, out var h) && h > 0)
                {
                    return new Size(w, h);
                }
            }
        }

        // Random fallback — never return 0
        var rng = new Random();
        return new Size(rng.Next(200, 400), rng.Next(150, 300));
    }

    [Description("Creates a dynamic slot on a node via CreateSlotCommand. Only use when the node does NOT already define typed slot properties (e.g. InputSlot/OutputSlot) — those are auto-created by source generator.")]
    private string CreateSlotOnNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Fully-qualified slot type name.")] string fullSlotTypeName,
        [Description("Channel: 'OneSender','OneReceiver','OneBoth','ManySender','ManyReceiver','ManyBoth'.")] string channel = "OneBoth")
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;

        var type = TypeIntrospector.ResolveType(fullSlotTypeName);
        if (type == null)
            return Error($"Type '{fullSlotTypeName}' not found.");
        if (!typeof(IWorkflowSlotViewModel).IsAssignableFrom(type))
            return Error($"'{fullSlotTypeName}' does not implement IWorkflowSlotViewModel.");

        try
        {
            var slot = (IWorkflowSlotViewModel)Activator.CreateInstance(type);
            if (Enum.TryParse<SlotChannel>(channel, true, out var ch))
                slot.Channel = ch;
            node!.CreateSlotCommand.Execute(slot);
            return JsonConvert.SerializeObject(new
            {
                status = "ok",
                id = GetComponentId(slot),
                si = node.Slots.IndexOf(slot),
            }, Formatting.None);
        }
        catch (Exception ex)
        {
            return Error($"Failed to create slot: {ex.Message}");
        }
    }

    // ────────────────────────── Slot Collection Functions ──────────────────────────

    [Description("Lists slot properties on a node type: named single slots and slot collection properties. Shows property name, whether it's a collection, current count, and slot IDs.")]
    private string ListSlotProperties(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var result = new JArray();
        var type = node!.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(prop.PropertyType))
            {
                var slot = prop.GetValue(node) as IWorkflowSlotViewModel;
                result.Add(new JObject
                {
                    ["name"] = prop.Name,
                    ["collection"] = false,
                    ["id"] = slot != null ? GetComponentId(slot) : null,
                    ["ch"] = slot?.Channel.ToString(),
                });
            }
            else if (IsSlotCollection(prop.PropertyType, out _))
            {
                var col = prop.GetValue(node) as IList;
                var ids = new JArray();
                if (col != null)
                {
                    foreach (var item in col)
                    {
                        if (item is IWorkflowSlotViewModel s)
                            ids.Add(GetComponentId(s));
                    }
                }
                var entry = new JObject
                {
                    ["name"] = prop.Name,
                    ["collection"] = true,
                    ["count"] = col?.Count ?? 0,
                    ["ids"] = ids,
                };

                // Expose EnumSlotCollection metadata if present
                var enumAttr = prop.GetCustomAttribute<EnumSlotCollectionAttribute>();
                if (enumAttr != null)
                {
                    entry["enumDriven"] = true;
                    // Discover current enum type via [EnumSlotValue] attribute binding
                    var enumTypeProp = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(p => p.GetCustomAttribute<SlotsEnumTypeAttribute>()?.CollectionPropertyName == prop.Name);
                    if (enumTypeProp != null)
                    {
                        var currentEnumType = enumTypeProp.GetValue(node) as Type;
                        entry["currentEnumType"] = currentEnumType?.FullName;
                        if (currentEnumType != null && currentEnumType.IsEnum)
                            entry["enumValues"] = new JArray(Enum.GetNames(currentEnumType));
                        var attr = enumTypeProp.GetCustomAttribute<SlotsEnumTypeAttribute>()!;
                        var allowedNames = GetAllowedEnumTypeDisplayNames(attr);
                        if (!string.IsNullOrEmpty(allowedNames))
                            entry["allowedEnumTypes"] = new JArray(allowedNames.Split([", "], StringSplitOptions.RemoveEmptyEntries));
                    }
                    entry["hint"] = "Use SetEnumSlotCollection to set or change the enum type. Do NOT add/remove slots manually.";
                }

                result.Add(entry);
            }
        }
        return result.ToString(Formatting.None);
    }

    [Description("Adds a new slot to a collection property on a node (e.g. OutputSlots). The slot is created via the node's CreateWorkflowSlot infrastructure and added to the collection, triggering lifecycle events.")]
    private string AddSlotToCollection(
        [Description("Node index.")] int nodeIndex,
        [Description("Name of the slot collection property, e.g. 'OutputSlots'.")] string propertyName,
        [Description("Fully-qualified slot type name.")] string fullSlotTypeName,
        [Description("Channel: 'OneSender','OneReceiver','OneBoth','ManySender','ManyReceiver','ManyBoth'.")] string channel = "MultipleBoth")
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = node!.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return Error($"Property '{propertyName}' not found on {node.GetType().Name}.");
        if (!IsSlotCollection(prop.PropertyType, out _))
            return Error($"Property '{propertyName}' is not a slot collection.");

        if (prop.GetValue(node) is not IList col) return Error($"Collection '{propertyName}' is null.");

        var slotType = TypeIntrospector.ResolveType(fullSlotTypeName);
        if (slotType == null) return Error($"Type '{fullSlotTypeName}' not found.");
        if (!typeof(IWorkflowSlotViewModel).IsAssignableFrom(slotType))
            return Error($"'{fullSlotTypeName}' does not implement IWorkflowSlotViewModel.");

        try
        {
            // Use CreateWorkflowSlot<T> via reflection to leverage node's infrastructure
            var createMethod = node.GetType().GetMethod("CreateWorkflowSlot");
            IWorkflowSlotViewModel slot;
            if (createMethod != null)
            {
                var generic = createMethod.MakeGenericMethod(slotType);
                slot = (IWorkflowSlotViewModel)generic.Invoke(node, null)!;
            }
            else
            {
                slot = (IWorkflowSlotViewModel)Activator.CreateInstance(slotType);
            }
            if (Enum.TryParse<SlotChannel>(channel, true, out var ch))
                slot.Channel = ch;
            col.Add(slot);
            NudgeIfEnumSlotNode(node!);
            return JsonConvert.SerializeObject(new
            {
                status = "ok",
                id = GetComponentId(slot),
                count = col.Count,
            }, Formatting.None);
        }
        catch (Exception ex)
        {
            return Error($"Failed to add slot: {ex.Message}");
        }
    }

    [Description("Removes a slot from a collection property on a node by slot runtime ID. Triggers lifecycle events (OnWorkflowSlotRemoved).")]
    private string RemoveSlotFromCollection(
        [Description("Node index.")] int nodeIndex,
        [Description("Name of the slot collection property, e.g. 'OutputSlots'.")] string propertyName,
        [Description("Runtime ID of the slot to remove.")] string slotRuntimeId)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = node!.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return Error($"Property '{propertyName}' not found on {node.GetType().Name}.");
        if (!IsSlotCollection(prop.PropertyType, out _))
            return Error($"Property '{propertyName}' is not a slot collection.");

        if (prop.GetValue(node) is not IList col) return Error($"Collection '{propertyName}' is null.");

        for (int i = 0; i < col.Count; i++)
        {
            if (col[i] is IWorkflowSlotViewModel slot && GetComponentId(slot) == slotRuntimeId)
            {
                col.RemoveAt(i);
                NudgeIfEnumSlotNode(node!);
                return Ok($"Removed slot '{slotRuntimeId}' from '{propertyName}'. Count={col.Count}.");
            }
        }
        return Error($"Slot '{slotRuntimeId}' not found in '{propertyName}'.");
    }

    [Description("Sets or changes the enum type on an [EnumSlotCollection]-marked slot collection. Resolves the enum type by full name, clears all existing slots in the collection, and recreates one slot per enum value. Returns the new slot IDs and enum labels.")]
    private string SetEnumSlotCollection(
        [Description("Node index.")] int nodeIndex,
        [Description("Name of the slot collection property marked with [EnumSlotCollection], e.g. 'OutputSlots'.")] string propertyName,
        [Description("Fully-qualified enum type name, e.g. 'Demo.ViewModels.NetworkRequestMethod'.")] string fullEnumTypeName)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var type = node!.GetType();
        var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return Error($"Property '{propertyName}' not found on {type.Name}.");
        if (prop.GetCustomAttribute<EnumSlotCollectionAttribute>() == null)
            return Error($"Property '{propertyName}' is not marked with [EnumSlotCollection].");
        if (!IsSlotCollection(prop.PropertyType, out var slotItemType))
            return Error($"Property '{propertyName}' is not a slot collection.");

        var enumType = TypeIntrospector.ResolveType(fullEnumTypeName);
        if (enumType == null) return Error($"Type '{fullEnumTypeName}' not found.");
        if (!enumType.IsEnum) return Error($"'{fullEnumTypeName}' is not an enum type.");

        // Try to set an EnumType property if the node exposes one via [SlotsEnumType] binding
        var enumTypeProp = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<SlotsEnumTypeAttribute>()?.CollectionPropertyName == propertyName);
        if (enumTypeProp != null && enumTypeProp.CanWrite && enumTypeProp.PropertyType == typeof(Type))
        {
            // Validate against allowed enum types if specified
            var slotsAttr = enumTypeProp.GetCustomAttribute<SlotsEnumTypeAttribute>()!;
            if (!IsEnumTypeAllowed(slotsAttr, enumType))
            {
                var allowed = GetAllowedEnumTypeDisplayNames(slotsAttr);
                return Error($"Enum type '{fullEnumTypeName}' is not allowed for '{propertyName}'. Allowed types: {allowed}");
            }

            // Setting EnumType triggers the node's own RebuildOutputSlots logic
            enumTypeProp.SetValue(node, enumType);
        }
        else
        {
            // Fallback: manually clear and rebuild the collection
            if (prop.GetValue(node) is not IList col) return Error($"Collection '{propertyName}' is null.");

            while (col.Count > 0)
                col.RemoveAt(col.Count - 1);

            var createMethod = node.GetType().GetMethod("CreateWorkflowSlot");
            var enumValues = Enum.GetValues(enumType);
            foreach (var _ in enumValues)
            {
                IWorkflowSlotViewModel slot;
                if (createMethod != null)
                {
                    var generic = createMethod.MakeGenericMethod(slotItemType ?? typeof(IWorkflowSlotViewModel));
                    slot = (IWorkflowSlotViewModel)generic.Invoke(node, null)!;
                }
                else
                {
                    slot = (IWorkflowSlotViewModel)Activator.CreateInstance(slotItemType ?? typeof(IWorkflowSlotViewModel))!;
                }
                slot.Channel = SlotChannel.MultipleTargets;
                col.Add(slot);
            }
        }

        // Nudge to force UI to recalculate slot anchor positions.
        NudgeNode(node);

        // Build result with slot IDs and labels
        var names = Enum.GetNames(enumType);
        var ids = new JArray();
        if (prop.GetValue(node) is IList resultCol)
        {
            for (int i = 0; i < resultCol.Count; i++)
            {
                if (resultCol[i] is IWorkflowSlotViewModel s)
                {
                    ids.Add(new JObject
                    {
                        ["id"] = GetComponentId(s),
                        ["label"] = i < names.Length ? names[i] : "?",
                    });
                }
            }
        }
        return new JObject
        {
            ["ok"] = true,
            ["enumType"] = enumType.FullName,
            ["collection"] = propertyName,
            ["count"] = ids.Count,
            ["slots"] = ids,
        }.ToString(Formatting.None);
    }

    /// <summary>
    /// Builds a reverse map from slot instance → property name (e.g. "InputSlot", "OutputSlots[2]").
    /// </summary>
    private static Dictionary<IWorkflowSlotViewModel, string> BuildSlotPropertyMap(IWorkflowNodeViewModel node)
    {
        var map = new Dictionary<IWorkflowSlotViewModel, string>();
        foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            try
            {
                if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(prop.PropertyType))
                {
                    if (prop.GetValue(node) is IWorkflowSlotViewModel slot)
                        map[slot] = prop.Name;
                }
                else if (IsSlotCollection(prop.PropertyType, out _))
                {
                    if (prop.GetValue(node) is IList col)
                    {
                        for (int i = 0; i < col.Count; i++)
                        {
                            if (col[i] is IWorkflowSlotViewModel s)
                                map[s] = $"{prop.Name}[{i}]";
                        }
                    }
                }
            }
            catch { /* skip inaccessible */ }
        }
        return map;
    }

    private static bool IsSlotCollection(Type type, out Type? itemType)
    {
        itemType = null;
        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            if (args.Length == 1 && typeof(IWorkflowSlotViewModel).IsAssignableFrom(args[0])
                && typeof(IEnumerable).IsAssignableFrom(type))
            {
                itemType = args[0];
                return true;
            }
        }
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(ICollection<>))
            {
                var args = iface.GetGenericArguments();
                if (args.Length == 1 && typeof(IWorkflowSlotViewModel).IsAssignableFrom(args[0]))
                {
                    itemType = args[0];
                    return true;
                }
            }
        }
        return false;
    }

    // ────────────────────────── Clone ──────────────────────────

    [Description("Clones a set of nodes (by indices) with their internal connections to a new position. Returns mapping of old→new node IDs.")]
    private string CloneNodes(
        [Description("JSON array of node indices to clone, e.g. [0,1,2].")] string nodeIndicesJson,
        [Description("Horizontal offset px for cloned nodes.")] double offsetX = 200,
        [Description("Vertical offset px for cloned nodes.")] double offsetY = 0)
    {
        int[] indices;
        try
        {
            indices = [.. JArray.Parse(nodeIndicesJson).Select(t => t.Value<int>())];
        }
        catch (Exception ex)
        {
            return Error($"Invalid JSON array: {ex.Message}");
        }

        // Validate all indices first
        var sourceNodes = new List<IWorkflowNodeViewModel>();
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= Tree.Nodes.Count)
                return Error($"Node index {idx} out of range [0,{Tree.Nodes.Count}).");
            sourceNodes.Add(Tree.Nodes[idx]);
        }

        var sourceSet = new HashSet<IWorkflowNodeViewModel>(sourceNodes);
        var oldToNew = new Dictionary<string, IWorkflowNodeViewModel>();
        var slotMap = new Dictionary<string, IWorkflowSlotViewModel>(); // old slot id → new slot

        // Phase 1: Clone nodes with slots
        foreach (var src in sourceNodes)
        {
            var srcType = src.GetType();
            IWorkflowNodeViewModel clone;
            try
            {
                clone = (IWorkflowNodeViewModel)Activator.CreateInstance(srcType);
            }
            catch (Exception ex)
            {
                return Error($"Failed to instantiate {srcType.Name}: {ex.Message}");
            }

            clone.Anchor = new Anchor(
                src.Anchor.Horizontal + offsetX,
                src.Anchor.Vertical + offsetY,
                src.Anchor.Layer);

            // Copy scalar properties (non-command-backed)
            ComponentPatcher.CopyScalarProperties(src, clone);

            Tree.CreateNodeCommand.Execute(clone);
            oldToNew[GetComponentId(src)] = clone;

            // Clone slots
            for (int s = 0; s < src.Slots.Count; s++)
            {
                var srcSlot = src.Slots[s];
                var slotType = srcSlot.GetType();
                try
                {
                    var newSlot = (IWorkflowSlotViewModel)Activator.CreateInstance(slotType);
                    newSlot.Channel = srcSlot.Channel;
                    ComponentPatcher.CopyScalarProperties(srcSlot, newSlot);
                    clone.CreateSlotCommand.Execute(newSlot);
                    slotMap[GetComponentId(srcSlot)] = newSlot;
                }
                catch { /* skip non-clonable slots */ }
            }
        }

        // Phase 2: Re-establish internal connections (only between cloned nodes)
        int connCount = 0;
        foreach (var src in sourceNodes)
        {
            foreach (var srcSlot in src.Slots)
            {
                foreach (var target in srcSlot.Targets)
                {
                    if (target.Parent != null && sourceSet.Contains(target.Parent))
                    {
                        var srcSlotId = GetComponentId(srcSlot);
                        var tgtSlotId = GetComponentId(target);
                        if (slotMap.TryGetValue(srcSlotId, out var newSender) &&
                            slotMap.TryGetValue(tgtSlotId, out var newReceiver))
                        {
                            Tree.SendConnectionCommand.Execute(newSender);
                            Tree.ReceiveConnectionCommand.Execute(newReceiver);
                            if (VerifyConnection(newSender, newReceiver))
                                connCount++;
                        }
                    }
                }
            }
        }

        var mapping = new JObject();
        foreach (var kvp in oldToNew)
            mapping[kvp.Key] = GetComponentId(kvp.Value);

        return JsonConvert.SerializeObject(new
        {
            status = "ok",
            cloned = oldToNew.Count,
            connections = connCount,
            mapping,
        }, Formatting.None);
    }

    // ────────────────────────── Batch Execution ──────────────────────────

    [Description("Executes multiple operations in one call to reduce round-trips. Each op is a JSON object with 'tool' and 'args'. Returns results array.")]
    private string BatchExecute(
        [Description("JSON array of operations: [{\"tool\":\"ToolName\",\"args\":{...}},...]")] string operationsJson)
    {
        JArray ops;
        try
        {
            ops = JArray.Parse(operationsJson);
        }
        catch (Exception ex)
        {
            return Error($"Invalid JSON array: {ex.Message}");
        }

        var results = new JArray();
        var toolMap = BuildToolDispatchMap();

        foreach (var op in ops)
        {
            var toolName = op["tool"]?.ToString();
            var args = op["args"] as JObject ?? [];

            if (string.IsNullOrEmpty(toolName) || !toolMap.TryGetValue(toolName!, out var handler))
            {
                results.Add(new JObject { ["tool"] = toolName, ["status"] = "error", ["message"] = $"Unknown tool '{toolName}'." });
                continue;
            }

            try
            {
                var result = handler(args);
                results.Add(new JObject { ["tool"] = toolName, ["result"] = result });
            }
            catch (Exception ex)
            {
                results.Add(new JObject { ["tool"] = toolName, ["status"] = "error", ["message"] = ex.Message });
            }
        }

        return results.ToString(Formatting.None);
    }

    private Dictionary<string, Func<JObject, string>> BuildToolDispatchMap()
    {
        return new Dictionary<string, Func<JObject, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["MoveNode"] = a => MoveNode(a.Value<int>("nodeIndex"), a.Value<double>("offsetX"), a.Value<double>("offsetY")),
            ["SetNodePosition"] = a => SetNodePosition(a.Value<int>("nodeIndex"), a.Value<double>("left"), a.Value<double>("top"), a.Value<int?>("layer") ?? 0),
            ["ResizeNode"] = a => ResizeNode(a.Value<int>("nodeIndex"), a.Value<double>("width"), a.Value<double>("height")),
            ["DeleteNode"] = a => DeleteNode(a.Value<int>("nodeIndex")),
            ["DeleteSlot"] = a => DeleteSlot(a.Value<int>("nodeIndex"), a.Value<int>("slotIndex")),
            ["ConnectSlots"] = a => ConnectSlots(a.Value<int>("senderNodeIndex"), a.Value<int>("senderSlotIndex"), a.Value<int>("receiverNodeIndex"), a.Value<int>("receiverSlotIndex")),
            ["ConnectSlotsById"] = a => ConnectSlotsById(a.Value<string>("senderSlotId")!, a.Value<string>("receiverSlotId")!),
            ["DisconnectSlots"] = a => DisconnectSlots(a.Value<int>("senderNodeIndex"), a.Value<int>("senderSlotIndex"), a.Value<int>("receiverNodeIndex"), a.Value<int>("receiverSlotIndex")),
            ["ExecuteWork"] = a => ExecuteWork(a.Value<int>("nodeIndex"), a.Value<string>("parameter")),
            ["BroadcastNode"] = a => BroadcastNode(a.Value<int>("nodeIndex"), a.Value<string>("parameter")),
            ["Undo"] = _ => Undo(),
            ["Redo"] = _ => Redo(),
            ["PatchNodeProperties"] = a => PatchNodeProperties(a.Value<int>("nodeIndex"), a.Value<string>("jsonPatch")!),
            ["PatchComponentById"] = a => PatchComponentById(a.Value<string>("runtimeId")!, a.Value<string>("jsonPatch")!),
            ["ExecuteCommandOnNode"] = a => ExecuteCommandOnNode(a.Value<int>("nodeIndex"), a.Value<string>("commandName")!, a.Value<string>("jsonParameter")),
            ["ExecuteCommandById"] = a => ExecuteCommandById(a.Value<string>("runtimeId")!, a.Value<string>("commandName")!, a.Value<string>("jsonParameter")),
            ["CreateNode"] = a => CreateNode(a.Value<string>("fullTypeName")!, a.Value<double?>("left") ?? 0, a.Value<double?>("top") ?? 0, a.Value<double?>("width") ?? 0, a.Value<double?>("height") ?? 0),
            ["CreateSlotOnNode"] = a => CreateSlotOnNode(a.Value<int>("nodeIndex"), a.Value<string>("fullSlotTypeName")!, a.Value<string>("channel") ?? "OneBoth"),
            ["CloneNodes"] = a => CloneNodes(a.Value<string>("nodeIndicesJson")!, a.Value<double?>("offsetX") ?? 200, a.Value<double?>("offsetY") ?? 0),
            ["ListSlotProperties"] = a => ListSlotProperties(a.Value<int>("nodeIndex")),
            ["AddSlotToCollection"] = a => AddSlotToCollection(a.Value<int>("nodeIndex"), a.Value<string>("propertyName")!, a.Value<string>("fullSlotTypeName")!, a.Value<string>("channel") ?? "MultipleBoth"),
            ["RemoveSlotFromCollection"] = a => RemoveSlotFromCollection(a.Value<int>("nodeIndex"), a.Value<string>("propertyName")!, a.Value<string>("slotRuntimeId")!),
            ["SetEnumSlotCollection"] = a => SetEnumSlotCollection(a.Value<int>("nodeIndex"), a.Value<string>("propertyName")!, a.Value<string>("fullEnumTypeName")!),
            ["FindNodes"] = a => FindNodes(a.Value<string>("typeName") ?? "", a.Value<string>("propertyName"), a.Value<string>("propertyValue")),
            ["ResolveSlotId"] = a => ResolveSlotId(a.Value<int>("nodeIndex"), a.Value<string>("propertyName")!, a.Value<int?>("collectionIndex") ?? 0),
            ["ConnectByProperty"] = a => ConnectByProperty(a.Value<int>("senderNodeIndex"), a.Value<string>("senderProperty")!, a.Value<int>("receiverNodeIndex"), a.Value<string>("receiverProperty")!, a.Value<int?>("senderCollectionIndex") ?? 0, a.Value<int?>("receiverCollectionIndex") ?? 0),
            ["CreateAndConfigureNode"] = a => CreateAndConfigureNode(a.Value<string>("fullTypeName")!, a.Value<double?>("left") ?? 0, a.Value<double?>("top") ?? 0, a.Value<double?>("width") ?? 0, a.Value<double?>("height") ?? 0, a.Value<string>("jsonPatch"), a.Value<string>("enumSlotProperty"), a.Value<string>("enumTypeName")),
            ["DeleteNodes"] = a => DeleteNodes(a.Value<string>("nodeIndicesJson")!),
            ["ArrangeNodes"] = a => ArrangeNodes(a.Value<string>("arrangementsJson")!),
            ["GetFullTopology"] = _ => GetFullTopology(),
            ["SearchForward"] = a => SearchForward(a.Value<int>("nodeIndex"), a.Value<string>("typeName"), a.Value<int?>("maxDepth") ?? 0),
            ["SearchReverse"] = a => SearchReverse(a.Value<int>("nodeIndex"), a.Value<string>("typeName"), a.Value<int?>("maxDepth") ?? 0),
            ["SearchAllRelative"] = a => SearchAllRelative(a.Value<int>("nodeIndex"), a.Value<string>("typeName"), a.Value<int?>("maxDepth") ?? 0),
            ["IsConnected"] = a => IsConnected(a.Value<int>("sourceNodeIndex"), a.Value<int>("targetNodeIndex"), a.Value<string>("direction") ?? "forward"),
            ["FindPath"] = a => FindPath(a.Value<int>("sourceNodeIndex"), a.Value<int>("targetNodeIndex")),
            ["ReverseBroadcastNode"] = a => ReverseBroadcastNode(a.Value<int>("nodeIndex"), a.Value<string>("parameter")),
            ["DisconnectSlotsById"] = a => DisconnectSlotsById(a.Value<string>("senderSlotId")!, a.Value<string>("receiverSlotId")!),
            ["DisconnectAllFromSlot"] = a => DisconnectAllFromSlot(a.Value<int>("nodeIndex"), a.Value<int>("slotIndex")),
            ["DisconnectAllFromNode"] = a => DisconnectAllFromNode(a.Value<int>("nodeIndex")),
            ["ReplaceConnection"] = a => ReplaceConnection(a.Value<string>("oldSenderSlotId")!, a.Value<string>("oldReceiverSlotId")!, a.Value<string>("newSenderSlotId")!, a.Value<string>("newReceiverSlotId")!),
            ["SetSlotChannel"] = a => SetSlotChannel(a.Value<int>("nodeIndex"), a.Value<int>("slotIndex"), a.Value<string>("channel")!),
            ["GetLinkDetail"] = a => GetLinkDetail(a.Value<string>("linkId")!),
            ["ExecuteWorkOnNodes"] = a => ExecuteWorkOnNodes(a.Value<string>("nodeIndicesJson")!, a.Value<string>("parameter")),
            ["BulkPatchNodes"] = a => BulkPatchNodes(a.Value<string>("nodeIndicesJson")!, a.Value<string>("jsonPatch")!),
            ["AlignNodes"] = a => AlignNodes(a.Value<string>("nodeIndicesJson")!, a.Value<string>("alignment")!),
            ["DistributeNodes"] = a => DistributeNodes(a.Value<string>("nodeIndicesJson")!, a.Value<string>("axis")!),
            ["AutoLayout"] = a => AutoLayout(a.Value<double?>("startX") ?? 0, a.Value<double?>("startY") ?? 0, a.Value<double?>("gapX") ?? 80, a.Value<double?>("gapY") ?? 40, a.Value<string>("direction") ?? "horizontal"),
            ["GetNodeStatistics"] = a => GetNodeStatistics(a.Value<int>("nodeIndex")),
            ["ListCreatableTypes"] = _ => ListCreatableTypes(),
            ["ValidateWorkflow"] = _ => ValidateWorkflow(),
        };
    }

    // ────────────────────────── Graph Traversal Functions ──────────────────────────

    [Description("Searches downstream (forward) nodes from a starting node via BFS. Returns compact list of reachable nodes. Optionally filter by type name substring and limit depth.")]
    private string SearchForward(
        [Description("Starting node index.")] int nodeIndex,
        [Description("Optional type name substring filter (case-insensitive). null for all.")] string? typeName = null,
        [Description("Max BFS depth. 0 = unlimited.")] int maxDepth = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        Func<IWorkflowNodeViewModel, bool>? predicate = null;
        if (!string.IsNullOrEmpty(typeName))
            predicate = n => n.GetType().Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0;
        var found = node!.SearchForwardNodes(predicate, maxDepth);
        return BuildNodeListResult(found);
    }

    [Description("Searches upstream (reverse) nodes from a starting node via BFS. Returns compact list of reachable nodes.")]
    private string SearchReverse(
        [Description("Starting node index.")] int nodeIndex,
        [Description("Optional type name substring filter (case-insensitive). null for all.")] string? typeName = null,
        [Description("Max BFS depth. 0 = unlimited.")] int maxDepth = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        Func<IWorkflowNodeViewModel, bool>? predicate = null;
        if (!string.IsNullOrEmpty(typeName))
            predicate = n => n.GetType().Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0;
        var found = node!.SearchReverseNodes(predicate, maxDepth);
        return BuildNodeListResult(found);
    }

    [Description("Searches both upstream and downstream nodes from a starting node via BFS. Returns compact list of all reachable nodes in both directions.")]
    private string SearchAllRelative(
        [Description("Starting node index.")] int nodeIndex,
        [Description("Optional type name substring filter (case-insensitive). null for all.")] string? typeName = null,
        [Description("Max BFS depth. 0 = unlimited.")] int maxDepth = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        Func<IWorkflowNodeViewModel, bool>? predicate = null;
        if (!string.IsNullOrEmpty(typeName))
            predicate = n => n.GetType().Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0;
        var found = node!.SearchAllRelativeNodes(predicate, maxDepth);
        return BuildNodeListResult(found);
    }

    [Description("Checks if two nodes are connected (directly or transitively). Direction: 'forward' (source→target), 'reverse' (target→source), 'any' (either direction).")]
    private string IsConnected(
        [Description("Source node index.")] int sourceNodeIndex,
        [Description("Target node index.")] int targetNodeIndex,
        [Description("Direction: 'forward', 'reverse', or 'any'.")] string direction = "forward")
    {
        if (!TryGetNode(sourceNodeIndex, out var srcNode, out var error)) return error;
        if (!TryGetNode(targetNodeIndex, out var tgtNode, out error)) return error;
        var srcId = GetComponentId(srcNode!);
        var tgtId = GetComponentId(tgtNode!);

        bool connected = false;
        if (direction != "reverse")
        {
            connected = srcNode!.SearchForwardNodes(n => ReferenceEquals(n, tgtNode)).Any();
        }
        if (!connected && direction != "forward")
        {
            connected = srcNode!.SearchReverseNodes(n => ReferenceEquals(n, tgtNode)).Any();
        }

        return JsonConvert.SerializeObject(new { status = "ok", connected, direction }, Formatting.None);
    }

    [Description("Finds the shortest forward path between two nodes. Returns ordered list of node IDs/indices from source to target, or empty if no path exists.")]
    private string FindPath(
        [Description("Source node index.")] int sourceNodeIndex,
        [Description("Target node index.")] int targetNodeIndex)
    {
        if (!TryGetNode(sourceNodeIndex, out var srcNode, out var error)) return error;
        if (!TryGetNode(targetNodeIndex, out var tgtNode, out error)) return error;

        // BFS to find shortest path
        var visited = new Dictionary<IWorkflowNodeViewModel, IWorkflowNodeViewModel?>();
        var queue = new Queue<IWorkflowNodeViewModel>();
        visited[srcNode!] = null;
        queue.Enqueue(srcNode!);
        bool found = false;

        while (queue.Count > 0 && !found)
        {
            var current = queue.Dequeue();
            foreach (var slot in current.Slots)
            {
                foreach (var target in slot.Targets)
                {
                    if (target.Parent != null && !visited.ContainsKey(target.Parent))
                    {
                        visited[target.Parent] = current;
                        if (ReferenceEquals(target.Parent, tgtNode))
                        {
                            found = true;
                            break;
                        }
                        queue.Enqueue(target.Parent);
                    }
                }
                if (found) break;
            }
        }

        if (!found)
            return JsonConvert.SerializeObject(new { status = "ok", found = false, path = Array.Empty<object>() }, Formatting.None);

        // Reconstruct path
        var path = new List<object>();
        var step = tgtNode!;
        while (step != null)
        {
            path.Add(new { i = IndexOfNode(step), id = GetComponentId(step), t = step.GetType().Name });
            visited.TryGetValue(step, out step!);
        }
        path.Reverse();
        return JsonConvert.SerializeObject(new { status = "ok", found = true, length = path.Count, path }, Formatting.None);
    }

    private string BuildNodeListResult(IEnumerable<IWorkflowNodeViewModel> nodes)
    {
        var arr = new JArray();
        foreach (var n in nodes)
        {
            arr.Add(new JObject
            {
                ["i"] = IndexOfNode(n),
                ["id"] = GetComponentId(n),
                ["t"] = n.GetType().Name,
            });
        }
        return arr.ToString(Formatting.None);
    }

    // ────────────────────────── Reverse Broadcast ──────────────────────────

    [Description("Executes ReverseBroadcastCommand on a node to trigger WorkCommand on upstream (source) nodes.")]
    private string ReverseBroadcastNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter.")] string? parameter = null)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        node!.ReverseBroadcastCommand.Execute(parameter);
        return Ok($"Reverse broadcast executed on node {nodeIndex}.");
    }

    // ────────────────────────── Connection Management ──────────────────────────

    [Description("Removes a connection between two slots by their runtime IDs.")]
    private string DisconnectSlotsById(
        [Description("Runtime ID of the sender slot.")] string senderSlotId,
        [Description("Runtime ID of the receiver slot.")] string receiverSlotId)
    {
        if (FindComponentById(senderSlotId) is not IWorkflowSlotViewModel sender) return Error($"Sender slot '{senderSlotId}' not found.");
        if (FindComponentById(receiverSlotId) is not IWorkflowSlotViewModel receiver) return Error($"Receiver slot '{receiverSlotId}' not found.");

        if (Tree.LinksMap.TryGetValue(sender, out var dic) && dic.TryGetValue(receiver, out var link))
        {
            link.DeleteCommand.Execute(null);
            return Ok($"Disconnected {senderSlotId}→{receiverSlotId}.");
        }
        return Error("No connection found between the specified slots.");
    }

    [Description("Disconnects all connections from a specific slot (both as sender and receiver).")]
    private string DisconnectAllFromSlot(
        [Description("Node index.")] int nodeIndex,
        [Description("Slot index within the node.")] int slotIndex)
    {
        if (!TryGetSlot(nodeIndex, slotIndex, out var slot, out var error)) return error;
        int count = 0;

        // Delete links where this slot is sender
        if (Tree.LinksMap.TryGetValue(slot!, out var targets))
        {
            foreach (var link in targets.Values.ToArray())
            {
                link.DeleteCommand.Execute(null);
                count++;
            }
        }

        // Delete links where this slot is receiver
        foreach (var kvp in Tree.LinksMap)
        {
            if (kvp.Value.TryGetValue(slot!, out var link))
            {
                link.DeleteCommand.Execute(null);
                count++;
            }
        }

        return Ok($"Disconnected {count} link(s) from slot [{nodeIndex}][{slotIndex}].");
    }

    [Description("Disconnects ALL connections from ALL slots of a node (without deleting the node or its slots).")]
    private string DisconnectAllFromNode(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        int count = 0;
        var slotSet = new HashSet<IWorkflowSlotViewModel>(node!.Slots);

        foreach (var slot in node.Slots.ToArray())
        {
            if (Tree.LinksMap.TryGetValue(slot, out var targets))
            {
                foreach (var link in targets.Values.ToArray())
                {
                    link.DeleteCommand.Execute(null);
                    count++;
                }
            }
        }

        // Also remove links where node's slots are receivers
        foreach (var kvp in Tree.LinksMap.ToArray())
        {
            foreach (var innerKvp in kvp.Value.ToArray())
            {
                if (slotSet.Contains(innerKvp.Key))
                {
                    innerKvp.Value.DeleteCommand.Execute(null);
                    count++;
                }
            }
        }

        return Ok($"Disconnected {count} link(s) from node {nodeIndex}.");
    }

    [Description("Atomically replaces a connection: disconnects old sender→receiver and connects new sender→receiver. Useful for re-routing connections without losing the other endpoint.")]
    private string ReplaceConnection(
        [Description("Runtime ID of the old sender slot.")] string oldSenderSlotId,
        [Description("Runtime ID of the old receiver slot.")] string oldReceiverSlotId,
        [Description("Runtime ID of the new sender slot.")] string newSenderSlotId,
        [Description("Runtime ID of the new receiver slot.")] string newReceiverSlotId)
    {
        // Disconnect old
        if (FindComponentById(oldSenderSlotId) is not IWorkflowSlotViewModel oldSender) return Error($"Old sender slot '{oldSenderSlotId}' not found.");
        if (FindComponentById(oldReceiverSlotId) is not IWorkflowSlotViewModel oldReceiver) return Error($"Old receiver slot '{oldReceiverSlotId}' not found.");

        if (Tree.LinksMap.TryGetValue(oldSender, out var dic) && dic.TryGetValue(oldReceiver, out var link))
            link.DeleteCommand.Execute(null);
        else
            return Error("No existing connection found between old sender and receiver.");

        // Connect new
        if (FindComponentById(newSenderSlotId) is not IWorkflowSlotViewModel newSender) return Error($"New sender slot '{newSenderSlotId}' not found.");
        if (FindComponentById(newReceiverSlotId) is not IWorkflowSlotViewModel newReceiver) return Error($"New receiver slot '{newReceiverSlotId}' not found.");

        Tree.SendConnectionCommand.Execute(newSender);
        Tree.ReceiveConnectionCommand.Execute(newReceiver);

        bool connected = VerifyConnection(newSender, newReceiver);
        if (!connected)
            return Error($"Old connection removed but new connection {newSenderSlotId}→{newReceiverSlotId} was rejected by framework validation. The old connection is NOT restored.");
        return Ok($"Replaced {oldSenderSlotId}→{oldReceiverSlotId} with {newSenderSlotId}→{newReceiverSlotId}.");
    }

    // ────────────────────────── Slot Channel ──────────────────────────

    [Description("Changes the channel type of a slot. Channels: 'OneSender','OneReceiver','OneBoth','ManySender','ManyReceiver','ManyBoth','MultipleSenders','MultipleTargets','MultipleBoth'.")]
    private string SetSlotChannel(
        [Description("Node index.")] int nodeIndex,
        [Description("Slot index.")] int slotIndex,
        [Description("New channel type.")] string channel)
    {
        if (!TryGetSlot(nodeIndex, slotIndex, out var slot, out var error)) return error;
        if (!Enum.TryParse<SlotChannel>(channel, true, out var ch))
            return Error($"Invalid channel '{channel}'. Valid: {string.Join(", ", Enum.GetNames(typeof(SlotChannel)))}.");
        slot!.SetChannelCommand.Execute(ch);
        return Ok($"Slot [{nodeIndex}][{slotIndex}] channel → {ch}.");
    }

    // ────────────────────────── Link Inspection ──────────────────────────

    [Description("Gets full detail of a link by runtime ID: sender/receiver slots, parent nodes, properties.")]
    private string GetLinkDetail(
        [Description("Runtime ID of the link.")] string linkId)
    {
        if (FindComponentById(linkId) is not IWorkflowLinkViewModel component) return Error($"Link '{linkId}' not found.");

        var obj = new JObject
        {
            ["id"] = linkId,
            ["visible"] = component.IsVisible,
        };

        if (component.Sender != null)
        {
            obj["sender"] = new JObject
            {
                ["slotId"] = GetComponentId(component.Sender),
                ["nodeId"] = component.Sender.Parent != null ? GetComponentId(component.Sender.Parent) : null,
                ["nodeIndex"] = component.Sender.Parent != null ? IndexOfNode(component.Sender.Parent) : -1,
            };
        }
        if (component.Receiver != null)
        {
            obj["receiver"] = new JObject
            {
                ["slotId"] = GetComponentId(component.Receiver),
                ["nodeId"] = component.Receiver.Parent != null ? GetComponentId(component.Receiver.Parent) : null,
                ["nodeIndex"] = component.Receiver.Parent != null ? IndexOfNode(component.Receiver.Parent) : -1,
            };
        }

        AppendScalarProperties(obj, component);
        return obj.ToString(Formatting.None);
    }

    // ────────────────────────── Bulk Operations ──────────────────────────

    [Description("Executes WorkCommand on multiple nodes in one call. Optionally pass a parameter shared by all.")]
    private string ExecuteWorkOnNodes(
        [Description("JSON array of node indices, e.g. [0,1,2].")] string nodeIndicesJson,
        [Description("Optional parameter passed to each WorkCommand.")] string? parameter = null)
    {
        int[] indices;
        try { indices = [.. JArray.Parse(nodeIndicesJson).Select(t => t.Value<int>())]; }
        catch (Exception ex) { return Error($"Invalid JSON array: {ex.Message}"); }

        int executed = 0;
        var errors = new JArray();
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= Tree.Nodes.Count)
            {
                errors.Add($"Index {idx} out of range.");
                continue;
            }
            Tree.Nodes[idx].WorkCommand.Execute(parameter);
            executed++;
        }

        var result = new JObject { ["status"] = "ok", ["executed"] = executed };
        if (errors.Count > 0) result["errors"] = errors;
        return result.ToString(Formatting.None);
    }

    [Description("Patches the same properties on multiple nodes in one call. Applies the same JSON patch to each node.")]
    private string BulkPatchNodes(
        [Description("JSON array of node indices, e.g. [0,1,2].")] string nodeIndicesJson,
        [Description("JSON patch object, e.g. '{\"Title\":\"Updated\"}'.")] string jsonPatch)
    {
        int[] indices;
        try { indices = [.. JArray.Parse(nodeIndicesJson).Select(t => t.Value<int>())]; }
        catch (Exception ex) { return Error($"Invalid JSON array: {ex.Message}"); }

        int patched = 0;
        var errors = new JArray();
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= Tree.Nodes.Count)
            {
                errors.Add($"Index {idx} out of range.");
                continue;
            }
            var r = ComponentPatcher.ApplyPatch(Tree.Nodes[idx], jsonPatch);
            if (r.Contains("error"))
                errors.Add($"Node {idx}: {r}");
            else
                patched++;
        }

        var result = new JObject { ["status"] = "ok", ["patched"] = patched };
        if (errors.Count > 0) result["errors"] = errors;
        return result.ToString(Formatting.None);
    }

    // ────────────────────────── Layout Functions ──────────────────────────

    [Description("Aligns nodes to a common edge or center. Alignment: 'left','right','top','bottom','centerH','centerV'.")]
    private string AlignNodes(
        [Description("JSON array of node indices, e.g. [0,1,2].")] string nodeIndicesJson,
        [Description("Alignment: 'left','right','top','bottom','centerH','centerV'.")] string alignment)
    {
        int[] indices;
        try { indices = [.. JArray.Parse(nodeIndicesJson).Select(t => t.Value<int>())]; }
        catch (Exception ex) { return Error($"Invalid JSON array: {ex.Message}"); }

        var nodes = new List<IWorkflowNodeViewModel>();
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= Tree.Nodes.Count) return Error($"Index {idx} out of range.");
            nodes.Add(Tree.Nodes[idx]);
        }
        if (nodes.Count < 2) return Error("Need at least 2 nodes to align.");

        switch (alignment.ToLowerInvariant())
        {
            case "left":
                var minX = nodes.Min(n => n.Anchor.Horizontal);
                foreach (var n in nodes) n.SetAnchorCommand.Execute(new Anchor(minX, n.Anchor.Vertical, n.Anchor.Layer));
                break;
            case "right":
                var maxRight = nodes.Max(n => n.Anchor.Horizontal + n.Size.Width);
                foreach (var n in nodes) n.SetAnchorCommand.Execute(new Anchor(maxRight - n.Size.Width, n.Anchor.Vertical, n.Anchor.Layer));
                break;
            case "top":
                var minY = nodes.Min(n => n.Anchor.Vertical);
                foreach (var n in nodes) n.SetAnchorCommand.Execute(new Anchor(n.Anchor.Horizontal, minY, n.Anchor.Layer));
                break;
            case "bottom":
                var maxBottom = nodes.Max(n => n.Anchor.Vertical + n.Size.Height);
                foreach (var n in nodes) n.SetAnchorCommand.Execute(new Anchor(n.Anchor.Horizontal, maxBottom - n.Size.Height, n.Anchor.Layer));
                break;
            case "centerh":
                var avgX = nodes.Average(n => n.Anchor.Horizontal + n.Size.Width / 2);
                foreach (var n in nodes) n.SetAnchorCommand.Execute(new Anchor(avgX - n.Size.Width / 2, n.Anchor.Vertical, n.Anchor.Layer));
                break;
            case "centerv":
                var avgY = nodes.Average(n => n.Anchor.Vertical + n.Size.Height / 2);
                foreach (var n in nodes) n.SetAnchorCommand.Execute(new Anchor(n.Anchor.Horizontal, avgY - n.Size.Height / 2, n.Anchor.Layer));
                break;
            default:
                return Error($"Unknown alignment '{alignment}'. Valid: left, right, top, bottom, centerH, centerV.");
        }
        return Ok($"Aligned {nodes.Count} nodes by '{alignment}'.");
    }

    [Description("Evenly distributes nodes along an axis. Axis: 'horizontal' or 'vertical'. Nodes are sorted by current position and spacing is equalized.")]
    private string DistributeNodes(
        [Description("JSON array of node indices, e.g. [0,1,2].")] string nodeIndicesJson,
        [Description("Axis: 'horizontal' or 'vertical'.")] string axis)
    {
        int[] indices;
        try { indices = [.. JArray.Parse(nodeIndicesJson).Select(t => t.Value<int>())]; }
        catch (Exception ex) { return Error($"Invalid JSON array: {ex.Message}"); }

        var nodes = new List<IWorkflowNodeViewModel>();
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= Tree.Nodes.Count) return Error($"Index {idx} out of range.");
            nodes.Add(Tree.Nodes[idx]);
        }
        if (nodes.Count < 3) return Error("Need at least 3 nodes to distribute.");

        if (axis.Equals("horizontal", StringComparison.OrdinalIgnoreCase))
        {
            nodes.Sort((a, b) => a.Anchor.Horizontal.CompareTo(b.Anchor.Horizontal));
            var first = nodes[0].Anchor.Horizontal;
            var last = nodes[nodes.Count - 1].Anchor.Horizontal;
            var step = (last - first) / (nodes.Count - 1);
            for (int i = 1; i < nodes.Count - 1; i++)
            {
                var n = nodes[i];
                n.SetAnchorCommand.Execute(new Anchor(first + step * i, n.Anchor.Vertical, n.Anchor.Layer));
            }
        }
        else if (axis.Equals("vertical", StringComparison.OrdinalIgnoreCase))
        {
            nodes.Sort((a, b) => a.Anchor.Vertical.CompareTo(b.Anchor.Vertical));
            var first = nodes[0].Anchor.Vertical;
            var last = nodes[^1].Anchor.Vertical;
            var step = (last - first) / (nodes.Count - 1);
            for (int i = 1; i < nodes.Count - 1; i++)
            {
                var n = nodes[i];
                n.SetAnchorCommand.Execute(new Anchor(n.Anchor.Horizontal, first + step * i, n.Anchor.Layer));
            }
        }
        else
        {
            return Error($"Unknown axis '{axis}'. Valid: horizontal, vertical.");
        }
        return Ok($"Distributed {nodes.Count} nodes along '{axis}'.");
    }

    [Description("Auto-layouts all nodes using topology-aware layered layout (Sugiyama-style). Coordinate system: origin (0,0) = top-left, X+ = rightward, Y+ = downward. Nodes are arranged in layers following the propagation chain from source nodes (in-degree=0). Within each layer, nodes are ordered to minimize edge crossings. Node sizes are respected to avoid overlap. Disconnected subgraphs are laid out separately. Direction: left-to-right (horizontal) or top-to-bottom (vertical).")]
    private string AutoLayout(
        [Description("Start X position.")] double startX = 0,
        [Description("Start Y position.")] double startY = 0,
        [Description("Horizontal gap between layers (horizontal) or nodes within a layer (vertical).")] double gapX = 80,
        [Description("Vertical gap between nodes within a layer (horizontal) or between layers (vertical).")] double gapY = 40,
        [Description("Direction: 'horizontal' (left-to-right) or 'vertical' (top-to-bottom).")] string direction = "horizontal")
    {
        var nodes = Tree.Nodes;
        if (nodes.Count == 0) return Ok("No nodes to layout.");

        bool horizontal = !direction.Equals("vertical", StringComparison.OrdinalIgnoreCase);

        // Build adjacency: node → set of downstream nodes (via slot.Targets)
        var forward = new Dictionary<IWorkflowNodeViewModel, HashSet<IWorkflowNodeViewModel>>();
        var backward = new Dictionary<IWorkflowNodeViewModel, HashSet<IWorkflowNodeViewModel>>();
        var allNodes = new HashSet<IWorkflowNodeViewModel>();

        foreach (var node in nodes)
        {
            allNodes.Add(node);
            if (!forward.ContainsKey(node)) forward[node] = [];
            if (!backward.ContainsKey(node)) backward[node] = [];
        }

        foreach (var node in nodes)
        {
            foreach (var slot in node.Slots)
            {
                foreach (var target in slot.Targets)
                {
                    if (target.Parent != null && allNodes.Contains(target.Parent) && target.Parent != node)
                    {
                        forward[node].Add(target.Parent);
                        if (!backward.ContainsKey(target.Parent))
                            backward[target.Parent] = [];
                        backward[target.Parent].Add(node);
                    }
                }
            }
        }

        // Find connected components via BFS
        var visited = new HashSet<IWorkflowNodeViewModel>();
        var components = new List<List<IWorkflowNodeViewModel>>();
        foreach (var node in nodes)
        {
            if (visited.Contains(node)) continue;
            var component = new List<IWorkflowNodeViewModel>();
            var queue = new Queue<IWorkflowNodeViewModel>();
            queue.Enqueue(node);
            visited.Add(node);
            while (queue.Count > 0)
            {
                var curr = queue.Dequeue();
                component.Add(curr);
                foreach (var next in forward[curr])
                {
                    if (visited.Add(next)) queue.Enqueue(next);
                }
                if (backward.ContainsKey(curr))
                {
                    foreach (var prev in backward[curr])
                    {
                        if (visited.Add(prev)) queue.Enqueue(prev);
                    }
                }
            }
            components.Add(component);
        }

        // For each component: assign layers via longest-path from sources
        // Then position layers
        double globalOffsetX = startX;
        double globalOffsetY = startY;
        int totalMoved = 0;

        foreach (var component in components)
        {
            var compSet = new HashSet<IWorkflowNodeViewModel>(component);

            // Find source nodes (in-degree = 0 within this component)
            var sources = new List<IWorkflowNodeViewModel>();
            foreach (var n in component)
            {
                bool hasIncoming = false;
                if (backward.ContainsKey(n))
                {
                    foreach (var prev in backward[n])
                    {
                        if (compSet.Contains(prev)) { hasIncoming = true; break; }
                    }
                }
                if (!hasIncoming) sources.Add(n);
            }
            // If cyclic (no source), pick the node with lowest in-degree
            if (sources.Count == 0)
            {
                sources.Add(component.OrderBy(n => backward.ContainsKey(n) ? backward[n].Count(compSet.Contains) : 0).First());
            }

            // Assign layers via BFS longest-path from sources
            var layerOf = new Dictionary<IWorkflowNodeViewModel, int>();
            foreach (var n in component) layerOf[n] = 0;

            // Topological relaxation: repeat until stable
            bool changed = true;
            int iterations = 0;
            while (changed && iterations < component.Count + 1)
            {
                changed = false;
                iterations++;
                foreach (var n in component)
                {
                    foreach (var next in forward[n])
                    {
                        if (compSet.Contains(next) && layerOf[next] <= layerOf[n])
                        {
                            layerOf[next] = layerOf[n] + 1;
                            changed = true;
                        }
                    }
                }
            }

            // Group by layer
            var layers = new SortedDictionary<int, List<IWorkflowNodeViewModel>>();
            foreach (var n in component)
            {
                var l = layerOf[n];
                if (!layers.ContainsKey(l)) layers[l] = [];
                layers[l].Add(n);
            }

            // Order within each layer: barycenter heuristic (average position of connected nodes in previous layer)
            List<IWorkflowNodeViewModel>? prevLayer = null;
            foreach (var kvp in layers)
            {
                if (prevLayer != null && prevLayer.Count > 0)
                {
                    var prevPositions = new Dictionary<IWorkflowNodeViewModel, int>();
                    for (int i = 0; i < prevLayer.Count; i++)
                        prevPositions[prevLayer[i]] = i;

                    kvp.Value.Sort((a, b) =>
                    {
                        double baryA = GetBarycenter(a, backward, prevPositions);
                        double baryB = GetBarycenter(b, backward, prevPositions);
                        return baryA.CompareTo(baryB);
                    });
                }
                prevLayer = kvp.Value;
            }

            // Compute positions respecting node sizes
            double layerPos = horizontal ? globalOffsetX : globalOffsetY; // advancing axis
            double maxCrossExtent = 0; // track max extent in cross axis for component offset

            foreach (var kvp in layers)
            {
                var layerNodes = kvp.Value;
                double crossPos = horizontal ? globalOffsetY : globalOffsetX; // cross axis
                double maxLayerExtent = 0; // max width (horizontal) or height (vertical) in this layer

                foreach (var n in layerNodes)
                {
                    double nx = horizontal ? layerPos : crossPos;
                    double ny = horizontal ? crossPos : layerPos;
                    n.SetAnchorCommand.Execute(new Anchor(nx, ny, n.Anchor.Layer));
                    totalMoved++;

                    double nodeMain = horizontal ? n.Size.Width : n.Size.Height;
                    double nodeCross = horizontal ? n.Size.Height : n.Size.Width;

                    if (nodeMain > maxLayerExtent) maxLayerExtent = nodeMain;
                    crossPos += nodeCross + gapY;
                }

                double totalCross = crossPos - (horizontal ? globalOffsetY : globalOffsetX) - gapY;
                if (totalCross > maxCrossExtent) maxCrossExtent = totalCross;

                layerPos += maxLayerExtent + gapX;
            }

            // Offset next component below/right of this one
            if (horizontal)
                globalOffsetY += maxCrossExtent + gapY * 2;
            else
                globalOffsetX += maxCrossExtent + gapX * 2;
        }

        return Ok($"Auto-layout: {totalMoved} nodes arranged in {components.Count} subgraph(s), direction={direction}.");
    }

    private static double GetBarycenter(
        IWorkflowNodeViewModel node,
        Dictionary<IWorkflowNodeViewModel, HashSet<IWorkflowNodeViewModel>> backward,
        Dictionary<IWorkflowNodeViewModel, int> prevPositions)
    {
        if (!backward.ContainsKey(node)) return 0;
        double sum = 0;
        int count = 0;
        foreach (var prev in backward[node])
        {
            if (prevPositions.TryGetValue(prev, out var pos))
            {
                sum += pos;
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    // ────────────────────────── Analytics Functions ──────────────────────────

    [Description("Gets statistics for a node: in-degree, out-degree, total connections, connected node IDs, slot utilization. Useful for understanding node importance and connectivity.")]
    private string GetNodeStatistics(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;

        int inDegree = 0;
        int outDegree = 0;
        var connectedNodeIds = new HashSet<string>();

        foreach (var slot in node!.Slots)
        {
            foreach (var target in slot.Targets)
            {
                outDegree++;
                if (target.Parent != null)
                    connectedNodeIds.Add(GetComponentId(target.Parent));
            }
            foreach (var source in slot.Sources)
            {
                inDegree++;
                if (source.Parent != null)
                    connectedNodeIds.Add(GetComponentId(source.Parent));
            }
        }

        return JsonConvert.SerializeObject(new
        {
            status = "ok",
            nodeIndex,
            id = GetComponentId(node),
            type = node.GetType().Name,
            inDegree,
            outDegree,
            totalConnections = inDegree + outDegree,
            connectedNodes = connectedNodeIds.Count,
            slotCount = node.Slots.Count,
            connectedNodeIds = connectedNodeIds.ToArray(),
        }, Formatting.None);
    }

    [Description("Lists all node and slot types that can be created. Scans assemblies for concrete types implementing IWorkflowNodeViewModel/IWorkflowSlotViewModel with parameterless constructors.")]
    private string ListCreatableTypes()
    {
        var nodeTypes = new JArray();
        var slotTypes = new JArray();

        // Scan assemblies of registered customer components + the tree's own assembly
        var assemblies = new HashSet<Assembly>
        {
            Tree.GetType().Assembly
        };
        foreach (var node in Tree.Nodes)
            assemblies.Add(node.GetType().Assembly);

        foreach (var asm in assemblies)
        {
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                    if (typeof(IWorkflowNodeViewModel).IsAssignableFrom(type))
                    {
                        nodeTypes.Add(new JObject
                        {
                            ["fullName"] = type.FullName,
                            ["name"] = type.Name,
                        });
                    }
                    else if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(type))
                    {
                        slotTypes.Add(new JObject
                        {
                            ["fullName"] = type.FullName,
                            ["name"] = type.Name,
                        });
                    }
                }
            }
            catch { /* skip assemblies that fail to enumerate */ }
        }

        return new JObject
        {
            ["nodeTypes"] = nodeTypes,
            ["slotTypes"] = slotTypes,
        }.ToString(Formatting.None);
    }

    [Description("Validates the workflow: checks for unconnected slots, nodes without connections, nodes with zero size, and other potential issues. Returns a list of warnings.")]
    private string ValidateWorkflow()
    {
        var warnings = new JArray();

        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            var node = Tree.Nodes[i];
            var nodeId = GetComponentId(node);

            // Check zero size
            if (node.Size.Width <= 0 || node.Size.Height <= 0)
                warnings.Add(new JObject { ["level"] = "error", ["node"] = i, ["id"] = nodeId, ["msg"] = $"Node has zero/negative size ({node.Size.Width}×{node.Size.Height})." });

            // Check isolated node (no connections at all)
            bool hasAnyConnection = false;
            foreach (var slot in node.Slots)
            {
                if (slot.Targets.Count > 0 || slot.Sources.Count > 0)
                {
                    hasAnyConnection = true;
                    break;
                }
            }
            if (!hasAnyConnection && node.Slots.Count > 0)
                warnings.Add(new JObject { ["level"] = "warn", ["node"] = i, ["id"] = nodeId, ["msg"] = "Node is isolated (has slots but no connections)." });

            // Check node with no slots
            if (node.Slots.Count == 0)
                warnings.Add(new JObject { ["level"] = "info", ["node"] = i, ["id"] = nodeId, ["msg"] = "Node has no slots." });
        }

        // Check for duplicate connections
        var seenLinks = new HashSet<string>();
        foreach (var link in Tree.Links)
        {
            if (!link.IsVisible) continue;
            var key = $"{GetComponentId(link.Sender)}→{GetComponentId(link.Receiver)}";
            if (!seenLinks.Add(key))
                warnings.Add(new JObject { ["level"] = "warn", ["id"] = GetComponentId(link), ["msg"] = $"Duplicate connection: {key}." });
        }

        return new JObject
        {
            ["status"] = "ok",
            ["nodeCount"] = Tree.Nodes.Count,
            ["linkCount"] = Tree.Links.Count(l => l.IsVisible),
            ["warningCount"] = warnings.Count,
            ["warnings"] = warnings,
        }.ToString(Formatting.None);
    }

    // ────────────────────────── Helpers ──────────────────────────

    private bool TryGetNode(int index, out IWorkflowNodeViewModel? node, out string error)
    {
        node = null;
        error = string.Empty;
        if (index < 0 || index >= Tree.Nodes.Count)
        {
            error = Error($"Node index {index} out of range [0,{Tree.Nodes.Count}).");
            return false;
        }
        node = Tree.Nodes[index];
        return true;
    }

    private bool TryGetSlot(int nodeIndex, int slotIndex, out IWorkflowSlotViewModel? slot, out string error)
    {
        slot = null;
        if (!TryGetNode(nodeIndex, out var node, out error) || node is null) return false;
        if (slotIndex < 0 || slotIndex >= node.Slots.Count)
        {
            error = Error($"Slot index {slotIndex} out of range [0,{node.Slots.Count}) on node {nodeIndex}.");
            return false;
        }
        slot = node.Slots[slotIndex];
        return true;
    }

    private int IndexOfNode(IWorkflowNodeViewModel node)
    {
        for (int i = 0; i < Tree.Nodes.Count; i++)
            if (ReferenceEquals(Tree.Nodes[i], node)) return i;
        return -1;
    }

    private (IWorkflowNodeViewModel? node, int index) FindNodeById(string runtimeId)
    {
        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            var node = Tree.Nodes[i];
            if (node is IWorkflowIdentifiable id && id.RuntimeId == runtimeId)
                return (node, i);
        }
        return (null, -1);
    }

    private object? FindComponentById(string runtimeId)
    {
        foreach (var node in Tree.Nodes)
        {
            if (node is IWorkflowIdentifiable nid && nid.RuntimeId == runtimeId)
                return node;
            foreach (var slot in node.Slots)
            {
                if (slot is IWorkflowIdentifiable sid && sid.RuntimeId == runtimeId)
                    return slot;
            }
        }
        foreach (var link in Tree.Links)
        {
            if (link is IWorkflowIdentifiable lid && lid.RuntimeId == runtimeId)
                return link;
        }
        if (Tree is IWorkflowIdentifiable tid && tid.RuntimeId == runtimeId)
            return Tree;
        return null;
    }

    private static string GetComponentId(object component)
    {
        if (component is IWorkflowIdentifiable identifiable)
            return identifiable.RuntimeId;
        return component.GetHashCode().ToString("x8");
    }

    private static void AppendScalarProperties(JObject obj, object target)
    {
        foreach (var prop in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            var pt = prop.PropertyType;
            if (pt == typeof(string) || pt == typeof(int) || pt == typeof(double) || pt == typeof(bool) ||
                pt == typeof(long) || pt == typeof(float) || pt == typeof(decimal))
            {
                try
                {
                    var val = prop.GetValue(target);
                    obj[prop.Name] = val != null ? JToken.FromObject(val) : JValue.CreateNull();
                }
                catch { /* skip inaccessible */ }
            }
            else if (pt == typeof(Type))
            {
                try
                {
                    var val = prop.GetValue(target) as Type;
                    obj[prop.Name] = val?.FullName;
                }
                catch { /* skip inaccessible */ }
            }
            else if (pt.IsEnum)
            {
                try
                {
                    var val = prop.GetValue(target);
                    obj[prop.Name] = val?.ToString();
                }
                catch { /* skip inaccessible */ }
            }
        }
    }

    /// <summary>
    /// Checks if a given enum type is allowed by the <see cref="SlotsEnumTypeAttribute"/>.
    /// Supports both <see cref="SlotsEnumTypeAttribute.AllowedEnumTypes"/> (Type[]) and
    /// <see cref="SlotsEnumTypeAttribute.AllowedEnumTypeNames"/> (string[]).
    /// Returns <c>true</c> if no constraints are specified (both arrays empty).
    /// </summary>
    private static bool IsEnumTypeAllowed(SlotsEnumTypeAttribute attr, Type enumType)
    {
        bool hasTypeConstraints = attr.AllowedEnumTypes.Length > 0;
        bool hasNameConstraints = attr.AllowedEnumTypeNames.Length > 0;

        if (!hasTypeConstraints && !hasNameConstraints)
            return true; // No constraints — any enum is allowed

        // Check Type[] first
        if (hasTypeConstraints && attr.AllowedEnumTypes.Contains(enumType))
            return true;

        // Check string[] (FullName match)
        if (hasNameConstraints)
        {
            var fullName = enumType.FullName;
            foreach (var name in attr.AllowedEnumTypeNames)
            {
                if (string.Equals(name, fullName, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a comma-separated display string of all allowed enum type names from the attribute.
    /// Merges both <see cref="SlotsEnumTypeAttribute.AllowedEnumTypes"/> and
    /// <see cref="SlotsEnumTypeAttribute.AllowedEnumTypeNames"/>.
    /// </summary>
    private static string GetAllowedEnumTypeDisplayNames(SlotsEnumTypeAttribute attr)
    {
        var names = new HashSet<string>();
        foreach (var t in attr.AllowedEnumTypes)
            names.Add(t.FullName);
        foreach (var n in attr.AllowedEnumTypeNames)
            names.Add(n);
        return string.Join(", ", names);
    }

    [Description("Finds nodes by type name (substring match) or property value. Returns compact list like ListNodes but filtered. Saves tokens vs. ListNodes + manual filtering.")]
    private string FindNodes(
        [Description("Substring of the node type name to match (case-insensitive). Pass empty string to skip type filter.")] string typeName = "",
        [Description("Optional property name to filter by.")] string? propertyName = null,
        [Description("Optional property value (string) to match.")] string? propertyValue = null)
    {
        var nodes = Tree.Nodes;
        var result = new JArray();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var nodeTypeName = node.GetType().Name;
            if (!string.IsNullOrEmpty(typeName) &&
                nodeTypeName.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!string.IsNullOrEmpty(propertyName))
            {
                var prop = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanRead) continue;
                var val = prop.GetValue(node);
                var valStr = val?.ToString() ?? "";
                if (propertyValue != null && !string.Equals(valStr, propertyValue, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var obj = new JObject
            {
                ["i"] = i,
                ["id"] = GetComponentId(node),
                ["t"] = nodeTypeName,
            };
            AppendScalarProperties(obj, node);
            result.Add(obj);
        }
        return result.ToString(Formatting.None);
    }

    [Description("Resolves a slot's runtime ID from its owning property name on a node. For collections, specify the index. Avoids needing GetNodeDetail just to get a slot ID.")]
    private string ResolveSlotId(
        [Description("Node index.")] int nodeIndex,
        [Description("Property name of the slot, e.g. 'InputSlot', 'OutputSlots'.")] string propertyName,
        [Description("For collection properties, the zero-based index within the collection. Ignored for single-slot properties.")] int collectionIndex = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = node!.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return Error($"Property '{propertyName}' not found on {node.GetType().Name}.");

        if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(prop.PropertyType))
        {
            if (prop.GetValue(node) is not IWorkflowSlotViewModel slot) return Error($"Slot property '{propertyName}' is null.");
            return JsonConvert.SerializeObject(new { status = "ok", id = GetComponentId(slot), prop = propertyName }, Formatting.None);
        }
        else if (IsSlotCollection(prop.PropertyType, out _))
        {
            if (prop.GetValue(node) is not IList col) return Error($"Collection '{propertyName}' is null.");
            if (collectionIndex < 0 || collectionIndex >= col.Count)
                return Error($"Index {collectionIndex} out of range [0,{col.Count}) for '{propertyName}'.");
            if (col[collectionIndex] is not IWorkflowSlotViewModel slot) return Error($"Item at index {collectionIndex} is not a slot.");
            return JsonConvert.SerializeObject(new { status = "ok", id = GetComponentId(slot), prop = $"{propertyName}[{collectionIndex}]" }, Formatting.None);
        }
        return Error($"Property '{propertyName}' is not a slot or slot collection.");
    }

    // ────────────────────────── Composite Functions (reduce round-trips) ──────────────────────────

    [Description("Connects two slots by property names on their owning nodes. No need to resolve slot IDs first. For collection properties, specify the index.")]
    private string ConnectByProperty(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot property name, e.g. 'OutputSlot', 'OutputSlots'.")] string senderProperty,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot property name, e.g. 'InputSlot'.")] string receiverProperty,
        [Description("For sender collection properties, the zero-based index. Default 0.")] int senderCollectionIndex = 0,
        [Description("For receiver collection properties, the zero-based index. Default 0.")] int receiverCollectionIndex = 0)
    {
        var senderSlot = ResolveSlotFromProperty(senderNodeIndex, senderProperty, senderCollectionIndex);
        if (senderSlot == null) return Error($"Cannot resolve sender slot: node={senderNodeIndex}, prop={senderProperty}[{senderCollectionIndex}].");
        var receiverSlot = ResolveSlotFromProperty(receiverNodeIndex, receiverProperty, receiverCollectionIndex);
        if (receiverSlot == null) return Error($"Cannot resolve receiver slot: node={receiverNodeIndex}, prop={receiverProperty}[{receiverCollectionIndex}].");

        Tree.SendConnectionCommand.Execute(senderSlot);
        Tree.ReceiveConnectionCommand.Execute(receiverSlot);

        bool connected = VerifyConnection(senderSlot, receiverSlot);
        if (!connected)
            return ConnectionRejected(senderSlot, receiverSlot,
                $"[{senderNodeIndex}].{senderProperty}", $"[{receiverNodeIndex}].{receiverProperty}");
        return Ok($"Connected [{senderNodeIndex}].{senderProperty}→[{receiverNodeIndex}].{receiverProperty}.");
    }

    [Description("Creates a node, optionally patches properties, optionally sets enum slot collection — all in one call. Returns full node detail with slot IDs so you can immediately connect. Replaces the 3-step: CreateNode → PatchNodeProperties → SetEnumSlotCollection.")]
    private string CreateAndConfigureNode(
        [Description("Fully-qualified type name.")] string fullTypeName,
        [Description("Left px.")] double left = 0,
        [Description("Top px.")] double top = 0,
        [Description("Width px. 0 = auto from AgentContext.")] double width = 0,
        [Description("Height px. 0 = auto from AgentContext.")] double height = 0,
        [Description("Optional JSON patch for properties, e.g. '{\"Title\":\"MyNode\"}'. null to skip.")] string? jsonPatch = null,
        [Description("Optional: name of [EnumSlotCollection] property to set, e.g. 'OutputSlots'. null to skip.")] string? enumSlotProperty = null,
        [Description("Optional: fully-qualified enum type name for SetEnumSlotCollection. Required if enumSlotProperty is set.")] string? enumTypeName = null)
    {
        // Step 1: Create node
        var createResult = CreateNode(fullTypeName, left, top, width, height);
        JObject createObj;
        try { createObj = JObject.Parse(createResult); }
        catch { return createResult; }
        if (createObj["status"]?.ToString() == "error") return createResult;

        var nodeIndex = createObj["i"]?.Value<int>() ?? -1;
        if (nodeIndex < 0 || nodeIndex >= Tree.Nodes.Count)
            return Error("Node created but index invalid.");

        var results = new JObject { ["create"] = createObj };

        // Step 2: Patch properties if provided
        if (!string.IsNullOrEmpty(jsonPatch))
        {
            var patchResult = PatchNodeProperties(nodeIndex, jsonPatch!);
            results["patch"] = patchResult;
        }

        // Step 3: Set enum slot collection if provided
        if (!string.IsNullOrEmpty(enumSlotProperty) && !string.IsNullOrEmpty(enumTypeName))
        {
            var enumResult = SetEnumSlotCollection(nodeIndex, enumSlotProperty!, enumTypeName!);
            results["enum"] = enumResult;
        }

        // Step 4: Return full node detail so caller has slot IDs immediately
        var node = Tree.Nodes[nodeIndex];
        results["detail"] = JObject.Parse(BuildNodeDetailJson(node, nodeIndex));
        results["status"] = "ok";

        return results.ToString(Formatting.None);
    }

    [Description("Deletes multiple nodes by indices in one call. Indices are processed in descending order to avoid shifting. Cascade: each node's slots and connections are auto-deleted.")]
    private string DeleteNodes(
        [Description("JSON array of node indices to delete, e.g. [0,2,5].")] string nodeIndicesJson)
    {
        int[] indices;
        try { indices = [.. JArray.Parse(nodeIndicesJson).Select(t => t.Value<int>()).Distinct().OrderByDescending(x => x)]; }
        catch (Exception ex) { return Error($"Invalid JSON array: {ex.Message}"); }

        int deleted = 0;
        var errors = new JArray();
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= Tree.Nodes.Count)
            {
                errors.Add($"Index {idx} out of range.");
                continue;
            }
            Tree.Nodes[idx].DeleteCommand.Execute(null);
            deleted++;
        }

        var result = new JObject { ["status"] = "ok", ["deleted"] = deleted };
        if (errors.Count > 0) result["errors"] = errors;
        return result.ToString(Formatting.None);
    }

    [Description("Sets absolute positions for multiple nodes in one call. Coordinate system: origin (0,0) = top-left, x increases rightward, y increases downward. Each entry: {i: nodeIndex, x: left, y: top, l?: layer}. Saves N tool calls.")]
    private string ArrangeNodes(
        [Description("JSON array of position entries: [{\"i\":0,\"x\":100,\"y\":200},{\"i\":1,\"x\":400,\"y\":200}]")] string arrangementsJson)
    {
        JArray arrangements;
        try { arrangements = JArray.Parse(arrangementsJson); }
        catch (Exception ex) { return Error($"Invalid JSON array: {ex.Message}"); }

        int moved = 0;
        var errors = new JArray();
        foreach (var entry in arrangements)
        {
            var idx = entry["i"]?.Value<int>() ?? -1;
            if (idx < 0 || idx >= Tree.Nodes.Count)
            {
                errors.Add($"Index {idx} out of range.");
                continue;
            }
            var x = entry["x"]?.Value<double>() ?? 0;
            var y = entry["y"]?.Value<double>() ?? 0;
            var layer = entry["l"]?.Value<int>() ?? 0;
            Tree.Nodes[idx].SetAnchorCommand.Execute(new Anchor(x, y, layer));
            moved++;
        }

        var result = new JObject { ["status"] = "ok", ["moved"] = moved };
        if (errors.Count > 0) result["errors"] = errors;
        return result.ToString(Formatting.None);
    }

    [Description("Returns the full topology: all nodes with their slots (including property names and IDs), plus all connections. One call replaces ListNodes + GetNodeDetail×N + ListConnections. Use for complex multi-node operations.")]
    private string GetFullTopology()
    {
        var nodesArr = new JArray();
        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            var node = Tree.Nodes[i];
            var slotPropertyMap = BuildSlotPropertyMap(node);
            var nodeObj = new JObject
            {
                ["i"] = i,
                ["id"] = GetComponentId(node),
                ["t"] = node.GetType().Name,
            };
            AppendScalarProperties(nodeObj, node);

            var slotsArr = new JArray();
            for (int s = 0; s < node.Slots.Count; s++)
            {
                var slot = node.Slots[s];
                var slotObj = new JObject
                {
                    ["si"] = s,
                    ["id"] = GetComponentId(slot),
                    ["ch"] = slot.Channel.ToString(),
                };
                if (slotPropertyMap.TryGetValue(slot, out var propName))
                    slotObj["prop"] = propName;
                slotsArr.Add(slotObj);
            }
            nodeObj["slots"] = slotsArr;
            nodesArr.Add(nodeObj);
        }

        var linksArr = new JArray();
        foreach (var link in Tree.Links)
        {
            if (!link.IsVisible) continue;
            linksArr.Add(new JObject
            {
                ["sid"] = link.Sender != null ? GetComponentId(link.Sender) : null,
                ["rid"] = link.Receiver != null ? GetComponentId(link.Receiver) : null,
            });
        }

        return new JObject
        {
            ["nodes"] = nodesArr,
            ["links"] = linksArr,
        }.ToString(Formatting.None);
    }

    /// <summary>
    /// Resolves a slot instance from node index + property name + optional collection index.
    /// Returns null if not found.
    /// </summary>
    private IWorkflowSlotViewModel? ResolveSlotFromProperty(int nodeIndex, string propertyName, int collectionIndex = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out _) || node == null) return null;
        var prop = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null || !prop.CanRead) return null;

        if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(prop.PropertyType))
            return prop.GetValue(node) as IWorkflowSlotViewModel;

        if (IsSlotCollection(prop.PropertyType, out _))
        {
            if (prop.GetValue(node) is not IList col || collectionIndex < 0 || collectionIndex >= col.Count)
            {
            }
            else
                return col[collectionIndex] as IWorkflowSlotViewModel;
        }
        return null;
    }

    /// <summary>
    /// Applies an imperceptible nudge (+0.5, −0.5 px) to force the UI to recalculate
    /// slot anchor positions. Required after any mutation that changes slots on a node
    /// with <see cref="EnumSlotCollectionAttribute"/>-marked collections.
    /// </summary>
    private static void NudgeNode(IWorkflowNodeViewModel node)
    {
        node.MoveCommand.Execute(new Offset(0.5, 0));
        node.MoveCommand.Execute(new Offset(-0.5, 0));
    }

    /// <summary>
    /// Returns <c>true</c> if the node type has any property marked with
    /// <see cref="EnumSlotCollectionAttribute"/>.
    /// </summary>
    private static bool HasEnumSlotCollection(IWorkflowNodeViewModel node)
    {
        return node.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetCustomAttribute<EnumSlotCollectionAttribute>() != null);
    }

    /// <summary>
    /// Nudges the node only if it has any <see cref="EnumSlotCollectionAttribute"/>-marked
    /// slot collection, ensuring the UI refreshes slot coordinates.
    /// </summary>
    private static void NudgeIfEnumSlotNode(IWorkflowNodeViewModel node)
    {
        if (HasEnumSlotCollection(node))
            NudgeNode(node);
    }

    /// <summary>
    /// Checks whether a connection was actually established between two slots
    /// by verifying the link exists in <see cref="IWorkflowTreeViewModel.LinksMap"/>.
    /// The framework may silently reject connections due to channel incompatibility,
    /// same-node constraint, or developer-overridden <c>ValidateConnection</c>.
    /// </summary>
    private bool VerifyConnection(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        return Tree.LinksMap.TryGetValue(sender, out var dic) && dic.ContainsKey(receiver);
    }

    /// <summary>
    /// Builds a structured error response when a connection is rejected by the framework,
    /// including diagnostic hints about the likely rejection reason.
    /// </summary>
    private string ConnectionRejected(
        IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver,
        string senderLabel, string receiverLabel)
    {
        var reasons = new List<string>();
        if (sender.Parent != null && receiver.Parent != null && sender.Parent == receiver.Parent)
            reasons.Add("same-node connection is not allowed");
        if (!sender.Channel.HasFlag(SlotChannel.OneTarget) &&
            !sender.Channel.HasFlag(SlotChannel.MultipleTargets) &&
            !sender.Channel.HasFlag(SlotChannel.OneBoth) &&
            !sender.Channel.HasFlag(SlotChannel.MultipleBoth))
            reasons.Add($"sender channel '{sender.Channel}' cannot send");
        if (!receiver.Channel.HasFlag(SlotChannel.OneSource) &&
            !receiver.Channel.HasFlag(SlotChannel.MultipleSources) &&
            !receiver.Channel.HasFlag(SlotChannel.OneBoth) &&
            !receiver.Channel.HasFlag(SlotChannel.MultipleBoth))
            reasons.Add($"receiver channel '{receiver.Channel}' cannot receive");
        if (reasons.Count == 0)
            reasons.Add("developer ValidateConnection rule or channel capacity limit");

        return JsonConvert.SerializeObject(new
        {
            status = "rejected",
            message = $"Connection {senderLabel}→{receiverLabel} was rejected by the framework.",
            reasons,
            hint = "Do NOT retry the same connection. Check slot channels and ValidateConnection rules, or choose different slots."
        }, Formatting.None);
    }

    private static string Ok(string message) => JsonConvert.SerializeObject(new { status = "ok", message }, Formatting.None);
    private static string Error(string message) => JsonConvert.SerializeObject(new { status = "error", message }, Formatting.None);
}
