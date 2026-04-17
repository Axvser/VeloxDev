using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI;

/// <summary>
/// Wraps any .NET object as a set of MAF-compatible <see cref="AITool"/> instances,
/// using <see cref="AgentPropertyAccessor"/>, <see cref="AgentMethodInvoker"/>,
/// <see cref="AgentCommandDiscoverer"/>, and <see cref="AgentContextReader"/> from Core.
/// <para>
/// This is a generic, non-workflow toolkit. For workflow-specific tools, use
/// <c>WorkflowAgentToolkit</c> instead.
/// </para>
/// </summary>
public sealed class AgentObjectToolkit : IAgentToolCallNotifier
{
    private readonly object _target;
    private readonly AgentLanguages _language;
    private readonly ISet<string>? _rejectedProperties;
    private int _toolCallCount;

    /// <inheritdoc />
    public event EventHandler<AgentToolCallEventArgs>? ToolCalled;

    /// <summary>
    /// Maximum number of tool calls allowed. <c>null</c> means unlimited.
    /// </summary>
    public int? MaxToolCalls { get; set; }

    /// <summary>
    /// Creates a toolkit that wraps the given target object.
    /// </summary>
    /// <param name="target">The object to expose to the Agent.</param>
    /// <param name="language">Language for <see cref="AgentContextAttribute"/> descriptions.</param>
    /// <param name="rejectedProperties">Property names that should be rejected when patching.</param>
    public AgentObjectToolkit(object target, AgentLanguages language = AgentLanguages.English, ISet<string>? rejectedProperties = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _language = language;
        _rejectedProperties = rejectedProperties;
    }

    /// <summary>
    /// Creates all generic AI tools for the target object.
    /// Every tool is wrapped with <see cref="TrackedAIFunction"/> so that
    /// <see cref="IAgentToolCallNotifier.ToolCalled"/> is raised after each call.
    /// </summary>
    public IList<AITool> CreateTools()
    {
        AITool T(Delegate method, string name)
            => new TrackedAIFunction(AIFunctionFactory.Create(method, name), this);

        return new List<AITool>
        {
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
        };
    }

    // ────────────────────────── Tracking ──────────────────────────

    private sealed class TrackedAIFunction : DelegatingAIFunction
    {
        private readonly AgentObjectToolkit _toolkit;

        public TrackedAIFunction(AIFunction inner, AgentObjectToolkit toolkit) : base(inner)
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

    private void Track(string toolName, string result)
    {
        var count = Interlocked.Increment(ref _toolCallCount);
        ToolCalled?.Invoke(this, new AgentToolCallEventArgs(toolName, result, count));
    }

    // ────────────────────────── Context ──────────────────────────

    [Description("Gets the type name and [AgentContext] developer descriptions for this component.")]
    private string GetComponentInfo()
    {
        var type = _target.GetType();
        var contexts = AgentContextReader.GetContexts(type, _language);
        var obj = new JObject
        {
            ["type"] = type.FullName,
            ["descriptions"] = new JArray(contexts),
        };
        return obj.ToString(Formatting.None);
    }

    // ────────────────────────── Properties ──────────────────────────

    [Description("Lists all public properties on the component with types, read/write status, and [AgentContext] descriptions.")]
    private string ListProperties()
    {
        var props = AgentPropertyAccessor.DiscoverProperties(_target, _language, includeValues: true);
        var arr = new JArray();
        foreach (var p in props)
        {
            var obj = new JObject
            {
                ["name"] = p.Name,
                ["type"] = p.PropertyType.Name,
                ["canRead"] = p.CanRead,
                ["canWrite"] = p.CanWrite,
            };
            if (p.AgentDescriptions.Count > 0)
                obj["descriptions"] = new JArray(p.AgentDescriptions.ToArray());
            if (p.CurrentValue != null)
            {
                try { obj["value"] = JToken.FromObject(p.CurrentValue); }
                catch { obj["value"] = p.CurrentValue.ToString(); }
            }
            arr.Add(obj);
        }
        return arr.ToString(Formatting.None);
    }

    [Description("Gets the current value of a named property.")]
    private string GetProperty(
        [Description("Property name.")] string propertyName)
    {
        var value = AgentPropertyAccessor.GetPropertyValue(_target, propertyName);
        if (value == null) return JsonConvert.SerializeObject(new { status = "ok", value = (object?)null });
        try
        {
            return JsonConvert.SerializeObject(new { status = "ok", value }, Formatting.None,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MaxDepth = 3 });
        }
        catch
        {
            return JsonConvert.SerializeObject(new { status = "ok", value = value.ToString() });
        }
    }

    [Description("Sets a single property by name.")]
    private string SetProperty(
        [Description("Property name.")] string propertyName,
        [Description("Value to set (as JSON token).")] string jsonValue)
    {
        object? value = ParseJsonValue(jsonValue);
        var result = AgentPropertyAccessor.SetPropertyValue(_target, propertyName, value);
        return JsonConvert.SerializeObject(new { status = result.Success ? "ok" : "error", result.Error }, Formatting.None);
    }

    [Description("Sets multiple properties at once from a JSON object. Rejected properties are skipped with an error.")]
    private string PatchProperties(
        [Description("JSON object with property names and values, e.g. '{\"Title\":\"New\",\"Count\":5}'.")] string jsonPatch)
    {
        JObject patch;
        try { patch = JObject.Parse(jsonPatch); }
        catch (Exception ex) { return JsonConvert.SerializeObject(new { status = "error", message = $"Invalid JSON: {ex.Message}" }); }

        var dict = new Dictionary<string, object?>();
        foreach (var kv in patch)
            dict[kv.Key] = kv.Value?.Type == JTokenType.Null ? null : kv.Value?.ToObject<object>();

        var results = AgentPropertyAccessor.SetProperties(_target, dict, _rejectedProperties);
        var successCount = results.Count(r => r.Success);
        return JsonConvert.SerializeObject(new
        {
            status = successCount > 0 ? "ok" : "error",
            message = $"{successCount}/{results.Count} properties set.",
            details = results.Select(r => new { r.PropertyName, r.Success, r.Error }),
        }, Formatting.None);
    }

    // ────────────────────────── Commands ──────────────────────────

    [Description("Lists all ICommand properties on the component with parameter types and descriptions.")]
    private string ListCommands()
    {
        var cmds = AgentCommandDiscoverer.DiscoverCommands(_target, _language);
        var arr = new JArray();
        foreach (var c in cmds)
        {
            var obj = new JObject
            {
                ["name"] = c.Name,
                ["paramType"] = c.ParameterType?.Name,
                ["canExecute"] = c.CanExecute,
            };
            if (c.AgentDescriptions.Count > 0)
                obj["descriptions"] = new JArray(c.AgentDescriptions.ToArray());
            arr.Add(obj);
        }
        return arr.ToString(Formatting.None);
    }

    [Description("Executes a named ICommand on the component. Appends 'Command' suffix automatically if missing.")]
    private string ExecuteCommand(
        [Description("Command name, e.g. 'Delete' or 'DeleteCommand'.")] string commandName,
        [Description("JSON parameter, or null.")] string? jsonParameter = null)
    {
        object? parameter = jsonParameter != null ? ParseJsonValue(jsonParameter) : null;
        var result = AgentCommandDiscoverer.Execute(_target, commandName, parameter);
        return JsonConvert.SerializeObject(new { status = result.Success ? "ok" : "error", result.Error }, Formatting.None);
    }

    // ────────────────────────── Methods ──────────────────────────

    [Description("Lists all public methods on the component (excluding property accessors and object base methods).")]
    private string ListMethods()
    {
        var methods = AgentMethodInvoker.DiscoverMethods(_target, _language);
        var arr = new JArray();
        foreach (var m in methods)
        {
            var obj = new JObject
            {
                ["name"] = m.Name,
                ["returnType"] = m.ReturnType.Name,
                ["params"] = new JArray(m.Parameters.Select(p => $"{p.ParameterType.Name} {p.Name}{(p.IsOptional ? "?" : "")}").ToArray()),
            };
            if (m.AgentDescriptions.Count > 0)
                obj["descriptions"] = new JArray(m.AgentDescriptions.ToArray());
            arr.Add(obj);
        }
        return arr.ToString(Formatting.None);
    }

    [Description("Invokes a named public method on the component with the given JSON arguments array.")]
    private string InvokeMethod(
        [Description("Method name.")] string methodName,
        [Description("JSON array of arguments, e.g. '[42, \"hello\"]'. Use '[]' for no arguments.")] string jsonArgs = "[]")
    {
        object?[] args;
        try
        {
            var arr = JArray.Parse(jsonArgs);
            args = arr.Select(t => t.Type == JTokenType.Null ? null : t.ToObject<object>()).ToArray();
        }
        catch (Exception ex)
        {
            return JsonConvert.SerializeObject(new { status = "error", message = $"Invalid args JSON: {ex.Message}" });
        }

        var result = AgentMethodInvoker.Invoke(_target, methodName, args);
        if (!result.Success)
            return JsonConvert.SerializeObject(new { status = "error", result.Error }, Formatting.None);

        try
        {
            return JsonConvert.SerializeObject(new { status = "ok", returnValue = result.ReturnValue }, Formatting.None,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MaxDepth = 3 });
        }
        catch
        {
            return JsonConvert.SerializeObject(new { status = "ok", returnValue = result.ReturnValue?.ToString() });
        }
    }

    // ────────────────────────── Type Resolution ──────────────────────────

    [Description("Resolves a .NET type by its fully-qualified name across all loaded assemblies. Returns type info.")]
    private string ResolveType(
        [Description("Fully-qualified type name.")] string fullTypeName)
    {
        var type = AgentTypeResolver.ResolveType(fullTypeName);
        if (type == null)
            return JsonConvert.SerializeObject(new { status = "error", message = $"Type '{fullTypeName}' not found." });

        return JsonConvert.SerializeObject(new
        {
            status = "ok",
            fullName = type.FullName,
            kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class",
            baseType = type.BaseType?.FullName,
        }, Formatting.None);
    }

    // ────────────────────────── Helpers ──────────────────────────

    private static object? ParseJsonValue(string jsonValue)
    {
        try
        {
            var token = JToken.Parse(jsonValue);
            return token.Type == JTokenType.Null ? null : token.ToObject<object>();
        }
        catch
        {
            return jsonValue; // fallback: treat as raw string
        }
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
