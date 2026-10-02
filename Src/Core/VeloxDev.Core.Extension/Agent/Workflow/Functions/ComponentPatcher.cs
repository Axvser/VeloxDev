using System;
using System.Collections.Generic;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Applies a JSON patch object to a component instance, setting writable public
/// properties by name. Property values are read through the generated archive serializer.
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
            return new VeloxJsonObject { ["status"] = "error", ["message"] = "Target is null." }.ToJson();

        VeloxJsonObject patch;
        try
        {
            patch = (VeloxJsonObject)VeloxJsonValue.Parse(jsonPatch);
        }
        catch (Exception ex)
        {
            return new VeloxJsonObject { ["status"] = "error", ["message"] = $"Invalid JSON: {ex.Message}" }.ToJson();
        }

        var type = target.GetType();
        var accessor = AIContextTreeRegistry.FindAccessor(type.FullName ?? type.Name);

        // Reject unmounted targets explicitly instead of mutating state outside the
        // command/lifecycle pipeline: an unmounted component has no parent chain, so the
        // change would bypass lifecycle hooks and view synchronization and be invisible.
        // (Undo is NOT the concern here — undo is Core's command pipeline's job. Direct
        // property writes are intentionally non-undoable; changes that must be undoable go
        // through their backing command, which the command-backed rejection below routes to.)
        var tree = ResolveTree(target);
        if (tree is null)
            return new VeloxJsonObject
            {
                ["status"] = "error",
                ["message"] = "Patch rejected: the target is not mounted in a Tree (no parent chain), so the change would bypass the component lifecycle and view synchronization. Mount the component first (e.g. create and add the node), then retry.",
            }.ToJson();

        var results = new VeloxJsonArray();
        int successCount = 0;

        foreach (var kv in patch)
        {
            var propName = kv.Key;
            var prop = accessor is null ? null : AIContextDirectory.Shared.MemberAcross(accessor.TypeName, "Properties", propName);
            if (prop == null || !prop.Has(AIContextFlags.CanWrite))
            {
                results.Add(new VeloxJsonObject { ["property"] = propName, ["status"] = "skipped", ["reason"] = prop == null ? "not found" : "read-only" });
                continue;
            }

            // Reject framework-managed properties
            if (FrameworkManagedProperties.Contains(propName))
            {
                results.Add(new VeloxJsonObject
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
                results.Add(new VeloxJsonObject
                {
                    ["property"] = propName,
                    ["status"] = "rejected",
                    ["reason"] = $"Property '{propName}' has a backing command '{commandName}'. Use that command instead of direct property patching.",
                });
                continue;
            }

            // Reject slot-typed properties — these are auto-created by source generator
            if (prop.Has(AIContextFlags.IsSingleSlot))
            {
                results.Add(new VeloxJsonObject
                {
                    ["property"] = propName,
                    ["status"] = "rejected",
                    ["reason"] = $"'{propName}' is a slot property managed by the source generator. It is auto-created via CreateSlotCommand. Do not assign it.",
                });
                continue;
            }

            // Reject [SlotSelectors]-marked properties — must use SetEnumSlotCollection tool
            if (prop.Has(AIContextFlags.HasSlotSelectors))
            {
                results.Add(new VeloxJsonObject
                {
                    ["property"] = propName,
                    ["status"] = "rejected",
                    ["reason"] = $"'{propName}' is a selector-type driver marked with [SlotSelectors]. Use the 'SetEnumSlotCollection' tool instead of direct patching.",
                });
                continue;
            }

            try
            {
                // 声明的类型来自访问器的 typeof 字面量 —— 生成的序列化器要一个 Type，这里给得出，且不必反射。
                var propertyType = accessor!.MemberType(propName);
                if (propertyType is null)
                {
                    results.Add(new VeloxJsonObject { ["property"] = propName, ["status"] = "skipped", ["reason"] = "not found" });
                    continue;
                }

                object? value;
                // Special handling: if the property is System.Type, resolve from type name string
                if (propertyType == typeof(Type))
                {
                    var typeName = (kv.Value as VeloxJsonScalar)?.AsString();
                    if (string.IsNullOrEmpty(typeName))
                    {
                        value = null;
                    }
                    else
                    {
                        value = TypeIntrospector.ResolveType(typeName!);
                        if (value == null)
                        {
                            results.Add(new VeloxJsonObject { ["property"] = propName, ["status"] = "error", ["reason"] = $"Type '{typeName}' not found." });
                            continue;
                        }
                    }
                }
                else
                {
                    value = DeserializeToType(kv.Value, propertyType);
                }
                var oldValue = accessor.TryGet(target, propName, out var current) ? current : null;
                if (Equals(oldValue, value))
                {
                    // Writing the same value would create a no-op undo entry (redo and undo both
                    // restore identical state). Report it as unchanged and skip it.
                    results.Add(new VeloxJsonObject { ["property"] = propName, ["status"] = "skipped", ["reason"] = "unchanged" });
                    continue;
                }

                var error = accessor.Set(target, propName, value);
                if (error is not null)
                {
                    results.Add(new VeloxJsonObject { ["property"] = propName, ["status"] = "error", ["reason"] = error });
                    continue;
                }

                successCount++;
                results.Add(new VeloxJsonObject { ["property"] = propName, ["status"] = "ok" });
            }
            catch (Exception ex)
            {
                results.Add(new VeloxJsonObject { ["property"] = propName, ["status"] = "error", ["reason"] = ex.Message });
            }
        }

        // Properties are written directly (done above). This is intentionally non-undoable:
        // undo/redo is exclusively owned by Core's IVeloxCommand pipeline. A patch that must be
        // undoable is rejected above and routed to its backing command (e.g. Anchor →
        // SetAnchorCommand). Never wrap these direct writes in Submit(WorkflowActionPair).
        // Unmounted-target rejection (tree is null) is still enforced above.

        return new VeloxJsonObject
        {
            ["status"] = successCount > 0 ? "ok" : "error",
            ["message"] = $"{successCount}/{patch.Count} properties patched.",
            ["details"] = results,
        }.ToJson(VeloxJsonFormat.Indented);
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

    // JSON 字面量读作 null；其余值写回 JSON 文本后，由生成的序列化器按声明类型读回。
    private static object? DeserializeToType(VeloxJsonValue? value, Type targetType)
        => value is null || value.IsNull ? null : VeloxJsonSerializer.Deserialize(value.ToJson(), targetType);

    /// <summary>
    /// Copies all writable scalar (non-command-backed) properties from source to target.
    /// Both objects should be of the same type. Command-backed and ICommand properties are skipped.
    /// </summary>
    public static void CopyScalarProperties(object source, object target)
    {
        if (source == null || target == null) return;

        var from = AIContextTreeRegistry.FindAccessor(source);
        var to = AIContextTreeRegistry.FindAccessor(target);
        if (from is null || to is null) return;

        var sourceType = source.GetType();

        foreach (var member in AIContextDirectory.Shared.MembersAcross(to.TypeName, "Properties"))
        {
            if (!member.Has(AIContextFlags.CanRead) || !member.Has(AIContextFlags.CanWrite)) continue;
            if (FrameworkManagedProperties.Contains(member.Name)) continue;
            if (FindBackingCommand(sourceType, member.Name) != null) continue;

            var pt = to.MemberType(member.Name);
            if (pt is null) continue;
            if (pt == typeof(string) || pt == typeof(int) || pt == typeof(double) || pt == typeof(bool) ||
                pt == typeof(long) || pt == typeof(float) || pt == typeof(decimal) || pt.IsEnum)
            {
                if (from.TryGet(source, member.Name, out var value)) to.Set(target, member.Name, value);
            }
        }
    }
}
