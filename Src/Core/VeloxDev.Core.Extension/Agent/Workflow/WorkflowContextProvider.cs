using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using VeloxDev.Core.MVVM;

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

        lock (SyncRoot)
        {
            ComponentContextMap[type] = new ComponentContext(type);
        }
    }
    
    [Description("Read the workflow agent role definition, tool conventions, request contracts, and the registered component contexts before calling any workflow agent tool.")]
    public static string GetWorkflowHelper()
    {
        ComponentContext[] componentContexts;
        lock (SyncRoot)
        {
            componentContexts = [.. ComponentContextMap.Values.OrderBy(context => context.TypeName, StringComparer.Ordinal)];
        }

        var contextText = componentContexts.Length == 0
            ? "No workflow components are registered yet. Call AsWorkflowAgentContextProvider(type) for each component type before using workflow agent tools."
            : string.Join($"{Environment.NewLine}{Environment.NewLine}", componentContexts.Select(context => context.ToString()));

        return $"""
        $Role: You are an in-process agent running inside a .NET application. You must use the following tools and contracts to take over workflow operations.

        # Workflow Tools Manual

        ## 1. Registered component context

        > Read the supported workflow component types, type descriptions, property descriptions, and serialized JSON examples below. These contexts define the JSON shapes that must be used in later tool calls.

        {contextText}

        ## 2. JSON-first operating model

        ### 2.1 Dynamic reading and creation from JSON
        - Every workflow agent tool uses JSON requests and JSON responses.
        - Reuse the documented `$type` values and property names exactly as provided by the registered component contexts.
        - If a JSON example contains `$id` or `$ref`, preserve the reference semantics in later requests.
        - Only assign public writable properties unless a sample explicitly shows a different runtime shape.
        - When a property is interface-based, abstract, or polymorphic, provide a concrete `$type` that can actually be instantiated.

        ### 2.2 Workflow takeover constraints
        - Determine the target component type from the registered contexts before constructing a request.
        - Prefer minimal updates based on the latest returned JSON snapshot instead of rebuilding unrelated fields.
        - Use JSON arrays for collections and JSON objects for dictionaries.
        - If a property has no extra description, fall back to the minimal valid value for its `.NET` type.
        - Always continue from the latest tool response. Do not reason from stale snapshots.
        - If only a skeleton JSON sample is available, treat it as the minimum safe contract for that type.

        ### 2.3 Execution strategy
        - Read the component context before choosing the next tool.
        - Follow the loop: read current JSON -> understand structure -> build the next JSON request.
        - Reuse the latest returned workflow JSON whenever you perform consecutive operations on the same graph.

        ## 3. Runtime session model

        - Stateless mode: send a full `tree` payload in each request. This is simple and deterministic.
        - Session mode: call `CreateWorkflowSession` first, then send `sessionId` in later requests. Use this mode for runtime-only capabilities such as `UndoWorkflowTree`, `RedoWorkflowTree`, and `ClearWorkflowTreeHistory`.
        - A request may contain both `sessionId` and `tree`. If both are provided, the tool treats the `tree` payload as the latest snapshot and refreshes the runtime session.

        ## 4. Request contract

        - All workflow agent tools accept a single JSON request string.
        - Common fields:
          - `sessionId`: optional string, used to bind the request to a live runtime workflow session.
          - `tree`: optional workflow tree JSON object. Required in stateless mode.
        - Component targeting fields:
          - `nodeIndex`: zero-based index in `tree.Nodes`.
          - `slotIndex`: zero-based index in `node.Slots`.
          - `linkIndex`: zero-based index in `tree.Links`.
        - Value payload fields:
          - `node`, `slot`: component JSON object used for creation.
          - `anchor`, `size`, `offset`: value objects for geometry updates.
          - `parameter`: any JSON value passed into work or broadcast operations.
          - `broadcastMode`, `reverseBroadcastMode`, `channel`, `isVisible`: scalar configuration values.

        ## 5. Tool catalog

        ### 5.1 Session tools
        - `CreateWorkflowSession`
        - `GetWorkflowSessionState`
        - `ReleaseWorkflowSession`

        ### 5.2 Tree tools
        - `NormalizeWorkflowTreeJson`
        - `CloseWorkflowTreeAsync`
        - `SetWorkflowPointer`
        - `ResetWorkflowVirtualLink`
        - `UndoWorkflowTree`
        - `RedoWorkflowTree`
        - `ClearWorkflowTreeHistory`

        ### 5.3 Node tools
        - `GetWorkflowNodeJson`
        - `CreateWorkflowNode`
        - `DeleteWorkflowNode`
        - `MoveWorkflowNode`
        - `SetWorkflowNodeAnchor`
        - `SetWorkflowNodeSize`
        - `SetWorkflowNodeBroadcastMode`
        - `SetWorkflowNodeReverseBroadcastMode`
        - `InvokeWorkflowNodeWorkAsync`
        - `InvokeWorkflowNodeBroadcastAsync`
        - `InvokeWorkflowNodeReverseBroadcastAsync`

        ### 5.4 Slot tools
        - `GetWorkflowSlotJson`
        - `CreateWorkflowSlot`
        - `DeleteWorkflowSlot`
        - `SetWorkflowSlotSize`
        - `SetWorkflowSlotChannel`
        - `ValidateWorkflowConnection`
        - `ConnectWorkflowSlots`

        ### 5.5 Link tools
        - `GetWorkflowLinkJson`
        - `DeleteWorkflowLink`
        - `SetWorkflowLinkVisibility`
        """;
    }

    [Description("Get the context for a single registered workflow component by full type name or simple type name.")]
    public static string GetComponentContext([Description("The full type name or simple type name of a registered workflow component.")] string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Component type name cannot be null or empty.", nameof(typeName));
        }

        lock (SyncRoot)
        {
            foreach (var item in ComponentContextMap)
            {
                if (string.Equals(item.Key.FullName, typeName, StringComparison.Ordinal)
                    || string.Equals(item.Key.Name, typeName, StringComparison.Ordinal)
                    || string.Equals(item.Value.TypeName, typeName, StringComparison.Ordinal))
                {
                    return item.Value.ToString();
                }
            }
        }

        return $"The context for component `{typeName}` was not found. Register the component type by calling AsWorkflowAgentContextProvider first.";
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
        instance = null;

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
