using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

public class WorkflowAgentScope(IWorkflowTreeViewModel tree) : IAgentToolCallNotifier
{
    public IWorkflowTreeViewModel Tree { get; } = tree;

    public int? MaxToolCalls { get; private set; }

    public bool AutoMarkDirty { get; private set; }

    public event EventHandler<AgentToolCallEventArgs>? ToolCalled;

    private const string SystemName = "Workflow";

    internal static readonly Type[] FrameworkEnums =
        [typeof(SlotChannel), typeof(SlotState)];

    internal static bool IsFrameworkEnum(Type t) => FrameworkEnums.Contains(t);

    private static readonly Type[] FrameworkInterfaces =
        [typeof(IWorkflowTreeViewModel), typeof(IWorkflowNodeViewModel), typeof(IWorkflowSlotViewModel), typeof(IWorkflowLinkViewModel), typeof(IWorkflowViewModel)];

    private static readonly Type[] FrameworkComponents =
        [typeof(TreeViewModelBase), typeof(NodeViewModelBase), typeof(SlotViewModelBase), typeof(LinkViewModelBase)];

    internal static readonly Type[] FrameworkData =
        [typeof(Anchor), typeof(Offset), typeof(Size)];

    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerEnums = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerInterfaces = [];
    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerComponents = [];

    private readonly Dictionary<AgentLanguages, HashSet<Type>> CustomerData = [];

    private readonly List<AITool> _customTools = [];
    private readonly StringBuilder _customToolPrompt = new();

    private AgentLanguages _defaultLanguage = AgentLanguages.English;

    /// <summary>
    /// Tools registered by the developer via <see cref="WithTools"/>.
    /// </summary>
    internal IReadOnlyList<AITool> CustomTools => _customTools;

    /// <summary>
    /// Sets the global default language used when a per-call <c>language</c> argument is <c>null</c>.
    /// Call this once at the start of the fluent chain before any <c>With*</c> registration.
    /// </summary>
    public WorkflowAgentScope WithLanguage(AgentLanguages language)
    {
        _defaultLanguage = language;
        return this;
    }

    private AgentLanguages Resolve(AgentLanguages? language) => language ?? _defaultLanguage;

    public WorkflowAgentScope WithMaxToolCalls(int maxCalls)
    {
        MaxToolCalls = maxCalls;
        return this;
    }

    /// <summary>
    /// Registers custom <see cref="AITool"/> instances that will be merged into the
    /// tool list returned by <see cref="ProvideTools"/>. Use <paramref name="promptContext"/> to inject
    /// additional instructions into the system prompt so the Agent knows when and how to use them.
    /// Pass <c>null</c> if the tool metadata (name + description) is self-explanatory.
    /// </summary>
    /// <param name="promptContext">Optional prompt text describing custom tools.</param>
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
    /// Configures whether every mutation tool call automatically marks the workflow tree as dirty.
    /// When enabled, the Agent may rely on framework-managed dirty marking.
    /// When disabled (the default), the framework does not auto-mark dirty and the Agent is not instructed to call <c>MarkDirty</c>.
    /// </summary>
    public WorkflowAgentScope WithAutoMarkDirty(bool enabled = false)
    {
        AutoMarkDirty = enabled;
        return this;
    }

    public WorkflowAgentScope WithToolCallCallback(EventHandler<AgentToolCallEventArgs> handler)
    {
        ToolCalled += handler;
        return this;
    }

    // ── Interactive handlers ────────────────────────────────────────────────

    // Delegate signatures for the two interaction patterns.
    // null means the tool is not available (tool won't be registered).
    internal Func<string, string[], Task<string?>>? SelectionHandler { get; private set; }
    internal Func<string, string, Task<AgentConfirmationResult>>? ConfirmationHandler { get; private set; }

    /// <summary>
    /// Backing set for "always allow in this session" confirmations.
    /// Keyed by the <c>operationKey</c> the Agent supplies.
    /// </summary>
    private readonly HashSet<string> _sessionAllowedOperations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Controls how aggressively the Agent uses <c>RequestSelection</c> and <c>RequestConfirmation</c>.
    /// <list type="bullet">
    ///   <item><b>0 — Silent</b>: Never interact. Act autonomously on best-guess; skip both tools entirely.</item>
    ///   <item><b>1 — Cautious (default)</b>: Ask only when intent is genuinely ambiguous or the action is bulk/destructive.</item>
    ///   <item><b>2 — Balanced</b>: Ask whenever there are multiple plausible paths OR the action touches ≥ 2 nodes/links.</item>
    ///   <item><b>3 — Strict</b>: Ask before every mutation that is not a pure single-node creation. Gate all destructive actions unconditionally.</item>
    /// </list>
    /// Valid range: 0–3. Values outside this range are clamped.
    /// </summary>
    private int _interactionSafety = 1;

    /// <summary>
    /// Custom prompt text per safety level (1–3). Level 0 is always the built-in silent rule.
    /// Key = level (1/2/3), Value = full body text to embed in the "Interaction Safety Policy" section.
    /// When a level has no entry the built-in default text is used.
    /// </summary>
    private readonly Dictionary<int, string> _safetyPromptOverrides = [];

    /// <summary>
    /// Sets the interaction safety level (0–3) that governs how often the Agent pauses
    /// to ask the user via <c>RequestSelection</c> or <c>RequestConfirmation</c>.
    /// Higher values make the Agent more conservative and user-driven.
    /// </summary>
    public WorkflowAgentScope WithInteractionSafety(int level)
    {
        _interactionSafety = Math.Max(0, Math.Min(3, level));
        return this;
    }

    /// <summary>
    /// Overrides the prompt body text injected into the system prompt for the specified safety level (1–3).
    /// Level 0 always uses the built-in silent rule and cannot be overridden.
    /// The <paramref name="promptBody"/> replaces the entire body of the
    /// "Interaction Safety Policy" section for that level; the heading and footer are still generated automatically.
    /// Call multiple times to configure several levels independently.
    /// </summary>
    /// <param name="level">Safety level to override (1, 2, or 3).</param>
    /// <param name="promptBody">Full body text for that level, written in the language your Agent understands.</param>
    public WorkflowAgentScope WithInteractionSafetyPrompt(int level, string promptBody)
    {
        if (level < 1 || level > 3) return this;
        _safetyPromptOverrides[level] = promptBody ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Registers an asynchronous handler for the <c>RequestSelection</c> tool.
    /// The handler receives a <c>prompt</c> and an array of <c>options</c> and must
    /// return the chosen option string, or <c>null</c> if the user rejected the selection.
    /// </summary>
    public WorkflowAgentScope WithSelectionHandler(Func<string, string[], Task<string?>> handler)
    {
        SelectionHandler = handler;
        return this;
    }

    /// <summary>
    /// Registers an asynchronous handler for the <c>RequestConfirmation</c> tool.
    /// The handler receives an <c>operationKey</c> (stable identifier) and a human-readable
    /// <c>description</c>, and must return an <see cref="AgentConfirmationResult"/>.
    /// </summary>
    public WorkflowAgentScope WithConfirmationHandler(Func<string, string, Task<AgentConfirmationResult>> handler)
    {
        ConfirmationHandler = handler;
        return this;
    }

    /// <summary>
    /// Called by <c>WorkflowAgentToolkit.RequestConfirmation</c>.
    /// Returns <c>true</c> when the operation is allowed (either session-wide or one-time).
    /// Persists session-wide approvals automatically.
    /// </summary>
    internal async Task<bool> ResolveConfirmationAsync(string operationKey, string description)
    {
        if (_sessionAllowedOperations.Contains(operationKey))
            return true;

        if (ConfirmationHandler is null)
            return false;

        var result = await ConfirmationHandler(operationKey, description);
        if (result == AgentConfirmationResult.AllowAlways)
            _sessionAllowedOperations.Add(operationKey);

        return result != AgentConfirmationResult.Deny;
    }

    internal void RaiseToolCalled(string toolName, string result, int callCount)
    {
        ToolCalled?.Invoke(this, new AgentToolCallEventArgs(toolName, result, callCount));
    }

    // ── Interaction safety prompt ───────────────────────────────────────────

    private string BuildInteractionSafetyPrompt(AgentLanguages language)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Interaction Safety Policy");
        sb.AppendLine();

        if (_interactionSafety == 0)
        {
            // Level 0: fully autonomous — no override allowed, no markdown file needed.
            if (language == AgentLanguages.Chinese)
            {
                sb.AppendLine("**第 0 挡 — 完全自主**");
                sb.AppendLine();
                sb.AppendLine("在任何情况下，**不得**调用 `RequestSelection` 或 `RequestConfirmation`。");
                sb.AppendLine("始终基于对用户意图的最佳判断立即行动。");
                sb.AppendLine("若意图不明确，选择最保守的有效动作并直接执行，无需询问。");
            }
            else
            {
                sb.AppendLine("**Level 0 — Fully Autonomous**");
                sb.AppendLine();
                sb.AppendLine("Do **NOT** call `RequestSelection` or `RequestConfirmation` under any circumstances.");
                sb.AppendLine("Always act immediately on your best interpretation of the user's intent.");
                sb.AppendLine("If the intent is ambiguous, pick the most conservative valid action and proceed without asking.");
            }
        }
        else
        {
            // ── Shared gate (levels 1–3): loaded from embedded Safety/Shared.md ──
            var shared = AgentEmbeddedResources.ReadSafety(SystemName, "Shared", language);
            if (!string.IsNullOrWhiteSpace(shared))
                sb.AppendLine(shared.TrimEnd());

            sb.AppendLine();

            // ── Per-level rules: loaded from embedded Safety/Level{n}.md ────────
            var levelFile = AgentEmbeddedResources.ReadSafety(SystemName, $"Level{_interactionSafety}", language);
            if (!string.IsNullOrWhiteSpace(levelFile))
                sb.AppendLine(levelFile.TrimEnd());

            // ── Host-supplied additive overrides ─────────────────────────────────
            if (_safetyPromptOverrides.TryGetValue(_interactionSafety, out var custom) && !string.IsNullOrWhiteSpace(custom))
            {
                sb.AppendLine();
                if (language == AgentLanguages.Chinese)
                    sb.AppendLine("#### 宿主自定义附加规则（优先级高于上述所有默认规则）");
                else
                    sb.AppendLine("#### Host-Configured Additional Rules (take priority over all defaults above)");
                sb.AppendLine(custom.TrimEnd());
            }
        }

        sb.AppendLine();
        if (language == AgentLanguages.Chinese)
            sb.AppendLine($"> 当前安全挡位：**第 {_interactionSafety} 挡**（由宿主通过 `WithInteractionSafety({_interactionSafety})` 设置）。");
        else
            sb.AppendLine($"> Active safety level: **{_interactionSafety}** (set by the host via `WithInteractionSafety({_interactionSafety})`).");
        return sb.ToString();
    }

    public WorkflowAgentScope WithEnums(Type[] enums, AgentLanguages? language = null)
    {
        var lang = Resolve(language);
        if (CustomerEnums.TryGetValue(lang, out var set))
        {
            foreach (var item in enums) set.Add(item);
        }
        else
        {
            CustomerEnums[lang] = [.. enums];
        }
        return this;
    }

    public WorkflowAgentScope WithInterfaces(Type[] interfaces, AgentLanguages? language = null)
    {
        var lang = Resolve(language);
        if (CustomerInterfaces.TryGetValue(lang, out var set))
        {
            foreach (var item in interfaces) set.Add(item);
        }
        else
        {
            CustomerInterfaces[lang] = [.. interfaces];
        }
        return this;
    }

    public WorkflowAgentScope WithComponents(Type[] components, AgentLanguages? language = null)
    {
        var lang = Resolve(language);
        if (CustomerComponents.TryGetValue(lang, out var set))
        {
            foreach (var item in components) set.Add(item);
        }
        else
        {
            CustomerComponents[lang] = [.. components];
        }
        return this;
    }

    /// <summary>
    /// Registers value-object / data types (e.g. custom Anchor-like structs, size records)
    /// so the Agent understands their structure as plain data, not as interactive components.
    /// </summary>
    public WorkflowAgentScope WithData(Type[] dataTypes, AgentLanguages? language = null)
    {
        var lang = Resolve(language);
        if (CustomerData.TryGetValue(lang, out var set))
        {
            foreach (var item in dataTypes) set.Add(item);
        }
        else
        {
            CustomerData[lang] = [.. dataTypes];
        }
        return this;
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> and automatically registers all workflow-related types,
    /// then deeply inspects each discovered component to infer additional related types.
    /// <list type="bullet">
    ///   <item>Concrete workflow component classes �� <see cref="WithComponents"/>.</item>
    ///   <item>Enum types referenced by <c>[SlotSelectors]</c> or any property/field/parameter �� <see cref="WithEnums"/>.</item>
    ///   <item>Interface types used as property/field types on any component �� <see cref="WithInterfaces"/>.</item>
    ///   <item>Custom parameter types referenced by <c>[AgentCommandParameter]</c> on any method �� <see cref="WithData"/>.</item>
    ///   <item>Non-workflow classes/structs decorated with <c>[AgentContext]</c> �� <see cref="WithData"/>.</item>
    ///   <item>Non-primitive value-object structs found on component properties �� <see cref="WithData"/>.</item>
    /// </list>
    /// Already-registered types and framework built-in types are never re-added.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="language">Override language for this call; if <c>null</c> the global default set by <see cref="WithLanguage"/> is used.</param>
    public WorkflowAgentScope WithAutoDiscovery(Assembly assembly, AgentLanguages? language = null)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));
        var lang = Resolve(language);

        var workflowBase = typeof(IWorkflowViewModel);
        var nodeBase     = typeof(IWorkflowNodeViewModel);
        var slotBase     = typeof(IWorkflowSlotViewModel);
        var linkBase     = typeof(IWorkflowLinkViewModel);
        var treeBase     = typeof(IWorkflowTreeViewModel);

        // Pass 1: register components and [AgentContext] data types
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            bool isWorkflowComponent = nodeBase.IsAssignableFrom(type)
                || slotBase.IsAssignableFrom(type)
                || linkBase.IsAssignableFrom(type)
                || treeBase.IsAssignableFrom(type);

            if (isWorkflowComponent)
                WithComponents([type], lang);
            else if (type.GetCustomAttributes<AgentContextAttribute>().Any())
                WithData([type], lang);
        }

        // Pass 2: deep-scan every registered component to infer Enums / Interfaces / Data
        var registeredComponents = CustomerComponents.TryGetValue(lang, out var cs) ? cs : (IEnumerable<Type>)[];
        foreach (var type in registeredComponents.ToArray())
            ScanComponentMembers(type, lang, workflowBase);

        return this;
    }

    /// <summary>
    /// Scans <paramref name="assemblyName"/> and automatically registers all workflow-related types.
    /// </summary>
    /// <param name="assemblyName">Simple name of the assembly to scan (e.g. <c>"Lib"</c>).</param>
    /// <param name="language">Override language for this call; if <c>null</c> the global default set by <see cref="WithLanguage"/> is used.</param>
    public WorkflowAgentScope WithAutoDiscovery(string assemblyName, AgentLanguages? language = null)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);
        return assembly == null
            ? throw new ArgumentException($"Assembly '{assemblyName}' not found in current AppDomain.", nameof(assemblyName))
            : WithAutoDiscovery(assembly, language);
    }

    private void ScanComponentMembers(Type type, AgentLanguages lang, Type workflowBase)
    {
        const BindingFlags allInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // --- Properties ---
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Prefer attributes declared on implemented interfaces when present (interface attributes are authoritative).
            SlotSelectorsAttribute? selAttr = prop.GetCustomAttribute<SlotSelectorsAttribute>();
            AgentCommandParameterAttribute? paramAttr = prop.GetCustomAttribute<AgentCommandParameterAttribute>();

            if (selAttr == null || paramAttr == null)
            {
                foreach (var iface in type.GetInterfaces())
                {
                    var ip = iface.GetProperty(prop.Name);
                    if (ip == null) continue;
                    selAttr ??= ip.GetCustomAttribute<SlotSelectorsAttribute>();
                    paramAttr ??= ip.GetCustomAttribute<AgentCommandParameterAttribute>();
                    if (selAttr != null && paramAttr != null) break;
                }
            }

            CollectSlotSelectorTypes(selAttr, lang);
            if (paramAttr?.ParameterType is { } ppt)
                TryRegisterData(ppt, lang, workflowBase);
            TryRegisterMemberType(prop.PropertyType, lang, workflowBase);
        }

        // --- Fields (backing fields of [VeloxProperty]; attributes allowed there too) ---
        foreach (var field in type.GetFields(allInstance))
        {
            CollectSlotSelectorTypes(field.GetCustomAttribute<SlotSelectorsAttribute>(), lang);
            if (field.GetCustomAttribute<AgentCommandParameterAttribute>()?.ParameterType is { } fpt)
                TryRegisterData(fpt, lang, workflowBase);
            TryRegisterMemberType(field.FieldType, lang, workflowBase);
        }

        // --- Methods: [AgentCommandParameter] + declared parameter types ---
        foreach (var method in type.GetMethods(allInstance))
        {
            if (method.GetCustomAttribute<AgentCommandParameterAttribute>()?.ParameterType is { } mpt)
                TryRegisterData(mpt, lang, workflowBase);
            foreach (var p in method.GetParameters())
                TryRegisterMemberType(p.ParameterType, lang, workflowBase);
        }
    }

    private void CollectSlotSelectorTypes(SlotSelectorsAttribute? sel, AgentLanguages lang)
    {
        if (sel == null) return;
        foreach (var et in sel.AllowedEnumTypes)
            TryRegisterEnum(et, lang);
        // String-based constructor: AllowedEnumTypes is empty; resolve names best-effort
        if (sel.AllowedEnumTypes.Length == 0)
        {
            foreach (var name in sel.AllowedEnumTypeNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var resolved = Type.GetType(name, throwOnError: false);
                if (resolved != null)
                    TryRegisterEnum(resolved, lang);
            }
        }
    }

    private void TryRegisterMemberType(Type type, AgentLanguages lang, Type workflowBase)
    {
        type = UnwrapGeneric(type);

        if (type == null || type == typeof(object) || type == typeof(string) || type.IsPrimitive)
            return;

        if (workflowBase.IsAssignableFrom(type))
            return;   // workflow components already handled in Pass 1

        if (IsFrameworkBuiltin(type))
            return;

        if (type.IsEnum)      { TryRegisterEnum(type, lang);               return; }
        if (type.IsInterface) { TryRegisterInterface(type, lang);           return; }
        if (type.IsValueType) { TryRegisterData(type, lang, workflowBase); return; }

        // Reference class carrying [AgentContext] �� Data
        if (type.GetCustomAttributes<AgentContextAttribute>().Any())
            TryRegisterData(type, lang, workflowBase);
    }

    private static Type UnwrapGeneric(Type type)
    {
        if (!type.IsGenericType) return type;

        var def  = type.GetGenericTypeDefinition();
        var args = type.GetGenericArguments();

        if (args.Length != 1) return type;

        var defName = def.FullName ?? string.Empty;
        if (def == typeof(Nullable<>)
            || defName == "System.Collections.Generic.IEnumerable`1"
            || defName == "System.Collections.Generic.IList`1"
            || defName == "System.Collections.Generic.ICollection`1"
            || defName == "System.Collections.Generic.IReadOnlyList`1"
            || defName == "System.Collections.Generic.IReadOnlyCollection`1"
            || defName == "System.Collections.Generic.List`1"
            || defName == "System.Threading.Tasks.Task`1"
            || defName == "System.Threading.Tasks.ValueTask`1")
        {
            return UnwrapGeneric(args[0]);
        }

        return type;
    }

    private static bool IsFrameworkBuiltin(Type type)
    {
        if (FrameworkEnums.Contains(type)) return true;
        if (FrameworkData.Contains(type)) return true;
        foreach (var fi in FrameworkInterfaces) if (fi == type) return true;
        foreach (var fc in FrameworkComponents) if (fc == type) return true;
        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("System") || ns.StartsWith("Microsoft") || ns.StartsWith("VeloxDev.WorkflowSystem");
    }

    private void TryRegisterEnum(Type type, AgentLanguages lang)
    {
        if (!type.IsEnum || IsFrameworkBuiltin(type)) return;
        if (CustomerEnums.TryGetValue(lang, out var set) && set.Contains(type)) return;
        WithEnums([type], lang);
    }

    private void TryRegisterInterface(Type type, AgentLanguages lang)
    {
        if (!type.IsInterface || IsFrameworkBuiltin(type)) return;
        if (CustomerInterfaces.TryGetValue(lang, out var set) && set.Contains(type)) return;
        WithInterfaces([type], lang);
    }

    private void TryRegisterData(Type type, AgentLanguages lang, Type workflowBase)
    {
        if (type.IsPrimitive || type == typeof(string) || type == typeof(object)) return;
        if (type.IsEnum || type.IsInterface || IsFrameworkBuiltin(type)) return;
        if (workflowBase.IsAssignableFrom(type)) return;
        if (CustomerComponents.TryGetValue(lang, out var cs) && cs.Contains(type)) return;
        if (CustomerData.TryGetValue(lang, out var ds) && ds.Contains(type)) return;
        WithData([type], lang);
    }

    /// <summary>
    /// Provides full context using the global default language set by <see cref="WithLanguage"/>.
    /// </summary>
    public string ProvideAllContexts() => ProvideAllContexts(_defaultLanguage);

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
        result.AppendLine("## Framework Data Types");
        result.AppendLine();
        result.AppendLine(ProvideFrameworkDataContext(language));
        result.AppendLine();
        result.AppendLine("## Customer Context");
        result.AppendLine();
        result.AppendLine(ProvideCustomerContext(language));
        result.AppendLine();
        result.AppendLine("## Customer Data Types");
        result.AppendLine();
        result.AppendLine(ProvideCustomerDataContext(language));
        result.AppendLine();

        // ── Interaction Safety Policy ──
        result.AppendLine(BuildInteractionSafetyPrompt(language));

        // ── Built-in Skills ──
        result.AppendLine(AgentEmbeddedResources.ReadAllSkills(SystemName, language).TrimEnd());
        result.AppendLine();
        return result.ToString();
    }

    /// <summary>
    /// Provides a minimal system prompt for progressive context disclosure.
    /// Only includes a brief overview and instructs the Agent to use
    /// GetWorkflowSummary / GetComponentContext / ListComponentCommands
    /// to discover details on demand, reducing initial token overhead.
    /// </summary>
    /// <summary>Provides a progressive context prompt using the global default language set by <see cref="WithLanguage"/>.</summary>
    public string ProvideProgressiveContextPrompt() => ProvideProgressiveContextPrompt(_defaultLanguage);

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
        foreach (var t in FrameworkData)
            result.AppendLine($"- `{t.FullName}` (framework data)");
        foreach (var kvp in CustomerEnums)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer enum)");
        }
        foreach (var kvp in CustomerInterfaces)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer interface)");
        }
        foreach (var kvp in CustomerComponents)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer component)");
        }
        foreach (var kvp in CustomerData)
        {
            foreach (var t in kvp.Value)
                result.AppendLine($"- `{t.FullName}` (customer data)");
        }

        result.AppendLine();
        result.AppendLine("## 📋 Pre-loaded Component Descriptions (from [AgentContext])");
        result.AppendLine();
        result.AppendLine("The following developer instructions are **AUTHORITATIVE** for each customer component type.");
        result.AppendLine("They define intended usage and semantics. Treat them as ground truth.");
        result.AppendLine("Call **GetComponentContext** with any type name to retrieve the full property/command table on demand.");
        result.AppendLine();
        AppendPreloadedComponentSummaries(result, language);
        result.AppendLine();
        result.AppendLine("> Call **GetComponentContext** with any type name above to get full documentation including all properties and commands.");
        result.AppendLine();

        // ── Interaction Safety Policy ──
        result.AppendLine(BuildInteractionSafetyPrompt(language));

        // ── Built-in Skills ──
        result.AppendLine(AgentEmbeddedResources.ReadAllSkills(SystemName, language).TrimEnd());
        result.AppendLine();
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

    private void AppendPreloadedComponentSummaries(StringBuilder result, AgentLanguages language)
    {
        void AppendSummary(Type t, AgentLanguages lang)
        {
            var classContexts = AgentContextCollector.GetAgentContext(t, lang);
            result.AppendLine($"### `{t.FullName}`");
            result.AppendLine();
            if (classContexts.Length > 0)
            {
                result.AppendLine("**Developer Instructions:**");
                foreach (var ctx in classContexts)
                    result.AppendLine($"- {ctx}");
            }
            else
            {
                result.AppendLine("*(no developer instructions)*");
            }
            result.AppendLine();
        }

        foreach (var kvp in CustomerEnums)
            foreach (var t in kvp.Value) AppendSummary(t, kvp.Key);
        foreach (var kvp in CustomerInterfaces)
            foreach (var t in kvp.Value) AppendSummary(t, kvp.Key);
        foreach (var kvp in CustomerComponents)
            foreach (var t in kvp.Value) AppendSummary(t, kvp.Key);
        foreach (var kvp in CustomerData)
            foreach (var t in kvp.Value) AppendSummary(t, kvp.Key);
    }

    /// <summary>
    /// All mode: full context for framework-built-in data types (Anchor, Offset, Size).
    /// </summary>
    public string ProvideFrameworkDataContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();
        foreach (var t in FrameworkData)
            result.AppendLine(AgentContextCollector.GetDataContext(t, language));
        return result.ToString();
    }

    /// <summary>
    /// All mode: full context for developer-registered data types.
    /// </summary>
    public string ProvideCustomerDataContext(AgentLanguages language = AgentLanguages.English)
    {
        var result = new StringBuilder();
        foreach (var kvp in CustomerData)
            foreach (var t in kvp.Value)
                result.AppendLine(AgentContextCollector.GetDataContext(t, kvp.Key));
        return result.ToString();
    }
}
