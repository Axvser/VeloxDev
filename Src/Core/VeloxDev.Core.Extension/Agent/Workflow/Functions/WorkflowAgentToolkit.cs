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
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Provides MAF-compatible <see cref="AITool"/> instances that give an Agent
/// full operational control over a single <see cref="IWorkflowTreeViewModel"/>.
/// All JSON output uses <see cref="Formatting.None"/> to minimize token consumption.
/// </summary>
public sealed class WorkflowAgentToolkit(WorkflowAgentScope scope)
{
    private readonly WorkflowAgentScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly WorkflowStateTracker _tracker = new(scope.Tree);
    private int _toolCallCount;
    private int _readToolCallCount;
    private int _writeToolCallCount;
    private IWorkflowTreeViewModel Tree => _scope.Tree;

    /// <summary>
    /// Creates the AI tools for workflow operations within the scoped tree, optionally restricted
    /// to the given <see cref="WorkflowToolCategory"/> flags. Every tool is wrapped with
    /// <see cref="TrackedAIFunction"/> so that tracking is invoked after each call.
    /// Developer-registered custom tools are always included regardless of <paramref name="categories"/>.
    /// </summary>
    public IList<AITool> CreateTools(WorkflowToolCategory categories = WorkflowToolCategory.All)
    {
        AITool T(Delegate method, string name)
            => new TrackedAIFunction(AIFunctionFactory.Create(method, name), this);

        var tools = new List<AITool>();

        void Add(WorkflowToolCategory category, params AITool[] items)
        {
            if ((categories & category) == category)
                tools.AddRange(items);
        }

        // ── Query (read-only inspection) ──
        Add(WorkflowToolCategory.Query,
            T(ListNodes, nameof(ListNodes)),
            T(GetNodeDetail, nameof(GetNodeDetail)),
            T(GetNodeDetailById, nameof(GetNodeDetailById)),
            T(ListConnections, nameof(ListConnections)),
            T(GetTypeSchema, nameof(GetTypeSchema)),
            T(GetWorkflowSummary, nameof(GetWorkflowSummary)),
            T(GetComponentContext, nameof(GetComponentContext)),
            T(ListComponentCommands, nameof(ListComponentCommands)),
            T(FindNodes, nameof(FindNodes)),
            T(ResolveSlotId, nameof(ResolveSlotId)),
            T(ListSlotProperties, nameof(ListSlotProperties)),
            T(GetEnumSlotByValue, nameof(GetEnumSlotByValue)),
            T(GetLinkDetail, nameof(GetLinkDetail)),
            T(ListCreatableTypes, nameof(ListCreatableTypes)),
            T(ValidateWorkflow, nameof(ValidateWorkflow)),
            T(GetFullTopology, nameof(GetFullTopology)),
            T(CompileWorkflow, nameof(CompileWorkflow)),
            T(GetCompileStatus, nameof(GetCompileStatus)),
            T(GetExecutionLog, nameof(GetExecutionLog)));

        // ── State tracking / diff / dirty ──
        Add(WorkflowToolCategory.State,
            T(TakeSnapshot, nameof(TakeSnapshot)),
            T(GetChangesSinceSnapshot, nameof(GetChangesSinceSnapshot)),
            T(MarkDirty, nameof(MarkDirty)));

        // ── Structural mutation (each tool executes exactly one component command — no bundled
        // multi-step gestures, so the framework's undo/redo stack stays the source of truth) ──
        Add(WorkflowToolCategory.Mutation,
            T(MoveNode, nameof(MoveNode)),
            T(SetNodePosition, nameof(SetNodePosition)),
            T(ResizeNode, nameof(ResizeNode)),
            T(DeleteNode, nameof(DeleteNode)),
            T(DeleteSlot, nameof(DeleteSlot)),
            T(ConnectSlots, nameof(ConnectSlots)),
            T(ConnectSlotsById, nameof(ConnectSlotsById)),
            T(ConnectByProperty, nameof(ConnectByProperty)),
            T(DisconnectSlots, nameof(DisconnectSlots)),
            T(DisconnectSlotsById, nameof(DisconnectSlotsById)),
            T(SetSlotChannel, nameof(SetSlotChannel)),
            T(SetEnumSlotChannel, nameof(SetEnumSlotChannel)),
            T(ConnectEnumSlot, nameof(ConnectEnumSlot)),
            T(PatchNodeProperties, nameof(PatchNodeProperties)),
            T(PatchComponentById, nameof(PatchComponentById)),
            T(CreateNode, nameof(CreateNode)),
            T(CreateSlotOnNode, nameof(CreateSlotOnNode)),
            T(AddSlotToCollection, nameof(AddSlotToCollection)),
            T(RemoveSlotFromCollection, nameof(RemoveSlotFromCollection)),
            T(SetEnumSlotCollection, nameof(SetEnumSlotCollection)),
            T(Undo, nameof(Undo)),
            T(Redo, nameof(Redo)),
            T(ClearHistory, nameof(ClearHistory)));

        // ── Node execution (gated by WithAllowNodeExecution) ──
        Add(WorkflowToolCategory.Execution,
            T(ExecuteNode, nameof(ExecuteNode)),
            T(ExecuteNodes, nameof(ExecuteNodes)),
            T(BroadcastNode, nameof(BroadcastNode)),
            T(ReverseBroadcastNode, nameof(ReverseBroadcastNode)),
            // Chain-level entry: drives the compiled graph with the execution engine
            // (the demo's Run path). Distinct from ExecuteNode (node-level EXEC).
            T(RunCompiledWorkflow, nameof(RunCompiledWorkflow)));

        // ── Generic command execution (gated by WithAllowedGenericCommands) ──
        Add(WorkflowToolCategory.Command,
            T(ExecuteCommandOnNode, nameof(ExecuteCommandOnNode)),
            T(ExecuteCommandById, nameof(ExecuteCommandById)));

        // ── Graph traversal ──
        Add(WorkflowToolCategory.Graph,
            T(SearchForward, nameof(SearchForward)),
            T(SearchReverse, nameof(SearchReverse)),
            T(SearchAllRelative, nameof(SearchAllRelative)),
            T(IsConnected, nameof(IsConnected)),
            T(FindPath, nameof(FindPath)));

        // ── Layout ──
        // No bundled layout tools: aligning/distributing/auto-arranging multiple nodes is
        // performed node-by-node via MoveNode / SetNodePosition (each one SetAnchorCommand).

        // ── Analytics ──
        Add(WorkflowToolCategory.Analytics,
            T(GetNodeStatistics, nameof(GetNodeStatistics)));

        // ── Composite ──
        // No composite/bundled tools: every operation is a single component-command step so the
        // undo/redo stack (owned by Core) is never bypassed or double-submitted.

        // ── Interaction (only registered when handlers are configured AND level > 0) ──
        if (_scope.IsInteractionAllowed)
        {
            if (_scope.SelectionHandler != null)
                Add(WorkflowToolCategory.Interaction, T(RequestSelection, nameof(RequestSelection)));
            if (_scope.ConfirmationHandler != null)
                Add(WorkflowToolCategory.Interaction, T(RequestConfirmation, nameof(RequestConfirmation)));
        }

        // Merge developer-registered custom tools (always included). AIFunction-typed tools are
        // wrapped with TrackedAIFunction so they get the same UI-thread marshalling, MaxToolCalls
        // accounting, ToolCalled callback and auto-dirty handling as the built-in tools. Non-AIFunction
        // tools (e.g. raw MCP client tools) are added as-is.
        foreach (var tool in _scope.CustomTools)
            tools.Add(WrapCustomTool(tool));
        foreach (var tool in _scope.QueryOnlyCustomTools)
            tools.Add(WrapCustomTool(tool));

        return tools;
    }

    /// <summary>
    /// Wraps an <c>AIFunction</c> custom tool with <see cref="TrackedAIFunction"/> so it receives the
    /// same tracking (UI marshal, call counting, callback, auto-dirty) as the built-in tools.
    /// Non-<c>AIFunction</c> tools are returned unchanged.
    /// </summary>
    private AITool WrapCustomTool(AITool tool)
        => tool is AIFunction fn ? new TrackedAIFunction(fn, this) : tool;

    /// <summary>
    /// Wraps an <see cref="AIFunction"/> so that <see cref="Track"/> is called
    /// after every invocation, ensuring call counting and callback dispatch.
    /// </summary>
    private sealed class TrackedAIFunction(AIFunction inner, WorkflowAgentToolkit toolkit) : DelegatingAIFunction(inner)
    {
        private readonly WorkflowAgentToolkit _toolkit = toolkit;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            // Workflow components are UI-bound, so when the host configured a UI SynchronizationContext
            // and we are not already on it, marshal the entire tool call (body + tracking) onto it.
            var uiContext = _toolkit._scope.UIContext;
            if (uiContext is not null && !ReferenceEquals(uiContext, SynchronizationContext.Current))
            {
                return await RunOnContextAsync(uiContext, cancellationToken,
                    () => InvokeCoreInnerAsync(arguments, cancellationToken)).ConfigureAwait(false);
            }
            return await InvokeCoreInnerAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<object?> InvokeCoreInnerAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            // ── Pre-flight: reject if any configured call limit would be exceeded ──
            if (_toolkit._scope.MaxToolCalls.HasValue && _toolkit._toolCallCount >= _toolkit._scope.MaxToolCalls.Value)
                return WorkflowAgentToolkit.Error($"Tool call limit ({_toolkit._scope.MaxToolCalls.Value}) exceeded. No further tool calls are allowed.");
            bool isQueryTool = _toolkit.IsQueryTool(Name);
            if (!isQueryTool && _toolkit._scope.MaxWriteToolCalls.HasValue && _toolkit._writeToolCallCount >= _toolkit._scope.MaxWriteToolCalls.Value)
                return WorkflowAgentToolkit.Error($"Mutation tool call limit ({_toolkit._scope.MaxWriteToolCalls.Value}) exceeded. No further mutation tool calls are allowed.");
            if (isQueryTool && _toolkit._scope.MaxReadToolCalls.HasValue && _toolkit._readToolCallCount >= _toolkit._scope.MaxReadToolCalls.Value)
                return WorkflowAgentToolkit.Error($"Query tool call limit ({_toolkit._scope.MaxReadToolCalls.Value}) exceeded. No further query tool calls are allowed.");

            try
            {
                var result = await base.InvokeCoreAsync(arguments, cancellationToken);
                var resultText = result?.ToString() ?? string.Empty;
                await _toolkit.TrackAsync(Name, resultText);
                return result;
            }
            catch (Exception ex)
            {
                return WorkflowAgentToolkit.Error($"Tool '{Name}' threw an unhandled exception: {ex.Message}");
            }
        }

        private static async ValueTask<T> RunOnContextAsync<T>(
            SynchronizationContext context, CancellationToken ct, Func<ValueTask<T>> body)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => tcs.TrySetCanceled(ct)))
            {
                context.Post(async _ =>
                {
                    try
                    {
                        var result = await body();
                        tcs.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }, null);
                return await tcs.Task.ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Tool names that are purely read-only queries and must never trigger a dirty mark.
    /// Every other tool is treated as a mutation when <see cref="WorkflowAgentScope.AutoMarkDirty"/> is enabled.
    /// </summary>
    private static readonly HashSet<string> QueryToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ListNodes", "GetNodeDetail", "GetNodeDetailById", "ListConnections", "GetTypeSchema",
        "GetWorkflowSummary", "GetComponentContext",
        "ListComponentCommands", "GetChangesSinceSnapshot", "TakeSnapshot",
        "GetFullTopology", "FindNodes", "ResolveSlotId", "ListSlotProperties",
        "GetEnumSlotByValue", "GetLinkDetail", "GetNodeStatistics", "ListCreatableTypes",
        "ValidateWorkflow", "SearchForward", "SearchReverse", "SearchAllRelative",
        "IsConnected", "FindPath", "RequestSelection", "RequestConfirmation",
        "CompileWorkflow", "GetCompileStatus", "GetExecutionLog",
    };

    /// <summary>
    /// Wraps a tool result with call counting, callback invocation, max-call enforcement,
    /// and optional auto-dirty marking when <see cref="WorkflowAgentScope.AutoMarkDirty"/> is enabled.
    /// </summary>
    private async Task TrackAsync(string toolName, string result)
    {
        Interlocked.Increment(ref _toolCallCount);
        if (IsQueryTool(toolName))
            Interlocked.Increment(ref _readToolCallCount);
        else
            Interlocked.Increment(ref _writeToolCallCount);
        await _scope.RaiseToolCalledAsync(toolName, result, _toolCallCount);
        if (_scope.AutoMarkDirty && !QueryToolNames.Contains(toolName) && !_scope.IsQueryOnlyCustomTool(toolName))
            Tree.GetHelper().MarkDirty();
    }

    /// <summary>
    /// Whether a tool is a read-only query (a built-in <see cref="QueryToolNames"/> entry or a
    /// registered query-only custom tool).
    /// </summary>
    private bool IsQueryTool(string toolName)
        => QueryToolNames.Contains(toolName) || _scope.IsQueryOnlyCustomTool(toolName);

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

    [Description("Lists all visible connections only (compact, with link ids). GetFullTopology also returns connections alongside full node/slot detail — prefer it for the whole graph; use this only when you need links without node detail.")]
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

    [Description("Moves a node by relative offset. Coordinate system: +offsetX = rightward, +offsetY = downward (origin is top-left). Mirrors GUI node-drag: dispatches SetAnchorCommand, which is NOT undoable (Core's move command has no undo entry).")]
    private async Task<string> MoveNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Horizontal offset px.")] double offsetX,
        [Description("Vertical offset px.")] double offsetY,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var n = node!;
        var newAnchor = new Anchor(n.Anchor.Horizontal + offsetX, n.Anchor.Vertical + offsetY, n.Anchor.Layer);
        var completion = WaitForExitedAsync(n.SetAnchorCommand, cancellationToken);
        n.SetAnchorCommand.Execute(newAnchor);
        await completion.ConfigureAwait(false);
        return Ok($"Moved {nodeIndex} by ({offsetX},{offsetY}).");
    }

    [Description("Sets absolute position of a node. Coordinate system: origin (0,0) is top-left; left (X) increases rightward, top (Y) increases downward. Mirrors GUI node placement: dispatches SetAnchorCommand, which is NOT undoable (Core's move command has no undo entry).")]
    private async Task<string> SetNodePosition(
        [Description("Node index.")] int nodeIndex,
        [Description("Left px.")] double left,
        [Description("Top px.")] double top,
        [Description("Layer (z-order).")] int layer = 0,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var completion = WaitForExitedAsync(node!.SetAnchorCommand, cancellationToken);
        node.SetAnchorCommand.Execute(new Anchor(left, top, layer));
        await completion.ConfigureAwait(false);
        return Ok($"Position {nodeIndex} → ({left},{top},{layer}).");
    }

    [Description("Resizes a node. Dispatches SetSizeCommand, which is NOT undoable (mirrors Core's resize semantics).")]
    private async Task<string> ResizeNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Width px.")] double width,
        [Description("Height px.")] double height,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var n = node!;
        var oldSize = new Size(n.Size.Width, n.Size.Height);
        var newSize = new Size(width, height);
        if (oldSize.Width == newSize.Width && oldSize.Height == newSize.Height)
            return Ok($"Resized {nodeIndex} → ({width},{height}).");
        var completion = WaitForExitedAsync(n.SetSizeCommand, cancellationToken);
        n.SetSizeCommand.Execute(newSize);
        await completion.ConfigureAwait(false);
        return Ok($"Resized {nodeIndex} → ({width},{height}).");
    }

    [Description("Deletes a node. Cascade: auto-deletes all child slots and their connections — no need to delete them first.")]
    private async Task<string> DeleteNode(
        [Description("Node index.")] int nodeIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var completion = WaitForExitedAsync(node!.DeleteCommand, cancellationToken);
        node.DeleteCommand.Execute(null);
        await completion.ConfigureAwait(false);
        return Ok($"Node {nodeIndex} deleted.");
    }

    [Description("Deletes a slot and its connections.")]
    private async Task<string> DeleteSlot(
        [Description("Node index.")] int nodeIndex,
        [Description("Slot index within the node.")] int slotIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSlot(nodeIndex, slotIndex, out var slot, out var error)) return error;
        var completion = WaitForExitedAsync(slot!.DeleteCommand, cancellationToken);
        slot.DeleteCommand.Execute(null);
        await completion.ConfigureAwait(false);
        return Ok($"Slot [{nodeIndex}][{slotIndex}] deleted.");
    }

    [Description("⚠ Prefer ConnectByProperty — slot indices shift on SlotEnumerator nodes. Use only after ListSlotProperties confirms a stable index. Returns the slot→property map so you can switch to property routing.")]
    private async Task<string> ConnectSlots(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot index.")] int senderSlotIndex,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot index.")] int receiverSlotIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSlot(senderNodeIndex, senderSlotIndex, out var senderSlot, out var error)) return error;
        if (!TryGetSlot(receiverNodeIndex, receiverSlotIndex, out var receiverSlot, out error)) return error;

        // Preflight: resolve property names so failure diagnostics are actionable
        var senderPropMap = BuildSlotPropertyMap(Tree.Nodes[senderNodeIndex]);
        var receiverPropMap = BuildSlotPropertyMap(Tree.Nodes[receiverNodeIndex]);
        senderPropMap.TryGetValue(senderSlot!, out var senderPropHint);
        receiverPropMap.TryGetValue(receiverSlot!, out var receiverPropHint);

        await SendReceiveAsync(senderSlot!, receiverSlot!, cancellationToken);

        bool connected = VerifyConnection(senderSlot!, receiverSlot!);
        if (!connected)
        {
            var rejected = JObject.Parse(ConnectionRejected(senderSlot!, receiverSlot!,
                $"[{senderNodeIndex}][{senderSlotIndex}]", $"[{receiverNodeIndex}][{receiverSlotIndex}]"));
            if (senderPropHint != null)
                rejected["senderProperty"] = senderPropHint;
            if (receiverPropHint != null)
                rejected["receiverProperty"] = receiverPropHint;
            rejected["preferredAlternative"] = $"ConnectByProperty senderNode={senderNodeIndex} senderProperty={senderPropHint ?? "?"} receiverNode={receiverNodeIndex} receiverProperty={receiverPropHint ?? "?"}";
            return rejected.ToString(Formatting.None);
        }

        var result = new JObject
        {
            ["status"] = "ok",
            ["message"] = $"Connected [{senderNodeIndex}][{senderSlotIndex}]→[{receiverNodeIndex}][{receiverSlotIndex}].",
        };
        if (senderPropHint != null) result["senderProperty"] = senderPropHint;
        if (receiverPropHint != null) result["receiverProperty"] = receiverPropHint;
        if (senderPropHint != null && receiverPropHint != null)
            result["preferPropertyRoute"] = $"Next time use ConnectByProperty senderNode={senderNodeIndex} senderProperty={senderPropHint} receiverNode={receiverNodeIndex} receiverProperty={receiverPropHint}";
        return result.ToString(Formatting.None);
    }

    [Description("Connects two slots by their runtime IDs. IDs are stable across UI redraws but NOT across SlotEnumerator reconfiguration. Prefer ConnectByProperty for SlotEnumerator and generated slot collections; use this only with IDs obtained after the latest collection configuration. The framework may silently reject: check 'connected' in the response.")]
    private async Task<string> ConnectSlotsById(
        [Description("Runtime ID of the sender slot.")] string senderSlotId,
        [Description("Runtime ID of the receiver slot.")] string receiverSlotId,
        CancellationToken cancellationToken = default)
    {
        if (FindComponentById(senderSlotId) is not IWorkflowSlotViewModel sender) return Error($"Sender slot '{senderSlotId}' not found.");
        if (FindComponentById(receiverSlotId) is not IWorkflowSlotViewModel receiver) return Error($"Receiver slot '{receiverSlotId}' not found.");

        // Preflight: resolve property names for richer diagnostics
        var senderPropHint = sender.Parent != null ? (BuildSlotPropertyMap(sender.Parent).TryGetValue(sender, out var sp) ? sp : null) : null;
        var receiverPropHint = receiver.Parent != null ? (BuildSlotPropertyMap(receiver.Parent).TryGetValue(receiver, out var rp) ? rp : null) : null;

        await SendReceiveAsync(sender, receiver, cancellationToken);

        bool connected = VerifyConnection(sender, receiver);
        if (!connected)
        {
            var rejected = JObject.Parse(ConnectionRejected(sender, receiver, senderSlotId, receiverSlotId));
            if (senderPropHint != null) rejected["senderProperty"] = senderPropHint;
            if (receiverPropHint != null) rejected["receiverProperty"] = receiverPropHint;
            return rejected.ToString(Formatting.None);
        }

        var result = new JObject { ["status"] = "ok", ["message"] = $"Connected {senderSlotId}→{receiverSlotId}." };
        if (senderPropHint != null) result["senderProperty"] = senderPropHint;
        if (receiverPropHint != null) result["receiverProperty"] = receiverPropHint;
        return result.ToString(Formatting.None);
    }

    [Description("Removes a connection between two slots by node/slot indices.")]
    private async Task<string> DisconnectSlots(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot index.")] int senderSlotIndex,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot index.")] int receiverSlotIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSlot(senderNodeIndex, senderSlotIndex, out var senderSlot, out var error)) return error;
        if (!TryGetSlot(receiverNodeIndex, receiverSlotIndex, out var receiverSlot, out error)) return error;

        if (Tree.LinksMap.TryGetValue(senderSlot!, out var dic) && dic.TryGetValue(receiverSlot!, out var link))
        {
            var completion = WaitForExitedAsync(link.DeleteCommand, cancellationToken);
            link.DeleteCommand.Execute(null);
            await completion.ConfigureAwait(false);
            return Ok($"Disconnected [{senderNodeIndex}][{senderSlotIndex}]✕[{receiverNodeIndex}][{receiverSlotIndex}].");
        }
        return Error("No connection found between the specified slots.");
    }

    [Description("Executes ReceiveCommand on a node and WAITS until the node actually completes. Returns 'ok' only after real completion; returns an error if the receive fails. Disabled by default: the host must call WithAllowNodeExecution(true) on the scope.")]
    private async Task<string> ExecuteNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter (becomes ITaskContext.Data, nullable).")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("ExecuteNode is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        try
        {
            await WaitForCommandAsync(node!.ReceiveCommand, new TaskContext(data: parameter), cancellationToken).ConfigureAwait(false);
            return Ok($"Receive on node {nodeIndex} completed.");
        }
        catch (OperationCanceledException)
        {
            return Error($"Receive on node {nodeIndex} was cancelled.");
        }
        catch (Exception ex)
        {
            return Error($"Receive on node {nodeIndex} failed: {ex.Message}");
        }
    }

    [Description("Executes BroadcastCommand on a node to forward data along connections, and waits until the broadcast command completes (downstream dispatch itself is fire-and-forget). Disabled by default: requires WithAllowNodeExecution(true).")]
    private async Task<string> BroadcastNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter.")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("BroadcastNode is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        try
        {
            await WaitForCommandAsync(node!.BroadcastCommand, parameter, cancellationToken).ConfigureAwait(false);
            return Ok($"Broadcast on node {nodeIndex} completed.");
        }
        catch (Exception ex)
        {
            return Error($"Broadcast on node {nodeIndex} failed: {ex.Message}");
        }
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

    /// <summary>
    /// Clears the entire undo/redo history. The canvas state is left untouched — only the
    /// recorded mutation trail is dropped. Use after a batch op the user must not undo through
    /// (e.g. ClearCanvas), so the user cannot walk back past the boundary.
    /// </summary>
    [Description("Clears the entire undo/redo history WITHOUT touching the canvas. The workflow state stays as-is; only the recorded mutation trail is dropped. Use after a bulk operation (e.g. clearing the canvas) so undo cannot walk back past the boundary.")]
    private string ClearHistory()
    {
        Tree.GetHelper().ClearHistory();
        return Ok("Undo/redo history cleared (canvas untouched).");
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
        RefreshSlotAnchorsIfEnumSlotNode(node!);
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
            RefreshSlotAnchorsIfEnumSlotNode(patchedNode);
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

    [Description("Marks the workflow tree as dirty. Call once at the end of an Agent task after one or more mutations so the view can refresh consistently.")]
    private string MarkDirty()
    {
        Tree.GetHelper().MarkDirty();
        return Ok("Tree marked dirty.");
    }

    // ────────────────────────── Generic Command Execution ──────────────────────────

    [Description("Executes any command on a node by index. Use ListComponentCommands to discover available commands. Disabled by default: the host must allowlist the command via WithAllowedGenericCommands.")]
    private string ExecuteCommandOnNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Command name, e.g. 'ReceiveCommand'. 'Command' suffix optional.")] string commandName,
        [Description("JSON parameter, or null.")] string? jsonParameter = null)
    {
        if (!_scope.IsGenericCommandAllowed(commandName))
            return Error($"Generic command execution is disabled by host policy. The host must allowlist '{commandName}' via WithAllowedGenericCommands.");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var result = CommandInvoker.Invoke(node!, commandName, jsonParameter);
        RefreshSlotAnchorsIfEnumSlotNode(node!);
        return result;
    }

    [Description("Executes any command on a component by runtime ID. Works for nodes, slots, links. Disabled by default: the host must allowlist the command via WithAllowedGenericCommands.")]
    private string ExecuteCommandById(
        [Description("Runtime ID.")] string runtimeId,
        [Description("Command name.")] string commandName,
        [Description("JSON parameter, or null.")] string? jsonParameter = null)
    {
        if (!_scope.IsGenericCommandAllowed(commandName))
            return Error($"Generic command execution is disabled by host policy. The host must allowlist '{commandName}' via WithAllowedGenericCommands.");
        var component = FindComponentById(runtimeId);
        if (component == null)
            return Error($"Component '{runtimeId}' not found.");
        var result = CommandInvoker.Invoke(component, commandName, jsonParameter);
        if (component is IWorkflowNodeViewModel cmdNode)
            RefreshSlotAnchorsIfEnumSlotNode(cmdNode);
        return result;
    }

    [Description("Creates a node (via CreateNodeCommand — never modify the Nodes collection directly). Width/height: 0 reads the type's default ([DefaultSize]) if declared, else 300×260. Position auto-offsets to avoid overlap.")]
    private string CreateNode(
        [Description("Fully-qualified type name.")] string fullTypeName,
        [Description("Left px. Consider existing node positions to avoid overlap.")] double left = 0,
        [Description("Top px. Consider existing node positions to avoid overlap.")] double top = 0,
        [Description("Width px. 0 = type's default size (fallback 300×260). Use GetComponentContext to discover the exact default.")] double width = 0,
        [Description("Height px. 0 = type's default size (fallback 300×260). Use GetComponentContext to discover the exact default.")] double height = 0)
    {
        var type = TypeIntrospector.ResolveType(fullTypeName);
        if (type == null)
            return Error($"Type '{fullTypeName}' not found.");
        if (!typeof(IWorkflowNodeViewModel).IsAssignableFrom(type))
            return Error($"'{fullTypeName}' does not implement IWorkflowNodeViewModel.");

        // Resolve a non-zero size: the caller's explicit value wins; otherwise read the node's real
        // default baked into the field initializer by the generator ([DefaultSize]), which survives
        // Activator.CreateInstance. Only fall back to the deterministic 300×260 if the type declares
        // no default at all.
        IWorkflowNodeViewModel node;
        try
        {
            node = (IWorkflowNodeViewModel)Activator.CreateInstance(type);
            if (width <= 0) width = node.Size.Width > 0 ? node.Size.Width : 300;
            if (height <= 0) height = node.Size.Height > 0 ? node.Size.Height : 260;
        }
        catch (Exception ex)
        {
            return Error($"Failed to create node: {ex.Message}");
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
            node.Anchor = new Anchor(left, top, 0);
            // Set size before adding to tree so the first Virtualize call (fired by
            // OnNodesChanged → Nodes.Add) already sees the correct bounds.  If size
            // were set afterwards the node would enter the spatial index with zero-size
            // bounds and miss the viewport check, causing it to never enter VisibleItems.
            node.Size.Width = width;
            node.Size.Height = height;
            Tree.CreateNodeCommand.Execute(node);
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

    [Description("Lists slot properties on a node type: named single slots, slot collection properties, and SlotEnumerator properties. Shows property name, type, current count, and slot IDs.")]
    private string ListSlotProperties(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var result = new JArray();
        var type = node!.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;

            // SlotEnumerator<TSlot>
            if (IsSlotEnumeratorProperty(prop.PropertyType, out _))
            {
                try
                {
                    var enumerator = prop.GetValue(node);
                    if (enumerator == null) continue;
                    var enumPropType = enumerator.GetType();
                    var selectorTypeName = enumPropType.GetProperty("SelectorTypeName")?.GetValue(enumerator) as string;
                    var ids = new JArray();
                    if (enumPropType.GetProperty("Items")?.GetValue(enumerator) is IEnumerable items)
                    {
                        foreach (var item in items)
                        {
                            var slotProp = item?.GetType().GetProperty("Slot");
                            if (slotProp?.GetValue(item) is IWorkflowSlotViewModel s)
                                ids.Add(GetComponentId(s));
                        }
                    }
                    var entry = new JObject
                    {
                        ["name"] = prop.Name,
                        ["collection"] = true,
                        ["slotEnumerator"] = true,
                        ["count"] = ids.Count,
                        ["ids"] = ids,
                        ["currentSelectorType"] = selectorTypeName,
                        ["hint"] = "Use SetEnumSlotCollection to set or change the enum/bool type.",
                    };

                    // Expose allowed selector types from [SlotSelectors] on the enumerator property itself.
                    var slotSelectorsAttr = prop.GetCustomAttribute<SlotSelectorsAttribute>();
                    if (slotSelectorsAttr != null)
                    {
                        var allowedNames = GetAllowedEnumTypeDisplayNames(slotSelectorsAttr);
                        if (!string.IsNullOrEmpty(allowedNames))
                            entry["allowedSelectorTypes"] = new JArray(allowedNames.Split([", "], StringSplitOptions.RemoveEmptyEntries));
                    }

                    result.Add(entry);
                }
                catch { /* skip inaccessible */ }
                continue;
            }

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

                result.Add(entry);
            }
        }
        return result.ToString(Formatting.None);
    }

    [Description("Adds a new slot to a collection property on a node (e.g. OutputSlots). The slot is created via the node's CreateWorkflowSlot infrastructure and registered through the node's CreateSlotCommand (the native slot-mount path).")]
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

            // CreateSlotCommand mounts the slot into the node and its slot collections (the same
            // path a human triggers by adding a slot via the GUI). It produces the framework's
            // undo entry — the toolkit never Submit()s its own gesture.
            node.CreateSlotCommand.Execute(slot);

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

    [Description("Removes a slot from a collection property on a node by slot runtime ID. Triggers the slot's DeleteCommand (the native slot-removal path).")]
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
                var capturedSlot = slot;
                capturedSlot.DeleteCommand.Execute(null);
                return Ok($"Removed slot '{slotRuntimeId}' from '{propertyName}'. Count={col.Count}.");
            }
        }
        return Error($"Slot '{slotRuntimeId}' not found in '{propertyName}'.");
    }

    [Description("Sets the selector of a SlotEnumerator on an EXISTING node. enum/bool: pass the type name in 'selectorTypeOrJson' (e.g. 'Demo.NetworkRequestMethod', 'System.Boolean'). Non-enum ISlotProvider: GetTypeSchema(type) first, then pass JSON in 'selectorTypeOrJson' and the type name in 'nonEnumTypeName'. Do NOT delete/recreate the node. New branches are auto re-wired onto the previous branches' downstream (by position); a reused type's connections are restored — do NOT manually rewire.")]
    private string SetEnumSlotCollection(
        [Description("Node index.")] int nodeIndex,
        [Description("Name of the slot collection or SlotEnumerator property, e.g. 'OutputSlots'.")] string propertyName,
        [Description("For enum/bool: fully-qualified type name (e.g. 'Demo.ViewModels.NetworkRequestMethod'). For non-enum ISlotProvider: JSON constructed after calling GetTypeSchema to understand the type structure.")] string selectorTypeOrJson,
        [Description("Only required for non-enum ISlotProvider selectors: the fully-qualified .NET type name. Call GetTypeSchema with this name first to inspect structure before constructing JSON. Leave empty for enum/bool selectors.")] string nonEnumTypeName = "")
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var type = node!.GetType();
        var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return Error($"Property '{propertyName}' not found on {type.Name}.");

        // SlotEnumerator<TSlot> path
        if (IsSlotEnumeratorProperty(prop.PropertyType, out _))
        {
            var enumerator = prop.GetValue(node);
            if (enumerator == null) return Error($"SlotEnumerator '{propertyName}' is null.");

            // Determine whether we are in enum/bool mode or arbitrary-object mode
            bool isNonEnum = !string.IsNullOrWhiteSpace(nonEnumTypeName);

            if (isNonEnum)
            {
                // Non-enum path: deserialize JSON → concrete object, pass directly to SetSelector
                var targetType = TypeIntrospector.ResolveType(nonEnumTypeName);
                if (targetType == null) return Error($"Type '{nonEnumTypeName}' not found.");

                // Validate against [SlotSelectors] whitelist when present.
                var selectorsAttrNE = prop.GetCustomAttribute<SlotSelectorsAttribute>();
                if (selectorsAttrNE != null && !IsEnumTypeAllowed(selectorsAttrNE, targetType))
                {
                    var allowed = GetAllowedEnumTypeDisplayNames(selectorsAttrNE);
                    return Error($"Selector type '{nonEnumTypeName}' is not allowed for '{propertyName}'. Allowed types: {allowed}");
                }

                object? selectorValue;
                try
                {
                    selectorValue = JsonConvert.DeserializeObject(selectorTypeOrJson, targetType);
                }
                catch (Exception ex)
                {
                    return Error($"Failed to deserialize selector JSON as '{nonEnumTypeName}': {ex.Message}");
                }

                if (selectorValue == null) return Error($"Deserialized selector value is null.");
                if (!IsEnumeratorInstalled(enumerator, node)) return Error($"SlotEnumerator '{propertyName}' is not installed on node {nodeIndex} — mount the node first, then retry.");

                // SetSelector captures the previous state and submits its own undoable
                // WorkflowActionPair internally. Do NOT wrap it in another Submit here —
                // that would create nested undo entries and break Ctrl+Z semantics.
                try
                {
                    InvokeSetSelector(enumerator, selectorValue);
                }
                catch (Exception ex)
                {
                    return Error($"SetSelector failed: {ex.Message}");
                }

                return new JObject
                {
                    ["ok"] = true,
                    ["selectorType"] = targetType.FullName,
                    ["property"] = propertyName,
                }.ToString(Formatting.None);
            }

            // Enum/bool path (original behaviour)
            var selectorType = selectorTypeOrJson == "System.Boolean" || selectorTypeOrJson == "bool"
                ? typeof(bool)
                : TypeIntrospector.ResolveType(selectorTypeOrJson);
            if (selectorType == null) return Error($"Type '{selectorTypeOrJson}' not found.");
            if (!selectorType.IsEnum && selectorType != typeof(bool))
                return Error($"'{selectorTypeOrJson}' is not an enum or bool type. If you intended to pass a non-enum selector value, supply the type name in 'nonEnumTypeName' and JSON in 'selectorTypeOrJson'.");

            // Validate against [SlotSelectors] allowed types if present on the enumerator property.
            // Framework-owned enum types (SlotChannel, SlotState, …) are always valid regardless of
            // any developer-specified whitelist — they must never be blocked by [SlotSelectors].
            var selectorsAttr2 = prop.GetCustomAttribute<SlotSelectorsAttribute>();
            if (selectorsAttr2 != null && !WorkflowAgentScope.IsFrameworkEnum(selectorType))
            {
                if (!IsEnumTypeAllowed(selectorsAttr2, selectorType))
                {
                    var allowed = GetAllowedEnumTypeDisplayNames(selectorsAttr2);
                    return Error($"Selector type '{selectorTypeOrJson}' is not allowed for '{propertyName}'. Allowed types: {allowed}");
                }
            }

            if (!IsEnumeratorInstalled(enumerator, node)) return Error($"SlotEnumerator '{propertyName}' is not installed on node {nodeIndex} — mount the node first, then retry.");

            // SetSelector captures the previous state (including old slots/links) and submits
            // its own undoable WorkflowActionPair internally. Call it directly — wrapping it in
            // another Submit created nested undo entries and required an anchor-refresh workaround.
            try
            {
                InvokeSetSelector(enumerator, selectorType);
            }
            catch (Exception ex)
            {
                return Error($"SetSelector failed: {ex.Message}");
            }

            var enumNames = selectorType == typeof(bool)
                ? ["False", "True"]
                : Enum.GetNames(selectorType);
            var slotIds = new JArray();
            if (enumerator.GetType().GetProperty("Items")?.GetValue(enumerator) is IEnumerable items)
            {
                int i = 0;
                foreach (var item in items)
                {
                    var slotProp = item?.GetType().GetProperty("Slot");
                    if (slotProp?.GetValue(item) is IWorkflowSlotViewModel s)
                    {
                        slotIds.Add(new JObject
                        {
                            ["id"] = GetComponentId(s),
                            ["label"] = i < enumNames.Length ? enumNames[i] : "?",
                        });
                    }
                    i++;
                }
            }
            return new JObject
            {
                ["ok"] = true,
                ["selectorType"] = selectorType.FullName,
                ["property"] = propertyName,
                ["count"] = slotIds.Count,
                ["slots"] = slotIds,
            }.ToString(Formatting.None);
        }

        return Error($"Property '{propertyName}' is not a SlotEnumerator.");
    }

    /// <summary>
    /// Builds a reverse map
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
                else if (IsSlotEnumeratorProperty(prop.PropertyType, out _))
                {
                    var enumerator = prop.GetValue(node);
                    if (enumerator?.GetType().GetProperty("Items")?.GetValue(enumerator) is IEnumerable items)
                    {
                        int i = 0;
                        foreach (var item in items)
                        {
                            var slotProp = item?.GetType().GetProperty("Slot");
                            if (slotProp?.GetValue(item) is IWorkflowSlotViewModel s)
                                map[s] = $"{prop.Name}[{i}]";
                            i++;
                        }
                    }
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

    private static bool IsSlotEnumeratorProperty(Type type, out Type? slotType)
    {
        slotType = null;
        if (!type.IsGenericType) return false;

        // 1. Exact match: SlotEnumerator<TSlot>
        var def = type.GetGenericTypeDefinition();
        if (def.Name.StartsWith("SlotEnumerator`") && def.Namespace == "VeloxDev.WorkflowSystem")
        {
            slotType = type.GetGenericArguments()[0];
            return true;
        }

        // 2. The type IS IConditionalSlotProvider<TSlot> itself
        if (def.Name.StartsWith("IConditionalSlotProvider`") && def.Namespace == "VeloxDev.WorkflowSystem")
        {
            slotType = type.GetGenericArguments()[0];
            return true;
        }

        // 3. The type implements IConditionalSlotProvider<TSlot>
        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            var ifaceDef = iface.GetGenericTypeDefinition();
            if (ifaceDef.Name.StartsWith("IConditionalSlotProvider`") && ifaceDef.Namespace == "VeloxDev.WorkflowSystem")
            {
                slotType = iface.GetGenericArguments()[0];
                return true;
            }
        }

        return false;
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

    [Description("Executes ReverseBroadcastCommand on a node to trigger ReceiveCommand on upstream (source) nodes, and waits until the reverse-broadcast command completes (upstream dispatch itself is fire-and-forget). Disabled by default: requires WithAllowNodeExecution(true).")]
    private async Task<string> ReverseBroadcastNode(
        [Description("Node index.")] int nodeIndex,
        [Description("Optional parameter.")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("ReverseBroadcastNode is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        try
        {
            await WaitForCommandAsync(node!.ReverseBroadcastCommand, parameter, cancellationToken).ConfigureAwait(false);
            return Ok($"Reverse broadcast on node {nodeIndex} completed.");
        }
        catch (Exception ex)
        {
            return Error($"Reverse broadcast on node {nodeIndex} failed: {ex.Message}");
        }
    }

    // ────────────────────────── Connection Management ──────────────────────────

    [Description("Removes a connection between two slots by their runtime IDs.")]
    private async Task<string> DisconnectSlotsById(
        [Description("Runtime ID of the sender slot.")] string senderSlotId,
        [Description("Runtime ID of the receiver slot.")] string receiverSlotId,
        CancellationToken cancellationToken = default)
    {
        if (FindComponentById(senderSlotId) is not IWorkflowSlotViewModel sender) return Error($"Sender slot '{senderSlotId}' not found.");
        if (FindComponentById(receiverSlotId) is not IWorkflowSlotViewModel receiver) return Error($"Receiver slot '{receiverSlotId}' not found.");

        if (Tree.LinksMap.TryGetValue(sender, out var dic) && dic.TryGetValue(receiver, out var link))
        {
            var completion = WaitForExitedAsync(link.DeleteCommand, cancellationToken);
            link.DeleteCommand.Execute(null);
            await completion.ConfigureAwait(false);
            return Ok($"Disconnected {senderSlotId}→{receiverSlotId}.");
        }
        return Error("No connection found between the specified slots.");
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

    [Description("Gets runtime ID of a slot inside SlotEnumerator by condition value")]
    private string GetEnumSlotByValue(
        [Description("Node index")] int nodeIndex,
        [Description("SlotEnumerator property name")] string propertyName,
        [Description("Condition value: enum name or True/False")] string conditionValue)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error)) return error;
        var prop = node!.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null || !IsSlotEnumeratorProperty(prop.PropertyType, out _))
            return Error($"'{propertyName}' is not SlotEnumerator on node [{nodeIndex}]");

        var enumerator = prop.GetValue(node);
        if (enumerator == null) return Error($"SlotEnumerator '{propertyName}' is null");

        var selectorType = enumerator.GetType().GetProperty("SelectorType")?.GetValue(enumerator) as Type;
        if (selectorType == null)
            return Error($"No SelectorType set. Call SetEnumSlotCollection first");

        object? value;
        if (selectorType == typeof(bool))
        {
            if (conditionValue.Equals("True", StringComparison.OrdinalIgnoreCase)) value = true;
            else if (conditionValue.Equals("False", StringComparison.OrdinalIgnoreCase)) value = false;
            else return Error($"Invalid bool: '{conditionValue}'. Use True/False");
        }
        else if (selectorType.IsEnum)
        {
            try { value = Enum.Parse(selectorType, conditionValue, true); }
            catch { return Error($"'{conditionValue}' not valid for {selectorType.Name}"); }
        }
        else return Error($"Selector type {selectorType.Name} neither bool nor enum");

        var trySelect = enumerator.GetType().GetMethod("TrySelect");
        if (trySelect == null) return Error("TrySelect not found");
        var args = new object?[] { value, null };
        if (!(bool)trySelect.Invoke(enumerator, args)!)
            return Error($"'{conditionValue}' not found in SlotEnumerator");
        if (args[1] is not IWorkflowSlotViewModel slot) return Error("TrySelect returned null slot");

        return new JObject
        {
            ["ok"] = true,
            ["nodeIndex"] = nodeIndex,
            ["property"] = propertyName,
            ["condition"] = conditionValue,
            ["slotId"] = GetComponentId(slot),
            ["channel"] = slot.Channel.ToString()
        }.ToString(Formatting.None);
    }

    [Description("Sets SlotChannel of slot inside SlotEnumerator by condition value")]
    private string SetEnumSlotChannel(
        [Description("Node index")] int nodeIndex,
        [Description("SlotEnumerator property")] string propertyName,
        [Description("Condition value")] string conditionValue,
        [Description("New channel")] string channel)
    {
        var getResult = GetEnumSlotByValue(nodeIndex, propertyName, conditionValue);
        var parsed = JObject.Parse(getResult);
        if (parsed["ok"]?.Value<bool>() != true) return getResult;

        var slotId = parsed["slotId"]?.ToString();
        if (string.IsNullOrEmpty(slotId)) return Error("No slotId returned");
        if (slotId is null || FindComponentById(slotId) is not IWorkflowSlotViewModel slot) return Error($"Slot '{slotId}' not found");
        if (!Enum.TryParse<SlotChannel>(channel, true, out var ch))
            return Error($"Invalid channel '{channel}'");

        slot.SetChannelCommand.Execute(ch);
        RefreshSlotAnchorsIfEnumSlotNode((IWorkflowNodeViewModel)Tree.Nodes[nodeIndex]);
        return Ok($"Slot '{conditionValue}' in {propertyName}[{nodeIndex}] channel set to {ch}");
    }

    [Description("Connects SlotEnumerator slot (by condition) to another slot. The receiver can be a plain slot property/index OR another SlotEnumerator slot — supply receiverCondition to pick the receiver slot by its enum/bool condition value instead of by index.")]
    private async Task<string> ConnectEnumSlot(
        [Description("Sender node index")] int senderNodeIndex,
        [Description("Sender SlotEnumerator property")] string senderProperty,
        [Description("Sender condition value")] string senderCondition,
        [Description("Receiver node index")] int receiverNodeIndex,
        [Description("Receiver slot property or index. When receiverCondition is supplied this must be the SlotEnumerator property name.")] string receiverSlot,
        [Description("Optional: receiver condition value (enum name or True/False). Set this when the receiver slot also lives inside a SlotEnumerator property.")] string? receiverCondition = null,
        CancellationToken cancellationToken = default)
    {
        var senderResult = GetEnumSlotByValue(senderNodeIndex, senderProperty, senderCondition);
        var senderParsed = JObject.Parse(senderResult);
        if (senderParsed["ok"]?.Value<bool>() != true) return senderResult;

        var senderSlotId = senderParsed["slotId"]?.ToString();
        if (string.IsNullOrEmpty(senderSlotId)) return Error("No sender slotId");
        if (senderSlotId is null || FindComponentById(senderSlotId) is not IWorkflowSlotViewModel sender) return Error($"Sender '{senderSlotId}' not found");

        if (!TryGetNode(receiverNodeIndex, out var receiverNode, out var error)) return error;
        IWorkflowSlotViewModel? receiver;

        if (!string.IsNullOrEmpty(receiverCondition))
        {
            // Receiver is also a SlotEnumerator slot — resolve by condition value.
            var receiverResult = GetEnumSlotByValue(receiverNodeIndex, receiverSlot, receiverCondition!);
            var receiverParsed = JObject.Parse(receiverResult);
            if (receiverParsed["ok"]?.Value<bool>() != true) return receiverResult;
            var receiverSlotId = receiverParsed["slotId"]?.ToString();
            if (string.IsNullOrEmpty(receiverSlotId)) return Error("No receiver slotId");
            if (receiverSlotId is null || FindComponentById(receiverSlotId) is not IWorkflowSlotViewModel enumReceiver)
                return Error($"Receiver '{receiverSlotId}' not found");
            receiver = enumReceiver;
        }
        else if (int.TryParse(receiverSlot, out var receiverIndex))
        {
            if (!TryGetSlot(receiverNodeIndex, receiverIndex, out receiver, out error)) return error;
        }
        else
        {
            var prop = receiverNode!.GetType().GetProperty(receiverSlot, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !typeof(IWorkflowSlotViewModel).IsAssignableFrom(prop.PropertyType))
                return Error($"'{receiverSlot}' not a slot on node [{receiverNodeIndex}]");
            receiver = prop.GetValue(receiverNode) as IWorkflowSlotViewModel;
            if (receiver == null) return Error($"Slot '{receiverSlot}' is null");
        }

        await SendReceiveAsync(sender, receiver, cancellationToken);

        bool connected = receiver is not null && VerifyConnection(sender, receiver);
        var senderLabel = $"[{senderNodeIndex}].{senderProperty}[{senderCondition}]";
        var receiverLabel = $"[{receiverNodeIndex}].{receiverSlot}";
        if (!connected && receiver is not null)
            return ConnectionRejected(sender, receiver, senderLabel, receiverLabel);
        return Ok($"Connected {senderLabel} to {receiverLabel}");
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

    [Description("Executes ReceiveCommand on multiple nodes and WAITS for each to complete before returning. Optionally pass a parameter shared by all. Disabled by default: requires WithAllowNodeExecution(true).")]
    private async Task<string> ExecuteNodes(
        [Description("JSON array of node indices, e.g. [0,1,2].")] string nodeIndicesJson,
        [Description("Optional parameter passed to each ReceiveCommand (becomes ITaskContext.Data).")] string? parameter = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("ExecuteNodes is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        int[] indices;
        try { indices = [.. JArray.Parse(nodeIndicesJson).Select(t => t.Value<int>())]; }
        catch (Exception ex) { return Error($"Invalid JSON array: {ex.Message}"); }

        int completed = 0;
        var errors = new JArray();
        foreach (var idx in indices)
        {
            if (idx < 0 || idx >= Tree.Nodes.Count)
            {
                errors.Add($"Index {idx} out of range.");
                continue;
            }
            try
            {
                await WaitForCommandAsync(Tree.Nodes[idx].ReceiveCommand, new TaskContext(data: parameter), cancellationToken).ConfigureAwait(false);
                completed++;
            }
            catch (Exception ex)
            {
                errors.Add($"Node {idx}: {ex.Message}");
            }
        }

        var result = new JObject { ["status"] = "ok", ["completed"] = completed };
        if (errors.Count > 0) result["errors"] = errors;
        return result.ToString(Formatting.None);
    }

    // ────────────────────────── Chain Execution (Compiler) ──────────────────────────

    /// <summary>
    /// Compiles the sub-graph reachable from a start node (typically a controller) and runs it
    /// through the execution engine (<see cref="CompilerEngine"/>), exactly like the demo's Run
    /// button. The engine drives the CHAIN: it injects an <see cref="IRuntimeContext"/> session into
    /// every <see cref="IRuntimeAware"/> node, selects branches via <see cref="ICompileTimeRouter"/>,
    /// and handles redirects — the node's own ReceiveAsync executes in "compiled-step" mode and does
    /// NOT auto-broadcast (the engine owns downstream dispatch). This is the chain-level entry,
    /// distinct from <see cref="ExecuteNode"/> (node-level EXEC via ReceiveCommand).
    /// </summary>
    [Description("Runs the compiled workflow (chain-level execution) from a start node, typically a controller. Compiles the reachable sub-graph, creates a runtime session (IRuntimeContext), and drives the whole chain via the execution engine — the same entry the demo's Run button uses. Nodes execute their ReceiveAsync with an IRuntimeContext (compiled-step semantics; no auto-broadcast — the engine drives the chain). Returns the session outcome: runStatus (Completed/Stopped), execution log, final data, attempts, and whether it ended with an error. DIFFERENT from ExecuteNode, which executes a single node via ReceiveCommand (node-level EXEC). Disabled by default: requires WithAllowNodeExecution(true).")]
    private async Task<string> RunCompiledWorkflow(
        [Description("Node index of the compile entry point (usually a controller).")] int startNodeIndex,
        [Description("Optional seed payload injected into the runtime session (becomes the session's Data).")] string? seed = null,
        CancellationToken cancellationToken = default)
    {
        if (!_scope.AllowNodeExecution)
            return Error("RunCompiledWorkflow is disabled by host policy. The host must enable node execution via WithAllowNodeExecution(true).");
        if (!TryGetNode(startNodeIndex, out var node, out var error)) return error;

        try
        {
            var compiler = new CompilerViewModel();
            var graphs = await compiler.CompileAsync(node!).ConfigureAwait(false);
            if (graphs.Count == 0)
                return Error("Compile produced no graphs from this start node.");

            var context = new RuntimeContext { Data = seed };
            await new CompilerEngine().RunAsync(graphs[0], context, cancellationToken).ConfigureAwait(false);

            return new JObject
            {
                ["status"] = "ok",
                ["runStatus"] = context.Status,
                ["endedWithError"] = context.EndedWithError,
                ["attempts"] = context.Attempt,
                ["data"] = context.Data is not null ? JToken.FromObject(context.Data) : JValue.CreateNull(),
                ["logs"] = new JArray(context.Logs),
            }.ToString(Formatting.None);
        }
        catch (OperationCanceledException)
        {
            return Error("Run was cancelled.");
        }
        catch (Exception ex)
        {
            return Error($"Run failed: {ex.Message}");
        }
    }

    // ────────────────────────── Analytics Functions ──────────────────────────

    [Description("Gets statistics for a node: in-degree, out-degree, total connections, connected node IDs, slot utilization. Useful for understanding node importance and connectivity.")]
    private string GetNodeStatistics(
        [Description("Node index.")] int nodeIndex)
    {
        if (!TryGetNode(nodeIndex, out var node, out var error) || node is null) return error;

        int inDegree = 0;
        int outDegree = 0;
        var connectedNodeIds = new HashSet<string>();

        foreach (var slot in node.Slots)
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
        foreach (var asm in _scope.CustomerAssemblies)
            assemblies.Add(asm);

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

    // ────────────────────────── Compiler Functions ──────────────────────────

    [Description("Compiles the workflow sub-graph reachable from a start node (typically the controller/entry node) and returns the compiled plan: execution entries, routing branches with their options/skipped/terminal flags, fan-out groups, and every compile-aware node's Order / ChainIndex / Offset. Order = -1 means the node is on a pruned static branch — absolute stop (do NOT drive it as part of the live chain). Compiling also attaches compile identity to nodes (updates IsCompileStopped badges). Use GetCompileStatus afterwards to read the identity without recompiling.")]
    private async Task<string> CompileWorkflow(
        [Description("Node index of the compile entry point (usually the controller/entry node).")] int startNodeIndex,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNode(startNodeIndex, out var node, out var error)) return error;
        try
        {
            var compiler = new CompilerViewModel();
            var graphs = await compiler.CompileAsync(node!).ConfigureAwait(false);

            var entries = new JArray();
            foreach (var g in graphs)
                AppendGraphEntries(entries, g, 0);

            return new JObject
            {
                ["status"] = "ok",
                ["graphCount"] = graphs.Count,
                ["entries"] = entries,
                ["nodeOrders"] = BuildCompileOrders(),
            }.ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            return Error($"Compile failed: {ex.Message}");
        }
    }

    [Description("Returns the current compile identity of every compile-aware node (Order / ChainIndex / Offset, isStopped = Order == -1) WITHOUT recompiling. Call CompileWorkflow first to populate it.")]
    private string GetCompileStatus()
    {
        var orders = BuildCompileOrders();
        return new JObject { ["status"] = "ok", ["compiledNodes"] = orders.Count, ["nodes"] = orders }.ToString(Formatting.None);
    }

    [Description("Returns the tree's aggregate execution log — the chronological record of direct (non-compiler) executions appended by nodes (e.g. '01. EXEC Load Seed'). For the compiler run-session log (with sequence numbers and WARN ⚠ / ERROR ✗ markers), use RunCompiledWorkflow's 'logs' field instead. Pure query.")]
    private string GetExecutionLog()
    {
        var logs = new JArray();
        try
        {
            // The tree's execution log is a convention-named public property on the concrete
            // tree view model (e.g. TreeViewModel.ExecutionLog). Read it if present.
            var prop = Tree.GetType().GetProperty("ExecutionLog");
            if (prop?.GetValue(Tree) is System.Collections.IEnumerable entries)
            {
                foreach (var e in entries)
                    if (e is not null) logs.Add(e.ToString());
            }
        }
        catch { /* tree exposes no ExecutionLog — return empty */ }

        return new JObject { ["status"] = "ok", ["entryCount"] = logs.Count, ["entries"] = logs }.ToString(Formatting.None);
    }

    private JArray BuildCompileOrders()
    {
        var arr = new JArray();
        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            var n = Tree.Nodes[i];
            if (n is ICompileTimeAware aware && aware.CompileContext is { } cc)
            {
                arr.Add(new JObject
                {
                    ["i"] = i,
                    ["id"] = GetComponentId(n),
                    ["t"] = n.GetType().Name,
                    ["order"] = cc.Order,
                    ["chainIndex"] = cc.ChainIndex,
                    ["offset"] = cc.Offset,
                    ["isStopped"] = cc.Order == -1,
                });
            }
        }
        return arr;
    }

    private static void AppendGraphEntries(JArray entries, CompiledGraph graph, int depth)
    {
        foreach (var entry in graph.Entries)
            AppendEntry(entries, entry, depth);
    }

    private static void AppendEntry(JArray entries, ActionEntry entry, int depth)
    {
        var obj = new JObject { ["depth"] = depth };
        switch (entry)
        {
            case ExecuteEntry exec:
                obj["type"] = "Execute";
                obj["nodes"] = new JArray(exec.Nodes.Select(n => n.GetType().Name));
                break;
            case BranchEntry branch:
                obj["type"] = "Branch";
                obj["router"] = branch.Router?.GetType().Name;
                obj["isDynamic"] = branch.IsDynamic;
                if (branch.CompileKey is { } ck) obj["compileKey"] = ck.ToString();
                var options = new JArray();
                foreach (var o in branch.Options)
                {
                    options.Add(new JObject
                    {
                        ["key"] = o.Key?.ToString(),
                        ["label"] = o.Label,
                        ["isSkipped"] = o.IsSkipped,
                        ["isTerminal"] = o.IsTerminal,
                    });
                    if (o.Graph is not null)
                        AppendGraphEntries(entries, o.Graph, depth + 1);
                }
                obj["options"] = options;
                break;
            case ParallelEntry parallel:
                obj["type"] = "Parallel";
                obj["branches"] = parallel.Branches.Count;
                foreach (var g in parallel.Branches)
                    AppendGraphEntries(entries, g, depth + 1);
                break;
            default:
                obj["type"] = entry.GetType().Name;
                break;
        }
        entries.Add(obj);
    }

    // ────────────────────────── Interaction Tools ──────────────────────────

    [Description("Presents a selection to the user and waits for their answer. Supports single-choice (default) and multi-choice mode. A free-text input field is always shown below the options so the user can type a custom response. Returns a JSON object with status, and depending on mode: 'chosen' (single) or 'chosenList' (multi), plus 'freeText'.")]
    private async Task<string> RequestSelection(
        [Description("A clear, concise prompt describing what the user needs to choose.")] string prompt,
        [Description("JSON array of option strings the user can pick from, e.g. [\"Option A\",\"Option B\"].")] string optionsJson,
        [Description("Label shown above the free-text input field. Provide this in the user's configured output language.")] string freeTextPrompt,
        [Description("When true, the user may select MULTIPLE options (checkboxes). When false (default), the user selects exactly one option (radio-buttons).")] bool allowMultiSelect = false)
    {
        string[] options;
        try { options = JsonConvert.DeserializeObject<string[]>(optionsJson) ?? []; }
        catch (Exception ex) { return Error($"Invalid options JSON: {ex.Message}"); }

        if (options.Length == 0) return Error("No options provided.");
        if (_scope.SelectionHandler == null) return Error("No SelectionHandler registered on WorkflowAgentScope.");

        var result = await _scope.SelectionHandler(prompt, options, freeTextPrompt, allowMultiSelect);
        if (result == null)
            return Error("User rejected the selection.");

        if (allowMultiSelect)
        {
            var selected = result.SelectedOptions?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
            var freeText = result.FreeTextResponse;
            return JsonConvert.SerializeObject(new
            {
                status = selected.Count > 0 || !string.IsNullOrWhiteSpace(freeText) ? "ok" : "cancelled",
                chosenList = selected,
                freeText,
            }, Formatting.None);
        }
        else
        {
            var chosen = result.SelectedOption;
            if (chosen == null && string.IsNullOrWhiteSpace(result.FreeTextResponse))
                return Error("User rejected the selection.");

            return JsonConvert.SerializeObject(new
            {
                status = "ok",
                chosen = chosen ?? result.FreeTextResponse,
                freeText = result.FreeTextResponse,
            }, Formatting.None);
        }
    }

    [Description("Requests explicit user confirmation before performing a dangerous or sensitive operation (e.g. deleting nodes, bulk mutations). The user can allow once, allow always for this session, or deny. Do NOT proceed with the operation if this tool returns denied.")]
    private async Task<string> RequestConfirmation(
        [Description("A stable, unique key identifying this operation type, e.g. 'delete-all-nodes'. Used to remember session-wide approvals.")] string operationKey,
        [Description("A human-readable description of what will happen if confirmed.")] string description)
    {
        if (_scope.ConfirmationHandler == null) return Error("No ConfirmationHandler registered on WorkflowAgentScope.");

        var allowed = await _scope.ResolveConfirmationAsync(operationKey, description);
        if (!allowed)
            return JsonConvert.SerializeObject(new { status = "denied", message = "User denied the operation. Do NOT proceed." }, Formatting.None);

        return JsonConvert.SerializeObject(new { status = "ok", message = "User confirmed. Proceed." }, Formatting.None);
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
        // Resolve IDs the same way every other tool does (GetComponentId: the component's RuntimeId,
        // provided by its Helper) so an ID returned by ListNodes/GetNodeDetail always round-trips
        // through the by-id tools.
        for (int i = 0; i < Tree.Nodes.Count; i++)
        {
            if (string.Equals(GetComponentId(Tree.Nodes[i]), runtimeId, StringComparison.Ordinal))
                return (Tree.Nodes[i], i);
        }
        return (null, -1);
    }

    private object? FindComponentById(string runtimeId)
    {
        foreach (var node in Tree.Nodes)
        {
            if (GetComponentId(node) == runtimeId)
                return node;

            var propertySlots = BuildSlotPropertyMap(node).Keys;
            foreach (var slot in node.Slots.Concat(propertySlots).Distinct())
            {
                if (GetComponentId(slot) == runtimeId)
                    return slot;
            }
        }
        foreach (var link in Tree.Links)
        {
            if (GetComponentId(link) == runtimeId)
                return link;
        }
        if (GetComponentId(Tree) == runtimeId)
            return Tree;
        return null;
    }

    private static string GetComponentId(object component)
    {
        // Convention: every workflow component's Helper provides a stable RuntimeId (all default
        // templates implement IWorkflowIdentifiable). Falling back to GetHashCode would yield a value
        // that is neither stable across runs nor meaningful to the Agent — so a missing RuntimeId is
        // an error, not something to paper over.
        if (component is IWorkflowIdentifiable identifiable)
            return identifiable.RuntimeId;
        throw new InvalidOperationException(
            $"'{component.GetType().Name}' does not implement IWorkflowIdentifiable — a stable RuntimeId (provided by the component Helper) is required.");
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
    /// Checks if a given selector type is allowed by the <see cref="SlotSelectorsAttribute"/>.
    /// Supports both <see cref="SlotSelectorsAttribute.AllowedEnumTypes"/> (Type[]) and
    /// <see cref="SlotSelectorsAttribute.AllowedEnumTypeNames"/> (string[]).
    /// Returns <c>true</c> if no constraints are specified (both arrays empty).
    /// </summary>
    private static bool IsEnumTypeAllowed(SlotSelectorsAttribute attr, Type enumType)
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
    /// Merges both <see cref="SlotSelectorsAttribute.AllowedEnumTypes"/> and
    /// <see cref="SlotSelectorsAttribute.AllowedEnumTypeNames"/>.
    /// </summary>
    private static string GetAllowedEnumTypeDisplayNames(SlotSelectorsAttribute attr)
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
            if (prop.GetValue(node) is not IList col || collectionIndex < 0 || collectionIndex >= col.Count)
                return Error($"Collection property '{propertyName}' index {collectionIndex} out of range or null.");
            if (col[collectionIndex] is not IWorkflowSlotViewModel slot2)
                return Error($"Element at [{collectionIndex}] is not a slot.");
            return JsonConvert.SerializeObject(new { status = "ok", id = GetComponentId(slot2), prop = propertyName, index = collectionIndex }, Formatting.None);
        }
        return Error($"Property '{propertyName}' is not a slot or slot collection.");
    }

    // ────────────────────────── Composite Functions (reduce round-trips) ──────────────────────────

    [Description("Connects two slots by property names on their owning nodes. No need to resolve slot IDs first. For collection properties, specify the index. Example: ConnectByProperty(senderNodeIndex: 0, senderProperty: \"OutputSlot\", receiverNodeIndex: 1, receiverProperty: \"InputSlot\").")]
    private async Task<string> ConnectByProperty(
        [Description("Sender node index.")] int senderNodeIndex,
        [Description("Sender slot property name, e.g. 'OutputSlot', 'OutputSlots'.")] string senderProperty,
        [Description("Receiver node index.")] int receiverNodeIndex,
        [Description("Receiver slot property name, e.g. 'InputSlot'.")] string receiverProperty,
        [Description("For sender collection properties, the zero-based index. Default 0.")] int senderCollectionIndex = 0,
        [Description("For receiver collection properties, the zero-based index. Default 0.")] int receiverCollectionIndex = 0,
        CancellationToken cancellationToken = default)
    {
        var senderSlot = ResolveSlotFromProperty(senderNodeIndex, senderProperty, senderCollectionIndex);
        if (senderSlot == null) return Error($"Cannot resolve sender slot: node={senderNodeIndex}, prop={senderProperty}[{senderCollectionIndex}].");
        var receiverSlot = ResolveSlotFromProperty(receiverNodeIndex, receiverProperty, receiverCollectionIndex);
        if (receiverSlot == null) return Error($"Cannot resolve receiver slot: node={receiverNodeIndex}, prop={receiverProperty}[{receiverCollectionIndex}].");

        await SendReceiveAsync(senderSlot, receiverSlot, cancellationToken);

        bool connected = VerifyConnection(senderSlot, receiverSlot);
        if (!connected)
            return ConnectionRejected(senderSlot, receiverSlot,
                $"[{senderNodeIndex}].{senderProperty}", $"[{receiverNodeIndex}].{receiverProperty}");
        return Ok($"Connected [{senderNodeIndex}].{senderProperty}→[{receiverNodeIndex}].{receiverProperty}.");
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
                ["id"] = GetComponentId(link),
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
    /// Supports direct slot properties, plain slot collections, and SlotEnumerator (IConditionalSlotProvider) properties.
    /// For SlotEnumerator properties <paramref name="collectionIndex"/> selects the slot by Items[i].Slot.
    /// Returns null if not found.
    /// </summary>
    private IWorkflowSlotViewModel? ResolveSlotFromProperty(int nodeIndex, string propertyName, int collectionIndex = 0)
    {
        if (!TryGetNode(nodeIndex, out var node, out _) || node == null) return null;
        var prop = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null || !prop.CanRead) return null;

        if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(prop.PropertyType))
            return prop.GetValue(node) as IWorkflowSlotViewModel;

        if (IsSlotEnumeratorProperty(prop.PropertyType, out _))
        {
            var enumerator = prop.GetValue(node);
            if (enumerator == null) return null;
            if (enumerator.GetType().GetProperty("Items")?.GetValue(enumerator) is not IEnumerable items) return null;
            int i = 0;
            foreach (var item in items)
            {
                if (i == collectionIndex)
                    return item?.GetType().GetProperty("Slot")?.GetValue(item) as IWorkflowSlotViewModel;
                i++;
            }
            return null;
        }

        if (IsSlotCollection(prop.PropertyType, out _))
        {
            if (prop.GetValue(node) is not IList col || collectionIndex < 0 || collectionIndex >= col.Count)
                return null;
            return col[collectionIndex] as IWorkflowSlotViewModel;
        }
        return null;
    }

    /// <summary>
    /// Raises <c>Anchor</c>/<c>Size</c> PropertyChanged so the platform's slot layout behavior
    /// re-syncs slot anchor positions after slots changed on a node with
    /// <see cref="SlotEnumerator{TSlot}"/> properties. Unlike the old ±0.5px move nudge, this is
    /// non-mutating: it produces NO undo entries and leaves the node geometry untouched.
    /// </summary>
    private static void RefreshSlotAnchors(IWorkflowNodeViewModel node)
    {
        node.OnPropertyChanged(nameof(node.Anchor));
        node.OnPropertyChanged(nameof(node.Size));
    }

    private static bool HasSlotEnumerator(IWorkflowNodeViewModel node)
    {
        return node.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => IsSlotEnumeratorProperty(p.PropertyType, out _));
    }

    /// <summary>
    /// Verifies a SlotEnumerator is actually installed on the given node before mutating it.
    /// Uninstalled enumerators make <c>SetSelector</c> a silent no-op, so we surface it as an
    /// explicit error instead (matching the framework's no-silent-failures contract).
    /// </summary>
    private static bool IsEnumeratorInstalled(object enumerator, IWorkflowNodeViewModel node)
    {
        return enumerator.GetType().GetProperty("Parent")?.GetValue(enumerator) is IWorkflowNodeViewModel parent
            && ReferenceEquals(parent, node);
    }

    /// <summary>
    /// Invokes Core's <c>IConditionalSlotProvider&lt;T&gt;.SetSelector</c> — the native channel for
    /// switching a SlotEnumerator's selector — instead of reflecting the concrete type's method.
    /// Routing through the interface keeps the call correct for any provider implementation
    /// (including custom ones with an explicit interface implementation), and is more robust under
    /// trimming/AOT than <c>GetMethod</c> on the runtime type.
    /// </summary>
    private static void InvokeSetSelector(object enumerator, object? selector)
    {
        var type = enumerator.GetType();
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IConditionalSlotProvider<>))
            {
                var setSelector = iface.GetMethod("SetSelector");
                if (setSelector is null)
                    throw new InvalidOperationException("IConditionalSlotProvider<T> does not expose SetSelector.");
                setSelector.Invoke(enumerator, [selector]);
                return;
            }
        }
        throw new InvalidOperationException($"'{type.FullName}' does not implement IConditionalSlotProvider<T>.");
    }

    private static void RefreshSlotAnchorsIfEnumSlotNode(IWorkflowNodeViewModel node)
    {
        if (HasSlotEnumerator(node))
            RefreshSlotAnchors(node);
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

    /// <summary>
    /// Dispatches a command and waits until it actually completes. <c>VeloxCommand.ExecuteAsync</c>
    /// is fire-and-forget, so without this the Agent could never observe when node work really finished.
    /// Throws on failure (or cancellation) so the caller can return a structured error.
    /// </summary>
    private static async Task WaitForCommandAsync(IVeloxCommand command, object? parameter, CancellationToken ct)
    {
        Exception? failure = null;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            CommandEventHandler onExited = _ => tcs.TrySetResult(null);
            CommandEventHandler onFailed = e => failure = e.Exception;
            command.Exited += onExited;
            command.Failed += onFailed;
            try
            {
                await command.ExecuteAsync(parameter).ConfigureAwait(false);
                await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                command.Exited -= onExited;
                command.Failed -= onFailed;
            }
        }
        if (failure is not null)
            throw failure;
    }

    /// <summary>
    /// Subscribes to a command's <c>Exited</c>/<c>Failed</c> and returns a task that completes when the
    /// NEXT dispatch finishes. Call this BEFORE dispatching (e.g. inside a Submit redo closure) so the
    /// completion is observable; awaiting afterwards guarantees the mutation has actually been applied
    /// before the tool returns (no stale-state window for the next tool call).
    /// </summary>
    private static async Task WaitForExitedAsync(IVeloxCommand command, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            CommandEventHandler onExited = _ => tcs.TrySetResult(null);
            CommandEventHandler onFailed = e => tcs.TrySetException(e.Exception);
            command.Exited += onExited;
            command.Failed += onFailed;
            try
            {
                await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                command.Exited -= onExited;
                command.Failed -= onFailed;
            }
        }
    }

    /// <summary>
    /// Subscribes to a shared command's <c>Exited</c> and returns a task that completes once
    /// <paramref name="count"/> dispatches have finished. Use for commands dispatched multiple times
    /// per tool call (e.g. the tree's CreateNodeCommand / SendConnectionCommand / ReceiveConnectionCommand),
    /// where <see cref="WaitForExitedAsync"/> cannot map a handler to one specific dispatch.
    /// </summary>
    private static Task WaitForNDispatchesAsync(IVeloxCommand command, int count, CancellationToken ct)
    {
        if (count <= 0) return Task.CompletedTask;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int remaining = count;
        CommandEventHandler onExited = _ =>
        {
            if (Interlocked.Decrement(ref remaining) == 0)
                tcs.TrySetResult(null);
        };
        CommandEventHandler onFailed = e => tcs.TrySetException(e.Exception);
        command.Exited += onExited;
        command.Failed += onFailed;
        var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task.ContinueWith(
            _ =>
            {
                command.Exited -= onExited;
                command.Failed -= onFailed;
                registration.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Dispatches the tree's Send then Receive connection commands and awaits both completions, so a
    /// connection tool returns only after the connection is actually created.
    /// </summary>
    private async Task SendReceiveAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, CancellationToken ct)
    {
        var sendCompletion = WaitForExitedAsync(Tree.SendConnectionCommand, ct);
        var recvCompletion = WaitForExitedAsync(Tree.ReceiveConnectionCommand, ct);
        Tree.SendConnectionCommand.Execute(sender);
        Tree.ReceiveConnectionCommand.Execute(receiver);
        await Task.WhenAll(sendCompletion, recvCompletion).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a set of anchor changes as a SINGLE undoable action and waits until the anchors are
    /// actually applied. A human performs a layout gesture once, so the Agent tool must produce exactly
    /// one undo entry for the whole layout. Nodes whose anchor already equals the target are excluded,
    /// so an alignment/layout that doesn't actually move anything does not create a no-op undo entry.
    /// </summary>
    private static string Ok(string message) => JsonConvert.SerializeObject(new { status = "ok", message }, Formatting.None);
    private static string Error(string message) => JsonConvert.SerializeObject(new { status = "error", message }, Formatting.None);
}
