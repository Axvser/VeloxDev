namespace VeloxDev.AI;

/// <summary>
/// Describes and edits an object's properties through the compiled agent context tree.
/// </summary>
/// <remarks>
/// <para>
/// Framework-agnostic — works with any object the tree carries an entry for, not limited to workflow components.
/// Nothing here reflects: the property list, its types and its read/write status were all recorded when the
/// declaring assembly was compiled, and the write goes through that type's generated
/// <see cref="IAIContextAccessor"/>.
/// </para>
/// <para>
/// The consequence is a closed world. A type whose assembly was not compiled with the context tree generator has
/// no entry, so it is neither describable nor writable — the answer is an empty list or a refusal, never a guess.
/// </para>
/// </remarks>
public static class AgentPropertyAccessor
{
    /// <summary>
    /// Describes a single property on an object for Agent consumption.
    /// </summary>
    public sealed class PropertyDescriptor
    {
        /// <summary>The property's name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The declared type's full name, as the tree records it — e.g. <c>System.Int32</c>. Never a
        /// <see cref="System.Type"/>, which is what keeps the descriptor trimmable.
        /// </summary>
        public string PropertyType { get; set; } = "System.Object";

        /// <summary>Whether the property can be read.</summary>
        public bool CanRead { get; set; }
        /// <summary>Whether the property can be written.</summary>
        public bool CanWrite { get; set; }
        /// <summary>The property's current value, when discovery was asked to read values.</summary>
        public object? CurrentValue { get; set; }
        /// <summary>The <c>[AgentContext]</c> descriptions for this property.</summary>
        public IReadOnlyList<string> AgentDescriptions { get; set; } = [];
    }

    /// <summary>
    /// Result of a property set operation.
    /// </summary>
    public sealed class SetResult
    {
        /// <summary>The name of the property the write targeted.</summary>
        public string PropertyName { get; set; } = string.Empty;
        /// <summary>Whether the write succeeded.</summary>
        public bool Success { get; set; }
        /// <summary>The reason the write was refused, when it was.</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Discovers the public instance properties the tree records for the target object, including the ones it
    /// inherits. Optionally filters by a predicate and attaches <c>[AgentContext]</c> descriptions.
    /// </summary>
    /// <param name="target">The object to inspect.</param>
    /// <param name="language">Language for <c>[AgentContext]</c> lookup.</param>
    /// <param name="filter">Optional predicate over the property name (return <c>false</c> to skip).</param>
    /// <param name="includeValues">If <c>true</c>, reads current property values.</param>
    /// <returns>The descriptors; empty when the target is <c>null</c> or its type has no entry in the tree.</returns>
    public static IReadOnlyList<PropertyDescriptor> DiscoverProperties(
        object target,
        AgentLanguages language = AgentLanguages.English,
        Func<string, bool>? filter = null,
        bool includeValues = false)
    {
        if (AIContextMembers.TypeNameOf(target) is not { } typeName) return [];

        var result = new List<PropertyDescriptor>();

        foreach (var node in AIContextDirectory.Shared.MembersAcross(typeName, "Properties"))
        {
            if (filter != null && !filter(node.Name)) continue;

            var desc = new PropertyDescriptor
            {
                Name = node.Name,
                PropertyType = node.TypeName ?? string.Empty,
                CanRead = node.Has(AIContextFlags.CanRead),
                CanWrite = node.Has(AIContextFlags.CanWrite),
                AgentDescriptions = AIContextMembers.DescriptionsFor(node, language),
            };

            if (includeValues && desc.CanRead)
            {
                var accessor = AIContextMembers.AccessorFor(node);
                if (accessor != null && accessor.TryGet(target, node.Name, out var value)) desc.CurrentValue = value;
            }

            result.Add(desc);
        }

        return result;
    }

    /// <summary>
    /// Gets the value of a named property.
    /// </summary>
    /// <returns>The value, or <c>null</c> when the property is not in the tree or is not readable.</returns>
    public static object? GetPropertyValue(object target, string propertyName)
    {
        if (AIContextMembers.TypeNameOf(target) is not { } typeName) return null;

        var node = AIContextMembers.Find(typeName, "Properties", propertyName);
        if (node == null || !node.Has(AIContextFlags.CanRead)) return null;

        var accessor = AIContextMembers.AccessorFor(node);
        return accessor != null && accessor.TryGet(target, propertyName, out var value) ? value : null;
    }

    /// <summary>
    /// Sets the value of a named property, converting the value to the property's own type.
    /// </summary>
    /// <param name="target">The object to write to.</param>
    /// <param name="propertyName">The property's name.</param>
    /// <param name="value">The value to write.</param>
    /// <returns>A result carrying the reason when the write was refused.</returns>
    public static SetResult SetPropertyValue(object target, string propertyName, object? value)
    {
        var result = new SetResult { PropertyName = propertyName };

        if (AIContextMembers.TypeNameOf(target) is not { } typeName)
        {
            result.Error = "Target is null, or its type is not in the agent context tree.";
            return result;
        }

        var node = AIContextMembers.Find(typeName, "Properties", propertyName);
        if (node == null)
        {
            result.Error = $"Property '{propertyName}' is not in the agent context tree for type '{typeName}'.";
            return result;
        }

        if (!node.Has(AIContextFlags.CanWrite))
        {
            result.Error = $"Property '{propertyName}' is read-only.";
            return result;
        }

        var accessor = AIContextMembers.AccessorFor(node);
        if (accessor == null)
        {
            result.Error = $"No accessor is registered for type '{node.OwnerTypeName}'.";
            return result;
        }

        result.Error = accessor.Set(target, propertyName, value);
        result.Success = result.Error == null;
        return result;
    }

    /// <summary>
    /// Sets multiple properties on a target object from a dictionary of name-value pairs.
    /// </summary>
    /// <param name="target">The object to patch.</param>
    /// <param name="properties">Property name to value mappings.</param>
    /// <param name="rejected">Optional set of property names to reject (framework-managed, etc.).</param>
    public static IReadOnlyList<SetResult> SetProperties(
        object target,
        IReadOnlyDictionary<string, object?> properties,
        ISet<string>? rejected = null)
    {
        if (target == null) return [];

        var results = new List<SetResult>();

        foreach (var kv in properties)
        {
            if (rejected != null && rejected.Contains(kv.Key))
            {
                results.Add(new SetResult
                {
                    PropertyName = kv.Key,
                    Error = $"Property '{kv.Key}' is rejected (framework-managed or restricted)."
                });
                continue;
            }

            results.Add(SetPropertyValue(target, kv.Key, kv.Value));
        }

        return results;
    }

    /// <summary>
    /// Copies the writable properties two objects have in common from source to target, by name.
    /// </summary>
    /// <param name="source">The object to read from. It must be in the tree too.</param>
    /// <param name="target">The object to write to.</param>
    /// <returns>The number of properties copied.</returns>
    public static int CopyScalarProperties(object source, object target)
    {
        if (source == null || target == null) return 0;

        return AIContextTreeRegistry.FindAccessor(target)?.CopyScalarFrom(source, target) ?? 0;
    }
}
