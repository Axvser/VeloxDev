using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Discovers and invokes <see cref="IVeloxCommand"/> properties on workflow components.
/// Supports parameter deserialization based on <see cref="AgentCommandParameterAttribute"/>.
/// </summary>
public static class CommandInvoker
{
    /// <summary>
    /// Discovers all <see cref="ICommand"/>-typed properties on a component,
    /// including their expected parameter types from <see cref="AgentCommandParameterAttribute"/>.
    /// </summary>
    public static IReadOnlyList<CommandDescriptor> DiscoverCommands(object component)
    {
        if (component == null) return Array.Empty<CommandDescriptor>();

        var result = new List<CommandDescriptor>();
        var type = component.GetType();

        // Scan interface command properties
        foreach (var iface in type.GetInterfaces())
        {
            foreach (var prop in iface.GetProperties())
            {
                if (!typeof(ICommand).IsAssignableFrom(prop.PropertyType)) continue;

                var paramAttr = prop.GetCustomAttribute<AgentCommandParameterAttribute>();
                var contexts = prop.GetCustomAttributes<AgentContextAttribute>()
                    .Select(a => new KeyValuePair<AgentLanguages, string>(a.Language, a.Context))
                    .ToList();

                result.Add(new CommandDescriptor
                {
                    Name = prop.Name,
                    ParameterType = paramAttr?.ParameterType,
                    Descriptions = contexts,
                });
            }
        }

        // Also scan the concrete type's own command properties (from [VeloxCommand])
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(ICommand).IsAssignableFrom(prop.PropertyType)) continue;
            if (result.Any(c => c.Name == prop.Name)) continue; // skip duplicates from interfaces

            var paramAttr = prop.GetCustomAttribute<AgentCommandParameterAttribute>();
            // Also check the backing method for VeloxCommand
            var methodName = prop.Name.Replace("Command", "");
            var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (paramAttr == null && method != null)
            {
                paramAttr = method.GetCustomAttribute<AgentCommandParameterAttribute>();
            }

            var contexts = AgentContextCollector.GetAgentContext(prop, AgentLanguages.English);

            result.Add(new CommandDescriptor
            {
                Name = prop.Name,
                ParameterType = paramAttr?.ParameterType,
                Descriptions = contexts.Select(c => new KeyValuePair<AgentLanguages, string>(AgentLanguages.English, c)).ToList(),
            });
        }

        return result;
    }

    /// <summary>
    /// Invokes a named command on a component, deserializing the JSON parameter
    /// to the type specified by <see cref="AgentCommandParameterAttribute"/>.
    /// </summary>
    public static string Invoke(object component, string commandName, string? jsonParameter)
    {
        if (component == null)
            return JsonConvert.SerializeObject(new { status = "error", message = "Component is null." });

        // Normalize command name
        if (!commandName.EndsWith("Command"))
            commandName += "Command";

        var type = component.GetType();
        var prop = FindCommandProperty(type, commandName);
        if (prop == null)
            return JsonConvert.SerializeObject(new { status = "error", message = $"Command '{commandName}' not found on type '{type.FullName}'." });

        var command = prop.GetValue(component) as ICommand;
        if (command == null)
            return JsonConvert.SerializeObject(new { status = "error", message = $"Command '{commandName}' is null." });

        // Resolve parameter
        object? parameter = null;
        var paramAttr = FindCommandParameterAttribute(type, commandName);
        var paramType = paramAttr?.ParameterType;

        if (paramType != null && !string.IsNullOrWhiteSpace(jsonParameter))
        {
            try
            {
                parameter = JsonConvert.DeserializeObject(jsonParameter, paramType);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { status = "error", message = $"Failed to deserialize parameter as '{paramType.FullName}': {ex.Message}" });
            }
        }
        else if (!string.IsNullOrWhiteSpace(jsonParameter) && paramType == null)
        {
            // Pass raw string as parameter
            parameter = jsonParameter;
        }

        try
        {
            command.Execute(parameter);
            return JsonConvert.SerializeObject(new { status = "ok", message = $"Command '{commandName}' executed." });
        }
        catch (Exception ex)
        {
            return JsonConvert.SerializeObject(new { status = "error", message = $"Command execution failed: {ex.Message}" });
        }
    }

    private static PropertyInfo? FindCommandProperty(Type type, string commandName)
    {
        // Search on concrete type
        var prop = type.GetProperty(commandName, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && typeof(ICommand).IsAssignableFrom(prop.PropertyType))
            return prop;

        // Search on interfaces
        foreach (var iface in type.GetInterfaces())
        {
            prop = iface.GetProperty(commandName);
            if (prop != null && typeof(ICommand).IsAssignableFrom(prop.PropertyType))
                return prop;
        }

        return null;
    }

    private static AgentCommandParameterAttribute? FindCommandParameterAttribute(Type type, string commandName)
    {
        // Check property on interfaces
        foreach (var iface in type.GetInterfaces())
        {
            var prop = iface.GetProperty(commandName);
            var attr = prop?.GetCustomAttribute<AgentCommandParameterAttribute>();
            if (attr != null) return attr;
        }

        // Check property on concrete type
        var concreteProp = type.GetProperty(commandName, BindingFlags.Public | BindingFlags.Instance);
        var concreteAttr = concreteProp?.GetCustomAttribute<AgentCommandParameterAttribute>();
        if (concreteAttr != null) return concreteAttr;

        // Check backing method
        var methodName = commandName.Replace("Command", "");
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        return method?.GetCustomAttribute<AgentCommandParameterAttribute>();
    }
}

public class CommandDescriptor
{
    public string Name { get; set; } = string.Empty;
    public Type? ParameterType { get; set; }
    public IReadOnlyList<KeyValuePair<AgentLanguages, string>> Descriptions { get; set; } = Array.Empty<KeyValuePair<AgentLanguages, string>>();
}
