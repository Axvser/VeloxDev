using System.Reflection;

namespace VeloxDev.AI;

/// <summary>
/// Provides generic reflection-based property read/write capabilities for Agent scenarios.
/// Framework-agnostic — works with any .NET object, not limited to workflow components.
/// </summary>
public static class AgentPropertyAccessor
{
    /// <summary>
    /// Describes a single property on an object for Agent consumption.
    /// </summary>
    public sealed class PropertyDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public Type PropertyType { get; set; } = typeof(object);
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public object? CurrentValue { get; set; }
        public IReadOnlyList<string> AgentDescriptions { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Result of a property set operation.
    /// </summary>
    public sealed class SetResult
    {
        public string PropertyName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Discovers all public instance properties on the target object.
    /// Optionally filters by a predicate and attaches <see cref="AgentContextAttribute"/> descriptions.
    /// </summary>
    /// <param name="target">The object to inspect.</param>
    /// <param name="language">Language for <see cref="AgentContextAttribute"/> lookup.</param>
    /// <param name="filter">Optional predicate to exclude properties (return <c>false</c> to skip).</param>
    /// <param name="includeValues">If <c>true</c>, reads current property values (may throw on some properties).</param>
    public static IReadOnlyList<PropertyDescriptor> DiscoverProperties(
        object target,
        AgentLanguages language = AgentLanguages.English,
        Func<PropertyInfo, bool>? filter = null,
        bool includeValues = false)
    {
        if (target == null) return Array.Empty<PropertyDescriptor>();

        var type = target.GetType();
        var result = new List<PropertyDescriptor>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (filter != null && !filter(prop)) continue;

            var desc = new PropertyDescriptor
            {
                Name = prop.Name,
                PropertyType = prop.PropertyType,
                CanRead = prop.CanRead,
                CanWrite = prop.CanWrite,
                AgentDescriptions = prop.GetCustomAttributes<AgentContextAttribute>(inherit: false)
                    .Where(a => a.Language == language)
                    .Select(a => a.Context)
                    .ToArray(),
            };

            if (includeValues && prop.CanRead)
            {
                try { desc.CurrentValue = prop.GetValue(target); }
                catch { /* inaccessible */ }
            }

            result.Add(desc);
        }

        return result;
    }

    /// <summary>
    /// Gets the value of a named property via reflection.
    /// </summary>
    /// <returns>The property value, or <c>null</c> if not found or not readable.</returns>
    public static object? GetPropertyValue(object target, string propertyName)
    {
        if (target == null) return null;
        var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null || !prop.CanRead) return null;
        return prop.GetValue(target);
    }

    /// <summary>
    /// Sets the value of a named property via reflection.
    /// </summary>
    public static SetResult SetPropertyValue(object target, string propertyName, object? value)
    {
        var result = new SetResult { PropertyName = propertyName };

        if (target == null)
        {
            result.Error = "Target is null.";
            return result;
        }

        var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null)
        {
            result.Error = $"Property '{propertyName}' not found on type '{target.GetType().FullName}'.";
            return result;
        }

        if (!prop.CanWrite)
        {
            result.Error = $"Property '{propertyName}' is read-only.";
            return result;
        }

        try
        {
            var converted = ConvertValue(value, prop.PropertyType);
            prop.SetValue(target, converted);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = $"Failed to set '{propertyName}': {ex.Message}";
        }

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
        if (target == null) return Array.Empty<SetResult>();

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
    /// Copies all writable scalar properties from source to target.
    /// Skips properties that match the <paramref name="skip"/> predicate.
    /// </summary>
    public static void CopyScalarProperties(
        object source,
        object target,
        Func<PropertyInfo, bool>? skip = null)
    {
        if (source == null || target == null) return;
        var type = source.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (skip != null && skip(prop)) continue;

            var pt = prop.PropertyType;
            if (pt == typeof(string) || pt == typeof(int) || pt == typeof(double) || pt == typeof(bool) ||
                pt == typeof(long) || pt == typeof(float) || pt == typeof(decimal) || pt.IsEnum ||
                pt == typeof(byte) || pt == typeof(short) || pt == typeof(char))
            {
                try { prop.SetValue(target, prop.GetValue(source)); }
                catch { /* skip inaccessible */ }
            }
        }
    }

    /// <summary>
    /// Attempts basic type conversion for common primitives and enums.
    /// </summary>
    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                throw new InvalidCastException($"Cannot assign null to non-nullable type '{targetType.FullName}'.");
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying.IsAssignableFrom(value.GetType()))
            return value;

        if (underlying.IsEnum)
        {
            if (value is string s)
                return Enum.Parse(underlying, s, ignoreCase: true);
            return Enum.ToObject(underlying, value);
        }

        return Convert.ChangeType(value, underlying);
    }
}
