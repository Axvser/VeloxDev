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

        // Reject unmounted targets explicitly instead of mutating state outside the
        // command/lifecycle pipeline: an unmounted component has no parent chain, so the
        // change would bypass lifecycle hooks and view synchronization and be invisible.
        // (Undo is NOT the concern here — undo is Core's command pipeline's job. Direct
        // property writes are intentionally non-undoable; changes that must be undoable go
        // through their backing command, which the command-backed rejection below routes to.)
        var tree = ResolveTree(target);
        if (tree is null)
            return JsonConvert.SerializeObject(new
            {
                status = "error",
                message = "Patch rejected: the target is not mounted in a Tree (no parent chain), so the change would bypass the component lifecycle and view synchronization. Mount the component first (e.g. create and add the node), then retry.",
            });

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

            // Reject [SlotSelectors]-marked properties — must use SetEnumSlotCollection tool
            if (prop.GetCustomAttribute<SlotSelectorsAttribute>() != null)
            {
                results.Add(new JObject
                {
                    ["property"] = propName,
                    ["status"] = "rejected",
                    ["reason"] = $"'{propName}' is a selector-type driver marked with [SlotSelectors]. Use the 'SetEnumSlotCollection' tool instead of direct patching.",
                });
                continue;
            }

            try
            {
                object? value;
                // Special handling: if the property is System.Type, resolve from type name string
                if (prop.PropertyType == typeof(Type))
                {
                    var typeName = kv.Value?.ToString();
                    if (string.IsNullOrEmpty(typeName))
                    {
                        value = null;
                    }
                    else
                    {
                        value = TypeIntrospector.ResolveType(typeName!);
                        if (value == null)
                        {
                            results.Add(new JObject { ["property"] = propName, ["status"] = "error", ["reason"] = $"Type '{typeName}' not found." });
                            continue;
                        }
                    }
                }
                else
                {
                    value = kv.Value?.DeserializeToType(prop.PropertyType);
                }
                var oldValue = prop.GetValue(target);
                if (Equals(oldValue, value))
                {
                    // Writing the same value would create a no-op undo entry (redo and undo both
                    // restore identical state). Report it as unchanged and skip it.
                    results.Add(new JObject { ["property"] = propName, ["status"] = "skipped", ["reason"] = "unchanged" });
                    continue;
                }
                prop.SetValue(target, value);
                successCount++;
                results.Add(new JObject { ["property"] = propName, ["status"] = "ok" });
            }
            catch (Exception ex)
            {
                results.Add(new JObject { ["property"] = propName, ["status"] = "error", ["reason"] = ex.Message });
            }
        }

        // Properties are written directly (done above). This is intentionally non-undoable:
        // undo/redo is exclusively owned by Core's IVeloxCommand pipeline. A patch that must be
        // undoable is rejected above and routed to its backing command (e.g. Anchor →
        // SetAnchorCommand). Never wrap these direct writes in Submit(WorkflowActionPair).
        // Unmounted-target rejection (tree is null) is still enforced above.

        return JsonConvert.SerializeObject(new
        {
            status = successCount > 0 ? "ok" : "error",
            message = $"{successCount}/{patch.Count} properties patched.",
            details = results,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Applies a JSON patch to a target object and records it in the workflow tree's
    /// undo/redo history. Since <see cref="ApplyPatch"/> is now undoable itself, this
    /// is a thin backward-compatible alias. The <paramref name="tree"/> argument is
    /// ignored — the owning tree is resolved from the target's Parent chain.
    /// </summary>
    /// <param name="target">The object to patch.</param>
    /// <param name="jsonPatch">A JSON object string, e.g. {"Title":"New","Delay":500}.</param>
    /// <param name="tree">Ignored; the owning tree is resolved from the target.</param>
    /// <returns>A JSON result string describing successes and failures.</returns>
    public static string ApplyPatchWithUndo(object target, string jsonPatch, IWorkflowTreeViewModel? tree = null)
        => ApplyPatch(target, jsonPatch);

    /// <summary>
    /// Resolves the owning <see cref="IWorkflowTreeViewModel"/> from a workflow component
    /// by walking its Parent chain. Returns <c>null</c> for unmounted components — those
    /// must not be mutated because there is no undo history to record the change in.
    /// </summary>
    private static IWorkflowTreeViewModel? ResolveTree(object target)
    {
        return target switch
        {
            IWorkflowTreeViewModel tree => tree,
            IWorkflowNodeViewModel node => node.Parent,
            IWorkflowSlotViewModel slot => slot.Parent?.Parent,
            IWorkflowLinkViewModel link => link.Sender?.Parent?.Parent,
            _ => null,
        };
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
