using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Reflection;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Resolves .NET types by full name across loaded assemblies and produces
/// a JSON schema description suitable for Agent consumption.
/// </summary>
public static class TypeIntrospector
{
    /// <summary>
    /// Delegates to <see cref="AgentTypeResolver.ResolveType"/> in Core.
    /// </summary>
    public static Type? ResolveType(string fullTypeName)
        => AgentTypeResolver.ResolveType(fullTypeName);

    /// <summary>
    /// Produces a JSON schema-like description of a type including its properties,
    /// fields, base type, interfaces, and (for enums) values.
    /// </summary>
    public static string GetTypeSchema(Type type)
    {
        var obj = new JObject
        {
            ["fullName"] = type.FullName,
            ["kind"] = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class",
            ["baseType"] = type.BaseType?.FullName,
            ["interfaces"] = new JArray(type.GetInterfaces().Select(i => i.FullName).ToArray()),
        };

        if (type.IsEnum)
        {
            var values = new JObject();
            foreach (var name in Enum.GetNames(type))
            {
                values[name] = Convert.ToInt64(Enum.Parse(type, name));
            }
            obj["values"] = values;
        }
        else
        {
            var props = new JArray();
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propObj = new JObject
                {
                    ["name"] = prop.Name,
                    ["type"] = FriendlyTypeName(prop.PropertyType),
                    ["canRead"] = prop.CanRead,
                    ["canWrite"] = prop.CanWrite,
                };
                props.Add(propObj);
            }
            obj["properties"] = props;
        }

        // Inject [AgentContext] class-level descriptions — these are the developer's
        // authoritative instructions and override any runtime defaults.
        var agentDescs = new JArray();
        foreach (AgentLanguages lang in Enum.GetValues(typeof(AgentLanguages)))
        {
            foreach (var desc in AgentContextCollector.GetAgentContext(type, lang))
                agentDescs.Add(desc);
        }
        if (agentDescs.Count > 0)
            obj["developerInstructions"] = agentDescs;

        // Try to create a default instance and serialize it
        // NOTE: These are runtime zero-initialized values, NOT the intended defaults.
        // Always prefer developerInstructions over defaultJson.
        try
        {
            if (!type.IsAbstract && !type.IsInterface && !type.IsEnum)
            {
                var instance = Activator.CreateInstance(type);
                if (instance != null)
                {
                    var json = JsonConvert.SerializeObject(instance, Formatting.Indented, new JsonSerializerSettings
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        MaxDepth = 3,
                        Error = (s, e) => e.ErrorContext.Handled = true,
                    });
                    obj["defaultJson_runtimeOnly"] = JToken.Parse(json);
                }
            }
        }
        catch { /* default instance not available */ }

        return obj.ToString(Formatting.Indented);
    }

    private static string FriendlyTypeName(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(double)) return "double";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(float)) return "float";
        if (t == typeof(long)) return "long";
        if (t == typeof(decimal)) return "decimal";
        if (t == typeof(void)) return "void";
        if (t.IsGenericType)
        {
            var baseName = t.Name.Split('`')[0];
            var args = string.Join(", ", t.GetGenericArguments().Select(FriendlyTypeName));
            return $"{baseName}<{args}>";
        }
        return t.FullName ?? t.Name;
    }
}
