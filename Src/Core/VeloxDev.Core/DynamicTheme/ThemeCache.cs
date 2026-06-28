using System.Reflection;
using System.Runtime.CompilerServices;

namespace VeloxDev.DynamicTheme
{
    /// <summary>
    /// Global cache for theme configurations. Eliminates per-class generated static dictionaries
    /// by storing all theme data in a single location, keyed by the declaring type.
    /// </summary>
    public static class ThemeCache
    {
        // ── Static (default) per-type configuration ──
        // Type → (propertyName → (PropertyInfo, (themeType → value)))
        private static readonly Dictionary<Type, Dictionary<string, PropertyEntry>> _staticCache = [];
        private static readonly object _lock = new();

        // ── Active (runtime-override) per-instance cache ──
        private static readonly ConditionalWeakTable<IThemeObject, InstanceCache> _activeCache = new();

        // ── Shared converter instances ──
        private static readonly Dictionary<string, IThemeValueConverter> _converters = [];
        private static int _converterIndex;

        private readonly struct PropertyEntry(PropertyInfo property, Dictionary<Type, object?> values)
        {
            public PropertyInfo Property { get; } = property;
            public Dictionary<Type, object?> Values { get; } = values;
        }

        /// <summary>
        /// Per-instance mutable cache entry.
        /// </summary>
        public sealed class InstanceCache
        {
            public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> Overrides { get; set; } = [];
        }

        /// <summary>
        /// Check if a type has already been registered in the static cache.
        /// </summary>
        public static bool IsTypeRegistered(Type type)
        {
            lock (_lock)
            {
                return _staticCache.ContainsKey(type);
            }
        }

        /// <summary>
        /// Register theme property configuration for a specific type.
        /// Thread-safe; duplicate registration is silently ignored.
        /// Called from generated <see cref="IThemeObject.InitializeTheme"/> implementations.
        /// </summary>
        public static void RegisterType(Type type, Dictionary<string, (PropertyInfo Property, Dictionary<Type, object?> Values)> properties)
        {
            lock (_lock)
            {
                if (!_staticCache.ContainsKey(type))
                {
                    var entries = new Dictionary<string, PropertyEntry>(properties.Count);
                    foreach (var kvp in properties)
                    {
                        entries[kvp.Key] = new PropertyEntry(kvp.Value.Property, kvp.Value.Values);
                    }
                    _staticCache[type] = entries;
                }
            }
        }

        /// <summary>
        /// Register a converter instance for reuse across types.
        /// </summary>
        public static string RegisterConverter(IThemeValueConverter converter)
        {
            lock (_lock)
            {
                var key = $"__velox_global_converter_{_converterIndex++}__";
                _converters[key] = converter;
                return key;
            }
        }

        /// <summary>
        /// Get a converter by key.
        /// </summary>
        public static IThemeValueConverter? GetConverter(string key)
        {
            lock (_lock)
            {
                return _converters.TryGetValue(key, out var c) ? c : null;
            }
        }

        /// <summary>
        /// Get the static (default) theme cache for a type, walking the inheritance chain.
        /// Returns a merged dictionary of all properties from the type and its base types.
        /// </summary>
        public static Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetStaticForType(Type type)
        {
            var result = new Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>();
            CollectStaticForType(type, result);
            return result;
        }

        private static void CollectStaticForType(Type? type, Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> accumulator)
        {
            if (type == null || type == typeof(object))
                return;

            // Recursively collect base type first so derived properties override base ones
            CollectStaticForType(type.BaseType, accumulator);

            lock (_lock)
            {
                if (_staticCache.TryGetValue(type, out var entries))
                {
                    foreach (var kvp in entries)
                    {
                        accumulator[kvp.Key] = new Dictionary<PropertyInfo, Dictionary<Type, object?>>
                        {
                            { kvp.Value.Property, kvp.Value.Values }
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Get or create the active (runtime-override) cache for an instance.
        /// </summary>
        public static InstanceCache GetOrCreateActiveEntry(IThemeObject instance)
        {
            return _activeCache.GetValue(instance, _ => new InstanceCache());
        }

        /// <summary>
        /// Get the active (runtime-override) cache for an instance, or null if not registered.
        /// </summary>
        public static InstanceCache? TryGetActiveEntry(IThemeObject instance)
        {
            _activeCache.TryGetValue(instance, out var entry);
            return entry;
        }

        /// <summary>
        /// Remove the active cache entry for an instance.
        /// </summary>
        public static void RemoveActiveEntry(IThemeObject instance)
        {
            _activeCache.Remove(instance);
        }

        /// <summary>
        /// Try to lookup a default value for a property on a type for a given theme.
        /// Walks the inheritance chain if the type doesn't have its own entry.
        /// </summary>
        public static bool TryGetDefaultValue(Type type, string propertyName, Type themeType, out object? value)
        {
            var current = type;
            while (current != null && current != typeof(object))
            {
                lock (_lock)
                {
                    if (_staticCache.TryGetValue(current, out var props) &&
                        props.TryGetValue(propertyName, out var entry) &&
                        entry.Values.TryGetValue(themeType, out value))
                    {
                        return true;
                    }
                }
                current = current.BaseType;
            }

            value = null;
            return false;
        }
    }
}
