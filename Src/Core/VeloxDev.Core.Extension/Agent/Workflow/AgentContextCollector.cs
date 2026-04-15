using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using VeloxDev.MVVM;

namespace VeloxDev.AI.Workflow;

public static class AgentContextCollector
{
    public static string[] GetAgentContext(Type type, AgentLanguages language)
    {
        var contexts =
            type.GetCustomAttributes<AgentContextAttribute>(inherit: false)
            .Where(c => c.Language == language)
            .Select(c => c.Context)
            .ToArray();
        return contexts;
    }

    public static string[] GetAgentContext(MemberInfo member, AgentLanguages language)
    {
        var contexts =
            member.GetCustomAttributes<AgentContextAttribute>(inherit: false)
            .Where(c => c.Language == language)
            .Select(c => c.Context)
            .ToArray();
        return contexts;
    }

    public static string GetEnumContext(Type enumType, AgentLanguages language)
    {
        var result = new StringBuilder();

        result.AppendLine("---");
        result.AppendLine();

        result.AppendLine("Enum");
        result.AppendLine();
        result.AppendLine($"Type: {enumType.FullName}");
        result.AppendLine();
        result.AppendLine("Descriptions:");
        foreach (var context in GetAgentContext(enumType, language))
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
            var descriptions = GetAgentContext(field, language);
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

    public static string GetInterfaceContext(Type type, AgentLanguages language)
    {
        if (!type.IsInterface)
            throw new ArgumentException("Type must be an interface.", nameof(type));

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
        foreach (var context in GetAgentContext(type, language))
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
                var descList = GetAgentContext(prop, language);
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
            result.AppendLine("| Name | Description |");
            result.AppendLine("| ---- | ----------- |");

            foreach (var cmdProp in commandProps)
            {
                var descList = GetAgentContext(cmdProp, language);
                var descText = string.Join("; ", descList.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
                result.AppendLine($"| {cmdProp.Name} | {descText} |");
            }
        }
        result.AppendLine();

        return result.ToString();
    }

    public static string GetClassContext(Type type, AgentLanguages language)
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
        result.AppendLine("Descriptions:");
        foreach (var context in GetAgentContext(type, language))
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
                Contexts = GetAgentContext(f, language),
                VeloxAttr = f.GetCustomAttributes<VeloxPropertyAttribute>(inherit: false).FirstOrDefault()
            })
            .Where(f => f.VeloxAttr != null && f.Contexts.Length > 0)
            .ToList();

        foreach (var item in veloxFields)
        {
            string? propertyName = item.VeloxAttr.GetType().GetProperty("PropertyName")?.GetValue(item.VeloxAttr) as string;

            if (string.IsNullOrEmpty(propertyName))
            {
                var value = item.Field.Name.TrimStart('_');
                propertyName = char.ToUpper(value[0]) + value.Substring(1);
            }

            string description = string.Join("; ", item.Contexts.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
            result.AppendLine($"| {item.Field.FieldType.FullName} | {propertyName} | {description} |");
        }

        var veloxProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new
            {
                Property = p,
                Contexts = GetAgentContext(p, language),
                HasVelox = p.GetCustomAttributes<VeloxPropertyAttribute>(inherit: false).Any()
            })
            .Where(p => p.HasVelox && p.Contexts.Length > 0) // 必须同时有 Velox 和 Agent 标记
            .ToList();

        foreach (var item in veloxProps)
        {
            string description = string.Join("; ", item.Contexts.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
            result.AppendLine($"| {item.Property.PropertyType.FullName} | {item.Property.Name} | {description} |");
        }

        result.AppendLine();

        result.AppendLine("Commands:");
        result.AppendLine();
        result.AppendLine("| Name | Description |");
        result.AppendLine("| ---- | ----------- |");

        var commands = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => new
            {
                Method = m,
                Contexts = GetAgentContext(m, language),
                HasCommand = m.GetCustomAttributes<VeloxCommandAttribute>(inherit: false).Any()
            })
            .Where(m => m.HasCommand && m.Contexts.Length > 0)
            .ToList();

        foreach (var item in commands)
        {
            string description = string.Join("; ", item.Contexts.Select((ctx, i) => $"[{i + 1}-{ctx}]"));
            string commandName = item.Method.Name.Replace("Async", "");
            result.AppendLine($"| {commandName} | {description} |");
        }
        result.AppendLine();

        return result.ToString();
    }
}