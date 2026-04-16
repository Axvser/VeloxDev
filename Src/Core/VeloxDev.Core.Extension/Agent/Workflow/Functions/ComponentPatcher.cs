using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Input;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Applies a JSON patch object to a component instance, setting writable public
/// properties by name. Leverages the existing Newtonsoft.Json serialization
/// infrastructure from <see cref="ComponentModelEx"/>.
/// Properties that have a corresponding command (e.g. Anchor → SetAnchorCommand)
/// are rejected to enforce the command pipeline.
/// </summary>
public static class ComponentPatcher
{
    /// <summary>
    /// Properties managed by the framework (set by helpers, source generators, or command pipeline).
    /// These must NEVER be patched directly — they are either auto-assigned or have dedicated commands.
    /// </summary>
    private static readonly HashSet<string> FrameworkManagedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // Hierarchy (set by framework when adding to tree/node)
        "Parent",
        // Collections managed by framework
        "Nodes", "Links", "LinksMap", "Slots", "Targets", "Sources",
        // Framework-managed state
        "State", "VirtualLink",
        // Runtime identity
        "RuntimeId",
        // Helper (use SetHelper() method)
        "Helper",
    };

    /// <summary>
    /// Applies a JSON patch (key-value pairs) to the target object's public properties.
    /// Properties backed by commands are skipped with a hint to use the command instead.
    /// </summary>
    /// <param name="target">The object to patch.</param>
    /// <param name="jsonPatch">A JSON object string, e.g. {"Title":"New","Delay":500}.</param>
    /// <returns>A JSON result string describing successes and failures.</returns>
    public static string ApplyPatch(object target, string jsonPatch)
    {
        if (target == null)
            return JsonConvert.SerializeObject(new { status = "error", message = "Target is null." });

        JObject patch;
        try
        {
            patch = JObject.Parse(jsonPatch);
        }
        catch (Exception ex)
        {
            return JsonConvert.SerializeObject(new { status = "error", message = $"Invalid JSON: {ex.Message}" });
        }

        var type = target.GetType();
        var results = new JArray();
        int successCount = 0;

        foreach (var kv in patch)
        {
            var propName = kv.Key;
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
            {
                results.Add(new JObject { ["property"] = propName, ["status"] = "skipped", ["reason"] = prop == null ? "not found" : "read-only" });
                continue;
            }

            // Reject framework-managed properties
            if (FrameworkManagedProperties.Contains(propName))
            {
                results.Add(new JObject
                {
                    ["property"] = propName,
                    ["status"] = "rejected",
                    ["reason"] = $"'{propName}' is framework-managed. It is set automatically by the framework (helpers, source generators, or commands). Do not modify it directly.",
                });
                continue;
            }

            // Reject properties that have a corresponding command — those must go through the command pipeline
            var commandName = FindBackingCommand(type, propName);
            if (commandName != null)
            {
                results.Add(new JObject
                {
                    ["property"] = propName,
                    ["status"] = "rejected",
                    ["reason"] = $"Property '{propName}' has a backing command '{commandName}'. Use that command instead of direct property patching.",
                });
                continue;
            }

            // Reject slot-typed properties — these are auto-created by source generator
            if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(prop.PropertyType))
            {
                results.Add(new JObject
                {
                    ["property"] = propName,
                    ["status"] = "rejected",
                    ["reason"] = $"'{propName}' is a slot property managed by the source generator. It is auto-created via CreateSlotCommand. Do not assign it.",
                });
                continue;
            }

            try
            {
                var value = kv.Value?.DeserializeToType(prop.PropertyType);
                prop.SetValue(target, value);
                successCount++;
                results.Add(new JObject { ["property"] = propName, ["status"] = "ok" });
            }
            catch (Exception ex)
            {
                results.Add(new JObject { ["property"] = propName, ["status"] = "error", ["reason"] = ex.Message });
            }
        }

        return JsonConvert.SerializeObject(new
        {
            status = successCount > 0 ? "ok" : "error",
            message = $"{successCount}/{patch.Count} properties patched.",
            details = results,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Delegates to <see cref="AgentCommandDiscoverer.FindBackingCommand"/> in Core.
    /// </summary>
    private static string? FindBackingCommand(Type type, string propertyName)
        => AgentCommandDiscoverer.FindBackingCommand(type, propertyName);

    /// <summary>
    /// Copies all writable scalar (non-command-backed) properties from source to target.
    /// Both objects should be of the same type. Command-backed and ICommand properties are skipped.
    /// </summary>
    public static void CopyScalarProperties(object source, object target)
    {
        if (source == null || target == null) return;
        var type = source.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (typeof(ICommand).IsAssignableFrom(prop.PropertyType)) continue;
            if (typeof(IWorkflowSlotViewModel).IsAssignableFrom(prop.PropertyType)) continue;
            if (FrameworkManagedProperties.Contains(prop.Name)) continue;
            if (FindBackingCommand(type, prop.Name) != null) continue;

            var pt = prop.PropertyType;
            if (pt == typeof(string) || pt == typeof(int) || pt == typeof(double) || pt == typeof(bool) ||
                pt == typeof(long) || pt == typeof(float) || pt == typeof(decimal) || pt.IsEnum)
            {
                try
                {
                    prop.SetValue(target, prop.GetValue(source));
                }
                catch { /* skip inaccessible */ }
            }
        }
    }
}
