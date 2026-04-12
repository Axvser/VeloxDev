using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using VeloxDev.AI;
using CoreWorkflowAgent = VeloxDev.AI.Workflow;
using VeloxDev.MVVM;

namespace VeloxDev.Core.Extension.Agent.Workflow;

public static class WorkflowContextProvider
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<Type, ComponentContext> ComponentContextMap = [];

    public static void AsWorkflowAgentContextProvider(this Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        CoreWorkflowAgent.WorkflowAgentContextRegistry.RegisterWorkflowAgentContext(type);

        lock (SyncRoot)
        {
            ComponentContextMap[type] = new ComponentContext(type);
        }
    }
    
    [Description("Read the workflow agent role definition, tool conventions, request contracts, and the registered component contexts before calling any workflow agent tool.")]
    public static string GetWorkflowHelper()
    {
        return $"""
        $Role: You are an in-process agent running inside a .NET application. Use the workflow protocol tools below as the only runtime takeover contract.

        # Workflow Agent Helper

        ## 1. Recommended tool loop

        1. Read `GetWorkflowBootstrap` once.
        2. Read only the required semantic section through `GetWorkflowContextSection`.
        3. Open or refresh a runtime session through `OpenWorkflowSession`.
        4. Read compact projections through `QueryWorkflowGraph`.
        5. Inspect annotated takeover surface through `GetWorkflowTargetCapabilities` when needed.
        6. Validate edits through `ValidateWorkflowPatch` before destructive changes.
        7. Submit batched edits through `ApplyWorkflowPatch`.
        8. Execute runtime actions, commands, or methods through `InvokeWorkflowActionAsync`, `InvokeWorkflowCommandAsync`, and `InvokeWorkflowMethodAsync`.
        9. Synchronize incrementally through `GetWorkflowChanges` and `GetWorkflowDiagnostics`.

        ## 2. Token strategy

        - Prefer `queryMode: summary` for the first read.
        - Prefer stable ids such as `nodeId`, `slotId`, and `linkId`.
        - Prefer `ValidateWorkflowPatch` before `ApplyWorkflowPatch` for multi-step edits.
        - Prefer `ApplyWorkflowPatch` over one-change-per-call mutation flows.
        - Prefer `returnMode: delta` unless a full snapshot is necessary.
        - Prefer `GetWorkflowChanges` over repeatedly fetching the entire tree.

        ## 3. Context entry points

        - `GetWorkflowAgentContextDocument`
        - `GetWorkflowFrameworkContext`
        - `GetWorkflowEnumContext`
        - `GetWorkflowValueTypeContext`
        - `GetRegisteredWorkflowComponentContext`
        - `GetWorkflowTypeAgentContext`

        ## 4. Runtime takeover tools

        - `OpenWorkflowSession`
        - `QueryWorkflowGraph`
        - `GetWorkflowTargetCapabilities`
        - `GetWorkflowPropertyValue`
        - `ValidateWorkflowPatch`
        - `ApplyWorkflowPatch`
        - `InvokeWorkflowActionAsync`
        - `InvokeWorkflowCommandAsync`
        - `InvokeWorkflowMethodAsync`
        - `GetWorkflowChanges`
        - `GetWorkflowDiagnostics`
        - `ReleaseWorkflowProtocolSession`

        ## 5. Registered component context

        {CoreWorkflowAgent.WorkflowAgentContextProvider.ProvideRegisteredWorkflowComponentContext(AgentLanguages.English)}
        """;
    }

    [Description("Get the context for a single registered workflow component by full type name or simple type name.")]
    public static string GetComponentContext([Description("The full type name or simple type name of a registered workflow component.")] string typeName)
    {
        return CoreWorkflowAgent.WorkflowAgentTools.GetWorkflowTypeAgentContext(typeName);
    }
}

public partial class ComponentContext(Type componentType)
{
    [VeloxProperty] private string _typeName = componentType.FullName;
    [VeloxProperty] private ObservableCollection<string> _classDescriptions = [];
    [VeloxProperty] private Dictionary<string, ObservableCollection<string>> _propertyDescriptions = [];
    [VeloxProperty] private string _jsonExample = string.Empty;

    private void LoadContext()
    {
        if (_classDescriptions.Count > 0 || _propertyDescriptions.Count > 0 || !string.IsNullOrWhiteSpace(_jsonExample))
        {
            return;
        }

        foreach (var item in GetTypeDescriptions(componentType))
        {
            _classDescriptions.Add(item);
        }

        foreach (var property in GetSerializableProperties(componentType))
        {
            _propertyDescriptions[property.Name] = [.. GetPropertyDescriptions(property)];
        }

        _jsonExample = CreateJsonExample();
    }

    public override string ToString()
    {
        LoadContext();

        var builder = new StringBuilder();
        builder.AppendLine($"### Component `{TypeName}`");
        builder.AppendLine("- Type metadata");
        foreach (var description in ClassDescriptions)
        {
            builder.AppendLine($"  - {description}");
        }

        builder.AppendLine("- Property metadata");
        if (PropertyDescriptions.Count == 0)
        {
            builder.AppendLine("  - No public writable properties");
        }
        else
        {
            foreach (var property in PropertyDescriptions.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                builder.AppendLine($"  - `{property.Key}`");
                foreach (var description in property.Value)
                {
                    builder.AppendLine($"    - {description}");
                }
            }
        }

        builder.AppendLine("- JSON example");
        builder.AppendLine("```json");
        builder.AppendLine(_jsonExample);
        builder.Append("```");
        return builder.ToString();
    }

    private static IEnumerable<PropertyInfo> GetSerializableProperties(Type type)
        => type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead && property.CanWrite)
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private static IEnumerable<string> GetTypeDescriptions(Type type)
    {
        var descriptions = new List<string>
        {
            $"$type:{type.FullName ?? type.Name}"
        };

        descriptions.AddRange(GetCommonDescriptions(type));

        if (descriptions.Count == 1)
        {
            descriptions.Add("$description:No type description was provided.");
        }

        return descriptions;
    }

    private static IEnumerable<string> GetPropertyDescriptions(PropertyInfo property)
    {
        var descriptions = new List<string>
        {
            $"$type:{GetFriendlyTypeName(property.PropertyType)}"
        };

        descriptions.AddRange(GetCommonDescriptions(property));

        if (descriptions.Count == 1)
        {
            descriptions.Add("$description:No property description was provided.");
        }

        return descriptions;
    }

    private static IEnumerable<string> GetCommonDescriptions(MemberInfo member)
    {
        if (member.GetCustomAttribute<DisplayNameAttribute>() is { DisplayName: { Length: > 0 } displayName })
        {
            yield return $"$displayName:{displayName}";
        }

        if (member.GetCustomAttribute<DescriptionAttribute>() is { Description: { Length: > 0 } description })
        {
            yield return $"$description:{description}";
        }

        if (member.GetCustomAttribute<DefaultValueAttribute>() is { Value: { } defaultValue })
        {
            yield return $"$defaultValue:{FormatLiteral(defaultValue)}";
        }
    }

    private string CreateJsonExample()
    {
        if (TryCreateSampleInstance(componentType, out var instance))
        {
            try
            {
                return instance.Serialize();
            }
            catch
            {
            }
        }

        return CreateSkeletonJsonExample(componentType);
    }

    private static bool TryCreateSampleInstance(Type type, out INotifyPropertyChanged instance)
    {
        instance = null!;

        if (!typeof(INotifyPropertyChanged).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
        {
            return false;
        }

        try
        {
            if (Activator.CreateInstance(type) is INotifyPropertyChanged created)
            {
                instance = created;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string CreateSkeletonJsonExample(Type type)
    {
        var lines = new List<string>
        {
            $"  \"$type\": \"{EscapeJson(type.FullName ?? type.Name)}\""
        };

        foreach (var property in GetSerializableProperties(type))
        {
            lines.Add($"  \"{EscapeJson(property.Name)}\": {CreateJsonPlaceholder(property.PropertyType)}");
        }

        return $"{{{Environment.NewLine}{string.Join($",{Environment.NewLine}", lines)}{Environment.NewLine}}}";
    }

    private static string CreateJsonPlaceholder(Type type)
    {
        var targetType = Nullable.GetUnderlyingType(type) ?? type;

        if (targetType == typeof(string) || targetType == typeof(char) || targetType == typeof(Guid) || targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset) || targetType == typeof(TimeSpan))
        {
            return "\"\"";
        }

        if (targetType == typeof(bool))
        {
            return bool.FalseString.ToLowerInvariant();
        }

        if (targetType.IsEnum)
        {
            return Convert.ToInt64(Enum.GetValues(targetType).GetValue(0), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        if (IsNumericType(targetType))
        {
            return "0";
        }

        if (IsDictionaryType(targetType))
        {
            return "{}";
        }

        if (targetType.IsArray || IsEnumerableType(targetType))
        {
            return "[]";
        }

        if (targetType.IsValueType)
        {
            return Convert.ToString(Activator.CreateInstance(targetType), CultureInfo.InvariantCulture) ?? "null";
        }

        return "null";
    }

    private static bool IsEnumerableType(Type type)
        => type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static bool IsDictionaryType(Type type)
        => (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
        || type
            .GetInterfaces()
            .Any(interfaceType => interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>));

    private static bool IsNumericType(Type type)
        => type == typeof(byte)
        || type == typeof(sbyte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong)
        || type == typeof(float)
        || type == typeof(double)
        || type == typeof(decimal);

    private static string GetFriendlyTypeName(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            return $"{GetFriendlyTypeName(nullableType)}?";
        }

        if (type.IsArray)
        {
            return $"{GetFriendlyTypeName(type.GetElementType() ?? typeof(object))}[]";
        }

        if (type.IsGenericType)
        {
            var genericTypeName = type.GetGenericTypeDefinition().FullName ?? type.Name;
            var index = genericTypeName.IndexOf('`');
            if (index >= 0)
            {
                genericTypeName = genericTypeName.Substring(0, index);
            }

            return $"{genericTypeName}<{string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName))}>";
        }

        return type.FullName ?? type.Name;
    }

    private static string FormatLiteral(object value)
        => value switch
        {
            string text => text,
            char character => character.ToString(),
            bool boolean => boolean.ToString().ToLowerInvariant(),
            Enum enumValue => Convert.ToInt64(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static string EscapeJson(string text)
        => text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
}
