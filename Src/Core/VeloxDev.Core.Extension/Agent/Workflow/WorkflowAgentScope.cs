using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public class WorkflowAgentScope(IWorkflowTreeViewModel tree) : IAgentToolCallNotifier
{
    public IWorkflowTreeViewModel Tree { get; } = tree;

    /// <summary>
    /// Maximum number of tool calls allowed per agent session. <c>null</c> means unlimited.
    /// </summary>
    public int? MaxToolCalls { get; private set; }

    /// <inheritdoc />
    public event EventHandler<AgentToolCallEventArgs>? ToolCalled;

    /// <summary>The system name used as the directory key for embedded resources under <c>Resources/Workflow/</c>.</summary>
    private const string SystemName = "Workflow";

    internal static readonly Type[] FrameworkEnums =
        [typeof(SlotChannel), typeof(SlotState)];

    /// <summary>
    /// Returns <c>true</c> for enum types that are part of the VeloxDev framework itself.
    /// Framework enums are always valid selector types regardless of <c>[SlotSelectors]</c> constraints.
    /// </summary>
    internal static bool IsFrameworkEnum(Type t) => FrameworkEnums.Contains(t);

    private static readonly Type[] FrameworkInterfaces =
        [typeof(IWorkflowTreeViewModel), typeof(IWorkflowNodeViewModel), typeof(IWorkflowSlotViewModel), typeof(IWorkflowLinkViewModel), typeof(IWorkflowViewModel)];

    private static readonly Type[] FrameworkComponents =
        [typeof(TreeViewModelBase), typeof(NodeViewModelBase), typeof(SlotViewModelBase), typeof(LinkViewModelBase),
         typeof(Anchor),typeof(Offset),typeof(Size)
        ];

    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerEnums = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerInterfaces = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerComponents = [];

    private readonly List<AITool> _customTools = [];
    private readonly StringBuilder _customToolPrompt = new();

    // ── Embedded resource registrations ──────────────────────────────────────
    private readonly List<string> _skillNames = [];
    private readonly List<string> _referenceNames = [];
    private readonly List<string> _scriptNames = [];

    /// <summary>
    /// Tools registered by the developer via <see cref="WithTools"/>.
    /// </summary>
    internal IReadOnlyList<AITool> CustomTools => _customTools;

    public WorkflowAgentScope WithMaxToolCalls(int maxCalls)
    {
        MaxToolCalls = maxCalls;
        return this;
    }

    public WorkflowAgentScope WithToolCallCallback(EventHandler<AgentToolCallEventArgs> handler)
    {
        ToolCalled += handler;
        return this;
    }

    /// <summary>
    /// Raises the <see cref="ToolCalled"/> event. Called by <see cref="WorkflowAgentToolkit"/> after each tool invocation.
    /// </summary>
    internal void RaiseToolCalled(string toolName, string result, int callCount)
    {
        ToolCalled?.Invoke(this, new AgentToolCallEventArgs(toolName, result, callCount));
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

    /// <summary>
    /// Registers custom <see cref="AITool"/> instances that will be merged into the
    /// toolkit alongside the built-in workflow tools. Use this to expose
    /// domain-specific or component-specific operations to the Agent.
    /// </summary>
    /// <param name="promptContext">
    /// Optional prompt text describing the custom tools. This is appended to the
    /// system prompt so the Agent knows when and how to use them.
    /// Pass <c>null</c> if the tool metadata (name + description) is self-explanatory.
    /// </param>
    /// <param name="tools">One or more <see cref="AITool"/> instances.</param>
    public WorkflowAgentScope WithTools(string? promptContext, params AITool[] tools)
    {
        _customTools.AddRange(tools);
        if (!string.IsNullOrWhiteSpace(promptContext))
        {
            _customToolPrompt.AppendLine(promptContext);
        }
        return this;
    }

    /// <summary>
    /// Registers an embedded Skill by name (file stem without language suffix or extension),
    /// e.g. <c>"SlotEnumerator"</c> for <c>Skills/SlotEnumerator.en.md</c>.
    /// The skill content is injected into the system prompt under <c>## 🧩 Skills</c>.
    /// </summary>
    public WorkflowAgentScope WithSkill(string name)
    {
        if (!_skillNames.Contains(name))
            _skillNames.Add(name);
        return this;
    }

    /// <summary>
    /// Registers an embedded Reference by name (file stem without language suffix or extension),
    /// e.g. <c>"CoordinateSystem"</c> for <c>References/CoordinateSystem.en.md</c>.
    /// The reference content is injected into the system prompt under <c>## 📚 References</c>.
    /// </summary>
    public WorkflowAgentScope WithReference(string name)
    {
        if (!_referenceNames.Contains(name))
            _referenceNames.Add(name);
        return this;
    }

    /// <summary>
    /// Registers an embedded Script by its full file name (including extension),
    /// e.g. <c>"MyWorkflow.md"</c> for <c>Scripts/MyWorkflow.md</c>.
    /// The script content is injected into the system prompt under <c>## 🔧 Scripts</c>.
    /// </summary>
    public WorkflowAgentScope WithScript(string name)
    {
        if (!_scriptNames.Contains(name))
            _scriptNames.Add(name);
        return this;
    }

    public string ProvideAllContexts(AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("# Workflow Agent Context");
        result.AppendLine();
        result.AppendLine("> Agent can learn about the structure of the Workflow Framework and how to Takeover a workflow system with Takeover Protocol.");
        result.AppendLine("> Agent can read source code from https://github.com/Axvser/VeloxDev");
        result.AppendLine();

        // ── Built-in References ──
        result.AppendLine(AgentEmbeddedResources.ReadAllReferences(SystemName, language).TrimEnd());
        result.AppendLine();

        result.AppendLine("## Framework Context");
        result.AppendLine();
        result.AppendLine(ProvideFrameworkContext(language));
        result.AppendLine();
        result.AppendLine("## Customer Context");
        result.AppendLine();
        result.AppendLine(ProvideCustomerContext(language));
        result.AppendLine();

        // ── Built-in Skills ──
        result.AppendLine(AgentEmbeddedResources.ReadAllSkills(SystemName, language).TrimEnd());
        result.AppendLine();

        // ── Custom Tools Prompt ──
        if (_customToolPrompt.Length > 0)
        {
            result.AppendLine();
            result.AppendLine("## 🔌 Custom Tools (Developer-Registered)");
            result.AppendLine();
            result.AppendLine(_customToolPrompt.ToString().TrimEnd());
        }

        // ── Developer-registered Resources (Skills / References / Scripts) ──
        AppendEmbeddedResources(result, language);

        return result.ToString();
    }

    /// <summary>
    /// Provides a minimal system prompt for progressive context disclosure.
    /// Only includes a brief overview and instructs the Agent to use
    /// GetWorkflowSummary / GetComponentContext / ListComponentCommands
    /// to discover details on demand, reducing initial token overhead.
    /// </summary>
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

        // ── Built-in References ──
        result.AppendLine(AgentEmbeddedResources.ReadAllReferences(SystemName, language).TrimEnd());
        result.AppendLine();

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
        result.AppendLine("## 📋 Pre-loaded Component Descriptions (from [AgentContext])");
        result.AppendLine();
        result.AppendLine("The following developer instructions are **AUTHORITATIVE** for each customer component type.");
        result.AppendLine("They define default sizes, property semantics, and intended usage. Treat them as ground truth.");
        result.AppendLine("You already have this information — do NOT call GetComponentContext again for these types unless specifically needing full property tables.");
        result.AppendLine();
        AppendPreloadedComponentDescriptions(result, language);
        result.AppendLine();
        result.AppendLine("> Call **GetComponentContext** with any type name above to get full documentation.");
        result.AppendLine();

        // ── Built-in Skills ──
        result.AppendLine(AgentEmbeddedResources.ReadAllSkills(SystemName, language).TrimEnd());
        result.AppendLine();

        // ── Custom Tools Prompt ──
        if (_customToolPrompt.Length > 0)
        {
            result.AppendLine();
            result.AppendLine("## 🔌 Custom Tools (Developer-Registered)");
            result.AppendLine();
            result.AppendLine(_customToolPrompt.ToString().TrimEnd());
        }

        // ── Developer-registered Resources (Skills / References / Scripts) ──
        AppendEmbeddedResources(result, language);

        return result.ToString();
    }

    /// <summary>
    /// Appends the <c>## 🧩 Skills</c>, <c>## 📚 References</c>, and <c>## 🔧 Scripts</c>
    /// sections to <paramref name="sb"/> from the embedded resource registrations made via
    /// <see cref="WithSkill"/>, <see cref="WithReference"/>, and <see cref="WithScript"/>.
    /// Sections with no registered content are omitted.
    /// </summary>
    private void AppendEmbeddedResources(StringBuilder sb, AgentLanguages language)
    {
        if (_skillNames.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## \ud83e\udde9 Skills");
            foreach (var name in _skillNames)
            {
                var content = AgentEmbeddedResources.ReadSkill(SystemName, name, language);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine();
                    sb.AppendLine(content!.TrimEnd());
                }
            }
        }

        if (_referenceNames.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## \ud83d\udcda References");
            foreach (var name in _referenceNames)
            {
                var content = AgentEmbeddedResources.ReadReference(SystemName, name, language);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine();
                    sb.AppendLine(content!.TrimEnd());
                }
            }
        }

        if (_scriptNames.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## \ud83d\udd27 Scripts");
            foreach (var name in _scriptNames)
            {
                var content = AgentEmbeddedResources.ReadScript(SystemName, name);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine();
                    sb.AppendLine(content!.TrimEnd());
                }
            }
        }
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

    /// <summary>
    /// Appends a condensed description block for each registered customer component,
    /// including class-level [AgentContext] instructions and per-property [AgentContext]
    /// annotations. This gives the Agent immediate knowledge of defaults and semantics
    /// without needing a tool call.
    /// </summary>
    private void AppendPreloadedComponentDescriptions(StringBuilder result, AgentLanguages language)
    {
        // Customer enums
        foreach (var kvp in CustomerEnums)
        {
            foreach (var t in kvp.Value)
                AppendTypeQuickRef(result, t, kvp.Key);
        }
        // Customer interfaces
        foreach (var kvp in CustomerInterfaces)
        {
            foreach (var t in kvp.Value)
                AppendTypeQuickRef(result, t, kvp.Key);
        }
        // Customer components (most important — these carry default sizes, property descriptions)
        foreach (var kvp in CustomerComponents)
        {
            foreach (var t in kvp.Value)
                AppendTypeQuickRef(result, t, kvp.Key);
        }
    }

    /// <summary>
    /// Appends a quick-reference block for a single type: class-level descriptions,
    /// plus [AgentContext]-annotated property summaries.
    /// </summary>
    private static void AppendTypeQuickRef(StringBuilder result, Type type, AgentLanguages language)
    {
        var classContexts = AgentContextCollector.GetAgentContext(type, language);

        result.AppendLine($"### `{type.FullName}`");
        result.AppendLine();

        if (classContexts.Length > 0)
        {
            result.AppendLine("**Developer Instructions:**");
            foreach (var ctx in classContexts)
                result.AppendLine($"- {ctx}");
            result.AppendLine();
        }

        // Collect property-level [AgentContext] from fields (source-generated [VeloxProperty] backing fields)
        // and public properties
        var entries = new List<(string name, string typeName, string[] descriptions)>();

        foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
        {
            var descs = AgentContextCollector.GetAgentContext(field, language);
            if (descs.Length == 0) continue;

            // Derive public property name from backing field
            var name = field.Name.TrimStart('_');
            if (name.Length > 0) name = char.ToUpper(name[0]) + name.Substring(1);
            entries.Add((name, field.FieldType.Name, descs));
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip if already added from backing field
            if (entries.Any(e => e.name == prop.Name)) continue;

            var descs = AgentContextCollector.GetAgentContext(prop, language);
            if (descs.Length == 0)
            {
                // Auto-inject synthetic description for SlotEnumerator<TSlot> properties
                if (IsSlotEnumeratorType(prop.PropertyType))
                {
                    var synth = language == AgentLanguages.Chinese
                        ? "SlotEnumerator — 通过 SetEnumSlotCollection 工具配置选择器类型（枚举或 bool），禁止手动增删"
                        : "SlotEnumerator — use SetEnumSlotCollection tool to configure the selector type (enum or bool). Do not add/remove slots manually.";
                    entries.Add((prop.Name, prop.PropertyType.Name, new[] { synth }));
                }
                continue;
            }
            entries.Add((prop.Name, prop.PropertyType.Name, descs));
        }

        if (entries.Count > 0)
        {
            result.AppendLine("| Property | Type | Description |");
            result.AppendLine("|---|---|---|");
            foreach (var (name, typeName, descs) in entries)
            {
                var descText = string.Join("; ", descs);
                result.AppendLine($"| {name} | {typeName} | {descText} |");
            }
            result.AppendLine();
        }
    }

    private static bool IsSlotEnumeratorType(Type type)
    {
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def.Name.StartsWith("SlotEnumerator`") && def.Namespace == "VeloxDev.WorkflowSystem";
    }
}
