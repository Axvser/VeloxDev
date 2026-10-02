using Microsoft.Extensions.AI;
using VeloxDev.AI.Pipelines;
using VeloxDev.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI;

/// <summary>
/// Wraps a .NET object as a set of MAF-compatible <see cref="AITool"/> instances,
/// using <see cref="AgentPropertyAccessor"/>, <see cref="AgentMethodInvoker"/>,
/// <see cref="AgentCommandDiscoverer"/>, and <see cref="AgentContextReader"/> from Core.
/// <para>
/// This is a generic, non-workflow toolkit. For workflow-specific tools, use
/// <c>WorkflowAgentToolkit</c> instead.
/// </para>
/// <para>
/// Those helpers read a compiled context tree, so only a type the VeloxDev generator admitted — annotated,
/// or carrying an <c>ICommand</c> property, or implementing one of the four workflow component interfaces —
/// can be described or acted on. Any other object still gets the ten tools, and every one of them answers
/// that the type is not in the tree. That closed world is what makes the surface trimmable.
/// </para>
/// </summary>
/// <remarks>
/// Creates a toolkit that wraps the given target object.
/// </remarks>
/// <param name="target">The object to expose to the Agent.</param>
/// <param name="language">Language for <see cref="AgentContextAttribute"/> descriptions.</param>
/// <param name="rejectedProperties">Property names that should be rejected when patching.</param>
public sealed class AgentObjectToolkit(object target, AgentLanguages language = AgentLanguages.English, ISet<string>? rejectedProperties = null) : IAgentToolCallNotifier
{
    private readonly object _target = target ?? throw new ArgumentNullException(nameof(target));
    private readonly AgentLanguages _language = language;
    private readonly ISet<string>? _rejectedProperties = rejectedProperties;
    private int _toolCallCount;

    /// <inheritdoc />
    public event EventHandler<AgentToolCallEventArgs>? ToolCalled;

    /// <summary>
    /// Maximum number of tool calls allowed. <c>null</c> means unlimited.
    /// </summary>
    public int? MaxToolCalls { get; set; }

    /// <summary>
    /// Creates all generic AI tools for the target object.
    /// Every tool is wrapped with <see cref="TrackedAIFunction"/> so that
    /// <see cref="IAgentToolCallNotifier.ToolCalled"/> is raised after each call.
    /// </summary>
    public IList<AITool> CreateTools()
    {
        AITool T(Delegate method, string name)
            => new TrackedAIFunction(AIFunctionFactory.Create(method, name), Tools, Pipeline);

        return
        [
            T(GetComponentInfo, nameof(GetComponentInfo)),
            T(ListProperties, nameof(ListProperties)),
            T(GetProperty, nameof(GetProperty)),
            T(SetProperty, nameof(SetProperty)),
            T(PatchProperties, nameof(PatchProperties)),
            T(ListCommands, nameof(ListCommands)),
            T(ExecuteCommand, nameof(ExecuteCommand)),
            T(ListMethods, nameof(ListMethods)),
            T(InvokeMethod, nameof(InvokeMethod)),
            T(ResolveType, nameof(ResolveType)),
        ];
    }

    // ────────────────────────── Tracking ──────────────────────────

    private AgentPipeline? _pipeline;
    private ToolPipeline? _tools;

    /// <summary>
    /// This toolkit's event chain. Exposed so a host that wants more than the call notification — the
    /// refusals and failures the wrapper now reports — can add a stage instead of being limited to the one
    /// delegate the old policy accepted.
    /// </summary>
    public AgentPipeline Pipeline => _pipeline ??= new AgentPipeline().Use(new CountingStage(this));

    /// <summary>
    /// The tool seam handed to every <see cref="TrackedAIFunction"/> this toolkit creates: one global call
    /// ceiling, and one place the calls are reported.
    /// <para>
    /// It registers no thread marshalling on purpose. The wrapped target is an arbitrary object rather
    /// than a UI-bound component, so there is no thread it must run on; a host that needs one can set
    /// <see cref="ToolPipeline.MarshalTo"/> before creating the tools.
    /// </para>
    /// </summary>
    public ToolPipeline Tools => _tools ??= new ToolPipeline()
    {
        Refuse = _ => MaxToolCalls.HasValue && _toolCallCount >= MaxToolCalls.Value
            ? $"Tool call limit ({MaxToolCalls.Value}) exceeded. No further tool calls are allowed."
            : null,
    };

    /// <summary>
    /// Counts completed calls and raises <see cref="ToolCalled"/>.
    /// <para>
    /// Counts successes only, which is what the old <c>AfterCall</c> hook saw: a caller that wants the
    /// refusals and failures too now has them on the pipeline, which is the point of the events carrying
    /// an outcome rather than the absence of a call meaning "failed".
    /// </para>
    /// </summary>
    private sealed class CountingStage(AgentObjectToolkit owner) : IAgentPipelineStage
    {
        public ValueTask OnEventAsync(
            AgentEvent agentEvent, Func<AgentEvent, ValueTask> next, CancellationToken cancellationToken)
        {
            if (agentEvent is AgentToolCallCompleted { Outcome: AgentToolOutcome.Succeeded } completed)
            {
                var count = Interlocked.Increment(ref owner._toolCallCount);
                owner.ToolCalled?.Invoke(owner, new AgentToolCallEventArgs(completed.ToolName, completed.Result, count));
            }

            return next(agentEvent);
        }
    }

    // ────────────────────────── Context ──────────────────────────

    [Description("Gets the type name and [AgentContext] developer descriptions for this component.")]
    private string GetComponentInfo()
    {
        var type = _target.GetType();
        var contexts = AgentContextReader.GetContexts(type, _language);
        var obj = new VeloxJsonObject
        {
            ["type"] = type.FullName,
            ["descriptions"] = ToJsonArray(contexts),
        };
        return obj.ToJson();
    }

    // ────────────────────────── Properties ──────────────────────────

    [Description("Lists all public properties on the component with types, read/write status, and [AgentContext] descriptions.")]
    private string ListProperties()
    {
        var props = AgentPropertyAccessor.DiscoverProperties(_target, _language, includeValues: true);
        var arr = new VeloxJsonArray();
        foreach (var p in props)
        {
            var obj = new VeloxJsonObject
            {
                ["name"] = p.Name,
                ["type"] = p.PropertyType,
                ["canRead"] = p.CanRead,
                ["canWrite"] = p.CanWrite,
            };
            if (p.AgentDescriptions.Count > 0)
                obj["descriptions"] = ToJsonArray(p.AgentDescriptions);
            if (p.CurrentValue != null)
            {
                try { obj["value"] = VeloxJsonValue.From(p.CurrentValue); }
                catch { obj["value"] = p.CurrentValue.ToString(); }
            }
            arr.Add(obj);
        }
        return arr.ToJson();
    }

    [Description("Gets the current value of a named property.")]
    private string GetProperty(
        [Description("Property name.")] string propertyName)
    {
        var value = AgentPropertyAccessor.GetPropertyValue(_target, propertyName);
        if (value == null) return new VeloxJsonObject { ["status"] = "ok", ["value"] = VeloxJsonValue.Null }.ToJson();
        try
        {
            return new VeloxJsonObject { ["status"] = "ok", ["value"] = VeloxJsonValue.From(value) }.ToJson();
        }
        catch
        {
            return new VeloxJsonObject { ["status"] = "ok", ["value"] = value.ToString() }.ToJson();
        }
    }

    [Description("Sets a single property by name.")]
    private string SetProperty(
        [Description("Property name.")] string propertyName,
        [Description("Value to set (as JSON token).")] string jsonValue)
    {
        object? value = ParseJsonValue(jsonValue);
        var result = AgentPropertyAccessor.SetPropertyValue(_target, propertyName, value);
        return new VeloxJsonObject { ["status"] = result.Success ? "ok" : "error", ["Error"] = result.Error }.ToJson();
    }

    [Description("Sets multiple properties at once from a JSON object. Rejected properties are skipped with an error.")]
    private string PatchProperties(
        [Description("JSON object with property names and values, e.g. '{\"Title\":\"New\",\"Count\":5}'.")] string jsonPatch)
    {
        VeloxJsonObject patch;
        try { patch = (VeloxJsonObject)VeloxJsonValue.Parse(jsonPatch); }
        catch (Exception ex) { return new VeloxJsonObject { ["status"] = "error", ["message"] = $"Invalid JSON: {ex.Message}" }.ToJson(); }

        var dict = new Dictionary<string, object?>();
        foreach (var kv in patch)
            dict[kv.Key] = kv.Value.IsNull ? null : JsonToClrValue(kv.Value);

        var results = AgentPropertyAccessor.SetProperties(_target, dict, _rejectedProperties);
        var successCount = results.Count(r => r.Success);
        var details = new VeloxJsonArray();
        foreach (var r in results)
            details.Add(new VeloxJsonObject { ["PropertyName"] = r.PropertyName, ["Success"] = r.Success, ["Error"] = r.Error });
        return new VeloxJsonObject
        {
            ["status"] = successCount > 0 ? "ok" : "error",
            ["message"] = $"{successCount}/{results.Count} properties set.",
            ["details"] = details,
        }.ToJson();
    }

    // ────────────────────────── Commands ──────────────────────────

    [Description("Lists all ICommand properties on the component with parameter types and descriptions.")]
    private string ListCommands()
    {
        var cmds = AgentCommandDiscoverer.DiscoverCommands(_target, _language);
        var arr = new VeloxJsonArray();
        foreach (var c in cmds)
        {
            var obj = new VeloxJsonObject
            {
                ["name"] = c.Name,
                ["paramType"] = c.ParameterType,
                ["canExecute"] = c.CanExecute,
            };
            if (c.AgentDescriptions.Count > 0)
                obj["descriptions"] = ToJsonArray(c.AgentDescriptions);
            arr.Add(obj);
        }
        return arr.ToJson();
    }

    [Description("Executes a named ICommand on the component. Appends 'Command' suffix automatically if missing.")]
    private string ExecuteCommand(
        [Description("Command name, e.g. 'Delete' or 'DeleteCommand'.")] string commandName,
        [Description("JSON parameter, or null.")] string? jsonParameter = null)
    {
        object? parameter = jsonParameter != null ? ParseJsonValue(jsonParameter) : null;
        var result = AgentCommandDiscoverer.Execute(_target, commandName, parameter);
        return new VeloxJsonObject { ["status"] = result.Success ? "ok" : "error", ["Error"] = result.Error }.ToJson();
    }

    // ────────────────────────── Methods ──────────────────────────

    [Description("Lists all public methods on the component (excluding property accessors and object base methods).")]
    private string ListMethods()
    {
        var methods = AgentMethodInvoker.DiscoverMethods(_target, _language);
        var arr = new VeloxJsonArray();
        foreach (var m in methods)
        {
            var obj = new VeloxJsonObject
            {
                ["name"] = m.Name,
                ["returnType"] = m.ReturnType,
                ["params"] = ToJsonArray(m.Parameters.Select(p => $"{p.ParameterType} {p.Name}{(p.IsOptional ? "?" : "")}")),
            };
            if (m.AgentDescriptions.Count > 0)
                obj["descriptions"] = ToJsonArray(m.AgentDescriptions);
            arr.Add(obj);
        }
        return arr.ToJson();
    }

    [Description("Invokes a named public method on the component with the given JSON arguments array.")]
    private string InvokeMethod(
        [Description("Method name.")] string methodName,
        [Description("JSON array of arguments, e.g. '[42, \"hello\"]'. Use '[]' for no arguments.")] string jsonArgs = "[]")
    {
        object?[] args;
        try
        {
            var arr = (VeloxJsonArray)VeloxJsonValue.Parse(jsonArgs);
            args = [.. arr.Select(t => t.IsNull ? null : JsonToClrValue(t))];
        }
        catch (Exception ex)
        {
            return new VeloxJsonObject { ["status"] = "error", ["message"] = $"Invalid args JSON: {ex.Message}" }.ToJson();
        }

        var result = AgentMethodInvoker.Invoke(_target, methodName, args);
        if (!result.Success)
            return new VeloxJsonObject { ["status"] = "error", ["Error"] = result.Error }.ToJson();

        try
        {
            return new VeloxJsonObject { ["status"] = "ok", ["returnValue"] = VeloxJsonValue.From(result.ReturnValue) }.ToJson();
        }
        catch
        {
            return new VeloxJsonObject { ["status"] = "ok", ["returnValue"] = result.ReturnValue?.ToString() }.ToJson();
        }
    }

    // ────────────────────────── Type Resolution ──────────────────────────

    [Description("Resolves a .NET type by its fully-qualified name across all loaded assemblies. Returns type info.")]
    private string ResolveType(
        [Description("Fully-qualified type name.")] string fullTypeName)
    {
        var type = AgentTypeResolver.ResolveType(fullTypeName);
        if (type == null)
            return new VeloxJsonObject { ["status"] = "error", ["message"] = $"Type '{fullTypeName}' not found." }.ToJson();

        return new VeloxJsonObject
        {
            ["status"] = "ok",
            ["fullName"] = type.FullName,
            ["kind"] = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class",
            ["baseType"] = type.BaseType?.FullName,
        }.ToJson();
    }

    // ────────────────────────── Helpers ──────────────────────────

    private static object? ParseJsonValue(string jsonValue)
    {
        try
        {
            var token = VeloxJsonValue.Parse(jsonValue);
            return token.IsNull ? null : JsonToClrValue(token);
        }
        catch
        {
            return jsonValue; // fallback: treat as raw string
        }
    }

    /// <summary>
    /// Turns a parsed JSON node into the loosely typed value the property accessor converts from: a scalar
    /// comes back as its text, which is what every conversion the accessor performs reads, and a container is
    /// handed over as the tree itself.
    /// </summary>
    private static object? JsonToClrValue(VeloxJsonValue value)
        => value is VeloxJsonScalar scalar ? (object?)scalar.Text : value;

    /// <summary>Builds a JSON array of strings.</summary>
    private static VeloxJsonArray ToJsonArray(IEnumerable<string> items)
    {
        var array = new VeloxJsonArray();
        foreach (var item in items) array.Add(VeloxJsonValue.From(item));
        return array;
    }
}

/// <summary>
/// Extension methods for creating <see cref="AgentObjectToolkit"/> from any object.
/// </summary>
public static class AgentObjectToolkitExtensions
{
    /// <summary>
    /// Creates an <see cref="AgentObjectToolkit"/> that wraps this object as a set of MAF <see cref="AITool"/> instances.
    /// </summary>
    public static AgentObjectToolkit AsAgentToolkit(
        this object target,
        AgentLanguages language = AgentLanguages.English,
        ISet<string>? rejectedProperties = null)
        => new(target, language, rejectedProperties);

    /// <summary>
    /// Convenience: creates the toolkit and returns all tools ready for use.
    /// </summary>
    public static IList<AITool> AsAgentTools(
        this object target,
        AgentLanguages language = AgentLanguages.English,
        ISet<string>? rejectedProperties = null)
        => new AgentObjectToolkit(target, language, rejectedProperties).CreateTools();
}
