using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.WorkflowSystem;

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

        return new List<AITool>
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
        };
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

    [Description("Moves a node by relative offset. Undo-able.")]
    private string MoveNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Horizontal offset px.")] double offsetX,
        [Description("Vertical offset px.")] double offsetY)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        node!.MoveCommand.Execute(new Offset(offsetX, offsetY));
        return Ok($"Moved {nodeIndex} by ({offsetX},{offsetY}).");
    }

    [Description("Sets absolute position of a node.")]
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

    [Description("Connects two slots by node/slot indices. Uses Tree.SendConnectionCommand + ReceiveConnectionCommand.")]
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
        return Ok($"Connected [{senderNodeIndex}][{senderSlotIndex}]→[{receiverNodeIndex}][{receiverSlotIndex}].");
    }

    [Description("Connects two slots by their runtime IDs. Stable across add/remove.")]
    private string ConnectSlotsById(
        [Description("Runtime ID of the sender slot.")] string senderSlotId,
        [Description("Runtime ID of the receiver slot.")] string receiverSlotId)
    {
        var sender = FindComponentById(senderSlotId) as IWorkflowSlotViewModel;
        if (sender == null) return Error($"Sender slot '{senderSlotId}' not found.");
        var receiver = FindComponentById(receiverSlotId) as IWorkflowSlotViewModel;
        if (receiver == null) return Error($"Receiver slot '{receiverSlotId}' not found.");

        Tree.SendConnectionCommand.Execute(sender);
        Tree.ReceiveConnectionCommand.Execute(receiver);
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
        return ComponentPatcher.ApplyPatch(node!, jsonPatch);
    }

    [Description("Patches custom properties on any component by runtime ID. Same rejection rules as PatchNodeProperties.")]
    private string PatchComponentById(
        [Description("Runtime ID of the component.")] string runtimeId,
        [Description("JSON patch object.")] string jsonPatch)
    {
        var component = FindComponentById(runtimeId);
        if (component == null) return Error($"Component '{runtimeId}' not found.");
        return ComponentPatcher.ApplyPatch(component, jsonPatch);
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
        return CommandInvoker.Invoke(node!, commandName, jsonParameter);
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
        return CommandInvoker.Invoke(component, commandName, jsonParameter);
    }

    [Description("Creates a node and adds it to the tree via CreateNodeCommand. IMPORTANT: Nodes must NEVER have Size(0,0). Always provide width/height, or use GetComponentContext first to discover the type's documented default size. If you cannot determine the default, use width=300 height=260 as a safe fallback.")]
    private string CreateNode(
        [Description("Fully-qualified type name.")] string fullTypeName,
        [Description("Left px.")] double left = 0,
        [Description("Top px.")] double top = 0,
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

        try
        {
            var node = (IWorkflowNodeViewModel)Activator.CreateInstance(type);
            node.Anchor = new Anchor(left, top, 0);
            Tree.CreateNodeCommand.Execute(node);
            node.SetSizeCommand.Execute(new Size(width, height));
            return JsonConvert.SerializeObject(new
            {
                status = "ok",
                id = GetComponentId(node),
                i = IndexOfNode(node),
                w = width,
                h = height,
            }, Formatting.None);
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
            indices = JArray.Parse(nodeIndicesJson).Select(t => t.Value<int>()).ToArray();
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
            var args = op["args"] as JObject ?? new JObject();

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
        };
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
        }
    }

    private static string Ok(string message) => JsonConvert.SerializeObject(new { status = "ok", message }, Formatting.None);
    private static string Error(string message) => JsonConvert.SerializeObject(new { status = "error", message }, Formatting.None);
}
