using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public class WorkflowAgentScope(IWorkflowTreeViewModel tree)
{
    public IWorkflowTreeViewModel Tree { get; } = tree;

    /// <summary>
    /// Maximum number of tool calls allowed per agent session. <c>null</c> means unlimited.
    /// </summary>
    public int? MaxToolCalls { get; private set; }

    /// <summary>
    /// Callback invoked after each tool call. Parameters: tool name, result string, current call count.
    /// </summary>
    public Action<string, string, int>? OnToolCalled { get; private set; }

    private static readonly Type[] FrameworkEnums =
        [typeof(SlotChannel), typeof(SlotState)];

    private static readonly Type[] FrameworkInterfaces =
        [typeof(IWorkflowTreeViewModel), typeof(IWorkflowNodeViewModel), typeof(IWorkflowSlotViewModel), typeof(IWorkflowLinkViewModel), typeof(IWorkflowViewModel)];

    private static readonly Type[] FrameworkComponents =
        [typeof(TreeViewModelBase), typeof(NodeViewModelBase), typeof(SlotViewModelBase), typeof(LinkViewModelBase),
         typeof(Anchor),typeof(Offset),typeof(Size)
        ];

    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerEnums = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerInterfaces = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerComponents = [];

    public WorkflowAgentScope WithMaxToolCalls(int maxCalls)
    {
        MaxToolCalls = maxCalls;
        return this;
    }

    public WorkflowAgentScope WithToolCallCallback(Action<string, string, int> callback)
    {
        OnToolCalled = callback;
        return this;
    }

    public WorkflowAgentScope WithEnums(AgentLanguages language, params Type[] enums)
    {
        if (CustomerEnums.TryGetValue(language, out var set))
        {
            foreach (var item in enums)
            {
                set.Add(item);
            }
        }
        else
        {
            CustomerEnums[language] = [.. enums];
        }
        return this;
    }

    public WorkflowAgentScope WithInterfaces(AgentLanguages language, params Type[] interfaces)
    {
        if (CustomerInterfaces.TryGetValue(language, out var set))
        {
            foreach (var item in interfaces)
            {
                set.Add(item);
            }
        }
        else
        {
            CustomerInterfaces[language] = [.. interfaces];
        }
        return this;
    }

    public WorkflowAgentScope WithComponents(AgentLanguages language, params Type[] components)
    {
        if (CustomerComponents.TryGetValue(language, out var set))
        {
            foreach (var item in components)
            {
                set.Add(item);
            }
        }
        else
        {
            CustomerComponents[language] = [.. components];
        }
        return this;
    }

    public string ProvideAllContexts(AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine($"# Workflow Agent Context");
        result.AppendLine();
        result.AppendLine("> Agent can learn about the structure of the Workflow Framework and how to Takeover a workflow system with Takeover Protocol.");
        result.AppendLine("> Agent can read source code from https://github.com/Axvser/VeloxDev");
        result.AppendLine();
        result.AppendLine("## ⚠️ Command-First Rule");
        result.AppendLine();
        result.AppendLine("**All mutations to the workflow MUST go through commands (IVeloxCommand).** Do NOT call Helper methods directly or set command-backed properties via reflection.");
        result.AppendLine("Commands handle UI thread dispatching, undo/redo tracking, and view synchronization. Bypassing them causes invisible changes.");
        result.AppendLine();
        result.AppendLine("## 🚫 Forbidden Operations");
        result.AppendLine();
        result.AppendLine("The following properties are **framework-managed** and must NEVER be set or patched directly:");
        result.AppendLine();
        result.AppendLine("| Property | Reason | Correct Approach |");
        result.AppendLine("|---|---|---|");
        result.AppendLine("| `Parent` | Auto-set when node/slot is added to tree/node | Use CreateNode / CreateSlotOnNode |");
        result.AppendLine("| `Nodes`, `Links`, `LinksMap` | Tree collections managed by framework | Use CreateNode, ConnectSlots, DeleteNode |");
        result.AppendLine("| `Slots` | Node slot collection managed by framework | Use CreateSlotOnNode, DeleteSlot |");
        result.AppendLine("| `Targets`, `Sources` | Slot connection collections managed by framework | Use ConnectSlots, DisconnectSlots |");
        result.AppendLine("| `State` | Slot state managed by connection lifecycle | Automatic |");
        result.AppendLine("| `VirtualLink` | Tree internal for connection preview | Never touch |");
        result.AppendLine("| `RuntimeId` | Immutable identity | Read-only |");
        result.AppendLine("| `Helper` | Internal framework plumbing | Never touch |");
        result.AppendLine("| `Anchor` (on Slot) | Slot anchor is computed by view layout | Never set on slots |");
        result.AppendLine();
        result.AppendLine("### Source-Generator Managed Slot Properties");
        result.AppendLine();
        result.AppendLine("Node types may declare typed slot properties (e.g. `InputSlot`, `OutputSlot`) using `[VeloxProperty]`.");
        result.AppendLine("These slots are **auto-created by the source generator** via lazy initialization + `CreateSlotCommand`.");
        result.AppendLine("Do NOT assign, replace, or create these slots manually — they are fully lifecycle-managed.");
        result.AppendLine("Only create slots dynamically via **CreateSlotOnNode** when the node type does NOT define them as typed properties.");
        result.AppendLine();
        result.AppendLine("## AgentContext Property Rule");
        result.AppendLine();
        result.AppendLine("Properties annotated with `[AgentContext]` are **explicitly intended by the developer for Agent read/write**.");
        result.AppendLine("- If such a property has NO backing command (e.g. `Title`, `DelayMilliseconds`), use **PatchNodeProperties** / **PatchComponentById** to set it directly.");
        result.AppendLine("- If such a property has a backing command (e.g. `Size` → `SetSizeCommand`), use the corresponding tool (e.g. **ResizeNode** or **CreateNode** with width/height).");
        result.AppendLine("- The developer's [AgentContext] description may include default values (e.g. \"默认大小为 200*100\"). Respect and use these values to satisfy user requests.");
        result.AppendLine("- **BEFORE creating or configuring a component type for the first time**, call **GetComponentContext** to read these annotations.");
        result.AppendLine();
        result.AppendLine("## Framework Context");
        result.AppendLine();
        result.AppendLine(ProvideFrameworkContext(language));
        result.AppendLine();
        result.AppendLine("## Customer Context");
        result.AppendLine();
        result.AppendLine(ProvideCustomerContext(language));

        return result.ToString();
    }

    /// <summary>
    /// Provides a minimal system prompt for progressive context disclosure.
    /// Only includes a brief overview and instructs the Agent to use
    /// GetWorkflowSummary / GetComponentContext / ListComponentCommands
    /// to discover details on demand, reducing initial token overhead.
    /// </summary>
    public string ProvideProgressiveContextPrompt(AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("# Workflow Agent Context (Progressive)");
        result.AppendLine();
        result.AppendLine("> You are an Agent that fully operates a visual workflow editor via tools.");
        result.AppendLine();
        result.AppendLine("## ⚠️ Command-First Rule (CRITICAL)");
        result.AppendLine();
        result.AppendLine("**All mutations MUST go through commands.** Never bypass the command pipeline.");
        result.AppendLine();
        result.AppendLine("| Operation | Tool | Command Used |");
        result.AppendLine("|---|---|---|");
        result.AppendLine("| Move node | MoveNode | MoveCommand |");
        result.AppendLine("| Position node | SetNodePosition | SetAnchorCommand |");
        result.AppendLine("| Resize node | ResizeNode | SetSizeCommand |");
        result.AppendLine("| Create node | CreateNode | Tree.CreateNodeCommand |");
        result.AppendLine("| Create slot | CreateSlotOnNode | Node.CreateSlotCommand |");
        result.AppendLine("| Connect | ConnectSlots / ConnectSlotsById | Tree.Send/ReceiveConnectionCommand |");
        result.AppendLine("| Disconnect | DisconnectSlots | Link.DeleteCommand |");
        result.AppendLine("| Delete node | DeleteNode | Node.DeleteCommand |");
        result.AppendLine("| Delete slot | DeleteSlot | Slot.DeleteCommand |");
        result.AppendLine("| Broadcast | BroadcastNode | Node.BroadcastCommand |");
        result.AppendLine("| Any other | ExecuteCommandOnNode / ExecuteCommandById | Resolved by name |");
        result.AppendLine("| Patch custom props | PatchNodeProperties / PatchComponentById | Direct (non-command props only) |");
        result.AppendLine();
        result.AppendLine("## 🚫 Forbidden Operations");
        result.AppendLine();
        result.AppendLine("NEVER set these framework-managed properties via PatchNodeProperties or any other means:");
        result.AppendLine("- **Parent** — auto-set by framework when added to tree/node");
        result.AppendLine("- **Nodes/Links/LinksMap/Slots/Targets/Sources** — collections managed by framework commands");
        result.AppendLine("- **State** (on slots) — managed by connection lifecycle");
        result.AppendLine("- **VirtualLink/Helper/RuntimeId** — framework internals, never touch");
        result.AppendLine("- **Anchor on Slot** — computed by view layout, never set manually");
        result.AppendLine("- **Typed slot properties** (e.g. InputSlot, OutputSlot on nodes) — auto-created by source generator, never assign or replace");
        result.AppendLine("- Only use **CreateSlotOnNode** for dynamically-added slots when the node type does NOT define them as typed properties");
        result.AppendLine();
        result.AppendLine("## Token-Saving Tips");
        result.AppendLine();
        result.AppendLine("- **BatchExecute**: combine multiple operations in one call to save round-trips.");
        result.AppendLine("- **TakeSnapshot** returns only version+counts. Use **GetChangesSinceSnapshot** for diffs instead of re-reading everything.");
        result.AppendLine("- **ListNodes** returns compact JSON. Use **GetNodeDetail** only when you need slot-level info.");
        result.AppendLine("- **ListComponentCommands** is separate from GetNodeDetail — call only when discovering commands.");
        result.AppendLine("- Prefer **RuntimeId** over indices for multi-step operations (stable across add/remove).");
        result.AppendLine("- Use **ConnectSlotsById** when you already have slot IDs.");
        result.AppendLine();
        result.AppendLine("## Framework Behaviors");
        result.AppendLine();
        result.AppendLine("- **Delete cascades**: Deleting a node automatically deletes all its child slots and their connections. No need to delete slots or links individually before deleting a node.");
        result.AppendLine("- **Node size**: Newly created nodes get a default size from view rendering. You can pass optional `width`/`height` to **CreateNode** to override the default, or use **ResizeNode** later.");
        result.AppendLine("- **CloneNodes**: Use CloneNodes to duplicate a set of nodes (with internal connections) to a new position. Provide node indices/IDs and an offset.");
        result.AppendLine();
        result.AppendLine("## Discovery Flow");
        result.AppendLine();
        result.AppendLine("1. **GetWorkflowSummary** → orient (counts + types)");
        result.AppendLine("2. **GetComponentContext** → **BEFORE creating or configuring a component**, call this to read the developer's [AgentContext] descriptions. These describe default sizes, property semantics, and intended usage. This is essential context.");
        result.AppendLine("3. **ListNodes** → compact list with IDs");
        result.AppendLine("4. **GetNodeDetail(ById)** → slot details for specific node");
        result.AppendLine("5. **ListComponentCommands** → discover commands before executing");
        result.AppendLine();
        result.AppendLine("## 🔧 Node Creation Protocol");
        result.AppendLine();
        result.AppendLine("When the user asks to create a node (e.g. \"在原点创建一个节点\"), follow these steps **in order**:");
        result.AppendLine();
        result.AppendLine("1. **If you have never seen the available component types**, call **GetWorkflowSummary** to discover all registered node types.");
        result.AppendLine("2. **Choose the most appropriate node type** from the registered customer component types. If only one node type exists, use it. If multiple exist, pick the one that best fits the user's intent, or ask the user to clarify.");
        result.AppendLine("3. **If you have never read the [AgentContext] for that type**, call **GetComponentContext** with its fully-qualified type name. This returns the developer's descriptions including default size, property meanings, and intended defaults.");
        result.AppendLine("4. **Apply defaults from [AgentContext]**: If the context says \"默认大小为 200*100\", use `width=200, height=100` in CreateNode. If the user explicitly specifies different values, use the user's values instead.");
        result.AppendLine("5. **Call CreateNode** with the resolved type, position, and size.");
        result.AppendLine("6. **Set AgentContext-described properties** via PatchNodeProperties if the defaults differ from what the user wants.");
        result.AppendLine();
        result.AppendLine("**Key principle**: Defaults from [AgentContext] are the baseline. User instructions override them. Never ignore documented defaults; never ask the user for information that [AgentContext] already provides.");
        result.AppendLine();
        result.AppendLine("⚠️ **GetTypeSchema `defaultJson` shows runtime zero-initialized values (e.g. Size={0,0}).** These are NOT the intended defaults. The authoritative defaults are in **GetComponentContext → Developer Instructions**. Always prefer Developer Instructions over `defaultJson`.");
        result.AppendLine();
        result.AppendLine("## AgentContext Property Rule");
        result.AppendLine();
        result.AppendLine("Properties annotated with `[AgentContext]` are **explicitly intended by the developer for Agent read/write**.");
        result.AppendLine("- If such a property has NO backing command (e.g. `Title`, `DelayMilliseconds`), use **PatchNodeProperties** / **PatchComponentById** to set it directly.");
        result.AppendLine("- If such a property has a backing command (e.g. `Size` → `SetSizeCommand`), use the corresponding tool (e.g. **ResizeNode** or **CreateNode** with width/height).");
        result.AppendLine("- The developer's [AgentContext] description may include default values (e.g. \"默认大小为 200*100\"). Use these to fulfill user requests (e.g. \"random size\" means generate values, not ignore size).");
        result.AppendLine();
        result.AppendLine("## Context Caching");
        result.AppendLine();
        result.AppendLine("You do NOT need to call GetWorkflowSummary or GetComponentContext every turn. Once you have read a type's context, remember it for the rest of the conversation. Only re-read if the user indicates types have changed.");
        result.AppendLine();

        // Include only the registered type names so the agent knows what to query
        result.AppendLine("## Registered Component Types");
        result.AppendLine();
        foreach (var t in FrameworkInterfaces)
            result.AppendLine($"- `{t.FullName}` (framework interface)");
        foreach (var t in FrameworkComponents)
            result.AppendLine($"- `{t.FullName}` (framework base class)");
        foreach (var kvp in CustomerComponents)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer component)");
        }
        result.AppendLine();
        result.AppendLine("> Call **GetComponentContext** with any type name above to get full documentation.");

        return result.ToString();
    }

    public string ProvideFrameworkContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();

        foreach (var framework in FrameworkEnums)
        {
            result.AppendLine(AgentContextCollector.GetEnumContext(framework, language));
        }
        foreach (var framework in FrameworkInterfaces)
        {
            result.AppendLine(AgentContextCollector.GetInterfaceContext(framework, language));
        }
        foreach (var framework in FrameworkComponents)
        {
            result.AppendLine(AgentContextCollector.GetClassContext(framework, language));
        }

        return result.ToString();
    }

    public string ProvideCustomerContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();

        foreach (var kvp in CustomerEnums)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetEnumContext(framework, kvp.Key));
            }
        }
        foreach (var kvp in CustomerInterfaces)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetInterfaceContext(framework, kvp.Key));
            }
        }
        foreach (var kvp in CustomerComponents)
        {
            foreach (var framework in kvp.Value)
            {
                result.AppendLine(AgentContextCollector.GetClassContext(framework, kvp.Key));
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Creates a <see cref="WorkflowAgentToolkit"/> that provides MAF-compatible
    /// <see cref="AITool"/> instances for full operational control over the scoped tree.
    /// </summary>
    public WorkflowAgentToolkit CreateToolkit() => new(this);

    /// <summary>
    /// Convenience method: creates the toolkit and returns all tools ready for use
    /// with <c>ChatOptions.Tools</c> or <c>AsAIAgent(tools: ...)</c>.
    /// </summary>
    public IList<AITool> ProvideTools() => CreateToolkit().CreateTools();
}
