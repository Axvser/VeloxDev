using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// Renders the prompt blocks the way the library used to, by reflecting over metadata, so the tree-backed
/// renderer can be compared against it character for character.
/// </summary>
/// <remarks>
/// <para>
/// These bodies used to live in <see cref="AgentContextCollector"/> as a fallback for types the tree does not
/// carry. That fallback is gone — the tree is the only source now — so the code that produced the expected
/// output moved here, where it is an oracle rather than a shipping path.
/// </para>
/// <para>
/// Keep it in step with the tables it reproduces, not with the library: it is meant to be the version of the
/// rendering that existed before the migration, and it stops being useful the day it stops being that.
/// </para>
/// </remarks>
internal static class ReflectionContextOracle
{
    internal static string EnumBlock(Type enumType, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();

        result.AppendLine("Enum");
        result.AppendLine();
        result.AppendLine($"Type: {enumType.FullName}");
        result.AppendLine();
        result.AppendLine("Descriptions:");
        foreach (var context in AgentContextCollector.GetAgentContext(enumType, language))
        {
            result.AppendLine($"- {context}");
        }
        result.AppendLine();
        result.AppendLine($"Member Value Type: {Enum.GetUnderlyingType(enumType)}");
        result.AppendLine();
        result.AppendLine($"Member Value List:");
        result.AppendLine();
        result.AppendLine("| Name | Value | Description |");
        result.AppendLine("| ---- | ----- | ----------- |");
        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var name = field.Name;
            var value = Convert.ChangeType(field.GetValue(null), Enum.GetUnderlyingType(enumType));
            var descriptions = AgentContextCollector.GetAgentContext(field, language);
            var description = string.Empty;
            for (int i = 0; i < descriptions.Length; i++)
            {
                description += $"[{i + 1}-{descriptions[i]}]";
            }
            result.AppendLine($"| {name} | {value} | {description} |");
        }
        result.AppendLine();

        return result.ToString();
    }

    internal static string InterfaceBlock(Type type, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();

        result.AppendLine("Interface");
        result.AppendLine();
        result.AppendLine($"Type: {type.FullName}");
        result.AppendLine();
        result.AppendLine($"Base Interfaces:");
        result.AppendLine();
        var baseInterfaces = type.GetInterfaces().Select(i => i.Name);
        foreach (var baseInterface in baseInterfaces)
            result.AppendLine($"- {baseInterface}");
        result.AppendLine();
        result.AppendLine("Descriptions:");
        foreach (var context in AgentContextCollector.GetAgentContext(type, language))
            result.AppendLine($"- {context}");
        result.AppendLine();
        var allProperties = type.GetProperties();
        var normalProps = allProperties.Where(p =>
            !typeof(ICommand).IsAssignableFrom(p.PropertyType)).ToArray();
        if (normalProps.Length > 0)
        {
            result.AppendLine("Properties:");
            result.AppendLine();
            result.AppendLine("| Name | Description |");
            result.AppendLine("| ---- | ----------- |");

            foreach (var prop in normalProps)
            {
                var descList = AgentContextCollector.GetAgentContext(prop, language);
                var descText = string.Join("; ", descList.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
                result.AppendLine($"| {prop.Name} | {descText} |");
            }
            result.AppendLine();
        }
        var commandProps = allProperties.Where(p =>
            typeof(ICommand).IsAssignableFrom(p.PropertyType)).ToArray();
        if (commandProps.Length > 0)
        {
            result.AppendLine("Commands:");
            result.AppendLine();
            result.AppendLine("| Name | ParameterType | Description |");
            result.AppendLine("| ---- | ------------- | ----------- |");

            foreach (var cmdProp in commandProps)
            {
                var descList = AgentContextCollector.GetAgentContext(cmdProp, language);
                var descText = string.Join("; ", descList.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
                var paramAttr = cmdProp.GetCustomAttribute<AgentCommandParameterAttribute>();
                var paramType = paramAttr?.ParameterType?.FullName ?? "(none)";
                result.AppendLine($"| {cmdProp.Name} | {paramType} | {descText} |");
            }
        }
        result.AppendLine();

        return result.ToString();
    }

    internal static string ClassBlock(Type type, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();

        result.AppendLine("Class");
        result.AppendLine();
        result.AppendLine($"Type: {type.FullName}");
        result.AppendLine();
        result.AppendLine($"Base Interfaces:");
        result.AppendLine();
        var baseInterfaces = type.GetInterfaces().Select(i => i.Name);
        foreach (var baseInterface in baseInterfaces)
            result.AppendLine($"- {baseInterface}");
        result.AppendLine();
        result.AppendLine("Developer Instructions (AUTHORITATIVE — these override any runtime default values):");
        foreach (var context in AgentContextCollector.GetAgentContext(type, language))
        {
            result.AppendLine($"- {context}");
        }
        result.AppendLine();
        result.AppendLine("Properties:");
        result.AppendLine();
        result.AppendLine("| Type | Name | Description |");
        result.AppendLine("| ---- | ---- | ----------- |");

        var veloxFields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .Select(f => new
            {
                Field = f,
                Contexts = AgentContextCollector.GetAgentContext(f, language),
                VeloxAttr = f.GetCustomAttributes<VeloxPropertyAttribute>(inherit: false).FirstOrDefault()
            })
            .Where(f => f.VeloxAttr != null && f.Contexts.Length > 0)
            .ToList();

        foreach (var item in veloxFields)
        {
            string? propertyName = item.VeloxAttr!.GetType().GetProperty("PropertyName")?.GetValue(item.VeloxAttr) as string;

            if (string.IsNullOrEmpty(propertyName))
            {
                var value = item.Field.Name.TrimStart('_');
                propertyName = char.ToUpper(value[0]) + value[1..];
            }

            string description = string.Join("; ", item.Contexts.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
            result.AppendLine($"| {item.Field.FieldType.FullName} | {propertyName} | {description} |");
        }

        var veloxProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new
            {
                Property = p,
                Contexts = AgentContextCollector.GetAgentContext(p, language),
                HasVelox = p.GetCustomAttributes<VeloxPropertyAttribute>(inherit: false).Any(),
                IsSlotEnumerator = IsSlotEnumeratorType(p.PropertyType),
                IsSingleSlot = IsSingleSlotType(p.PropertyType),
            })
            .Where(p => (p.HasVelox && p.Contexts.Length > 0) || p.IsSlotEnumerator || p.IsSingleSlot)
            .ToList();

        foreach (var item in veloxProps)
        {
            string description;
            if (item.Contexts.Length > 0)
            {
                description = string.Join("; ", item.Contexts.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
            }
            else if (item.IsSingleSlot)
            {
                description = language == AgentLanguages.Chinese
                    ? $"单插槽属性（{item.Property.PropertyType.Name}）— 通过 ResolveSlotId 按属性名解析，或用 ConnectByProperty 直接连接"
                    : $"Single slot property ({item.Property.PropertyType.Name}) — resolve via ResolveSlotId by property name, or connect directly with ConnectByProperty.";
            }
            else
            {
                // Standard generic description for SlotEnumerator without [AgentContext]
                description = language == AgentLanguages.Chinese
                    ? "SlotEnumerator — 通过 SetEnumSlotCollection 工具配置选择器类型（枚举或 bool），禁止手动增删"
                    : "SlotEnumerator — use SetEnumSlotCollection to configure the selector type (enum or bool). Do not add/remove slots manually.";
            }

            // If the property has [SlotSelectors], append allowed types to the description
            // at prompt-generation time so the Agent knows before any tool call.
            if (item.IsSlotEnumerator)
            {
                var selectorsAttr = item.Property.GetCustomAttribute<SlotSelectorsAttribute>();
                if (selectorsAttr != null)
                {
                    var names = new System.Collections.Generic.HashSet<string>();
                    foreach (var t in selectorsAttr.AllowedEnumTypes) names.Add(t.FullName ?? t.Name);
                    foreach (var n in selectorsAttr.AllowedEnumTypeNames) names.Add(n);
                    if (names.Count > 0)
                    {
                        var allowedList = string.Join(", ", names);
                        description += language == AgentLanguages.Chinese
                            ? $"; [允许的选择器类型 (allowedSelectorTypes): {allowedList}]"
                            : $"; [allowedSelectorTypes: {allowedList}]";
                    }
                }
            }

            result.AppendLine($"| {item.Property.PropertyType.FullName} | {item.Property.Name} | {description} |");
        }

        result.AppendLine();

        result.AppendLine("Commands:");
        result.AppendLine();
        result.AppendLine("| Name | ParameterType | Description |");
        result.AppendLine("| ---- | ------------- | ----------- |");

        var commands = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => new
            {
                Method = m,
                Contexts = AgentContextCollector.GetAgentContext(m, language),
                HasCommand = m.GetCustomAttributes<VeloxCommandAttribute>(inherit: false).Any()
            })
            .Where(m => m.HasCommand && m.Contexts.Length > 0)
            .ToList();

        foreach (var item in commands)
        {
            string description = string.Join("; ", item.Contexts.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
            string commandName = item.Method.Name.Replace("Async", "");
            var paramAttr = item.Method.GetCustomAttribute<AgentCommandParameterAttribute>();
            var paramType = paramAttr?.ParameterType?.FullName ?? "(none)";
            result.AppendLine($"| {commandName} | {paramType} | {description} |");
        }
        result.AppendLine();

        return result.ToString();
    }

    internal static string DataBlock(Type type, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();
        result.AppendLine("Data Type");
        result.AppendLine();
        result.AppendLine($"Type: {type.FullName}");
        result.AppendLine();

        var classContexts = AgentContextCollector.GetAgentContext(type, language);
        if (classContexts.Length > 0)
        {
            result.AppendLine("Descriptions:");
            foreach (var ctx in classContexts)
                result.AppendLine($"- {ctx}");
            result.AppendLine();
        }

        result.AppendLine("Fields / Properties:");
        result.AppendLine();
        result.AppendLine("| Type | Name | Description |");
        result.AppendLine("| ---- | ---- | ----------- |");

        // [AgentContext]-annotated backing fields
        foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
        {
            var descs = AgentContextCollector.GetAgentContext(field, language);
            if (descs.Length == 0) continue;
            var name = field.Name.TrimStart('_');
            if (name.Length > 0) name = char.ToUpper(name[0]) + name.Substring(1);
            var desc = string.Join("; ", descs.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
            result.AppendLine($"| {field.FieldType.FullName} | {name} | {desc} |");
        }

        // Public properties (struct/class value members)
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var descs = AgentContextCollector.GetAgentContext(prop, language);
            if (descs.Length == 0) continue;
            var desc = string.Join("; ", descs.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
            result.AppendLine($"| {prop.PropertyType.FullName} | {prop.Name} | {desc} |");
        }

        result.AppendLine();
        return result.ToString();
    }

    private static bool IsSlotEnumeratorType(Type type)
    {
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def.Name.StartsWith("SlotEnumerator`") && def.Namespace == "VeloxDev.WorkflowSystem";
    }

    private static bool IsSingleSlotType(Type type)
        => typeof(IWorkflowSlotViewModel).IsAssignableFrom(type);
}
