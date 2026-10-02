using System;
using VeloxDev.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Discovers and invokes the command properties of a workflow component, through its compiled context tree entry
/// and generated accessor.
/// </summary>
/// <remarks>
/// <para>
/// A command's parameter type is the one <c>[AgentCommandParameter]</c> declared on it, handed over as a
/// <c>typeof</c> literal by the accessor — the same shape the tree gives every declared type, and one the
/// trimmer can follow.
/// </para>
/// <para>
/// <see cref="Invoke"/> deserializes the model's JSON through the generated serializer, so the parameter's
/// type is one the archive format already knows rather than one looked up at run time.
/// </para>
/// </remarks>
public static class CommandInvoker
{
    /// <summary>
    /// Discovers the command properties the context tree records for a component, including the ones it
    /// inherits.
    /// </summary>
    /// <param name="component">The component to inspect.</param>
    /// <returns>The descriptors; empty when the component is <c>null</c> or its type is not in the tree.</returns>
    public static IReadOnlyList<CommandDescriptor> DiscoverCommands(object component)
    {
        var accessor = AIContextTreeRegistry.FindAccessor(component);
        if (accessor is null) return [];

        var result = new List<CommandDescriptor>();

        foreach (var node in AIContextDirectory.Shared.MembersAcross(accessor.TypeName, "Commands"))
        {
            result.Add(new CommandDescriptor
            {
                Name = node.Name,
                ParameterType = accessor.ParameterType(node.Name),
                Descriptions = [.. node.Descriptions.Select(static t => new KeyValuePair<AgentLanguages, string>(t.Language, t.Text))],
            });
        }

        return result;
    }

    /// <summary>
    /// Invokes a named command on a component, deserializing the JSON parameter to the type
    /// <c>[AgentCommandParameter]</c> declared for it. The command name gets a <c>Command</c> suffix when it
    /// does not already have one.
    /// </summary>
    /// <param name="component">The component that owns the command.</param>
    /// <param name="commandName">The command property name, with or without the <c>Command</c> suffix.</param>
    /// <param name="jsonParameter">The parameter as JSON, or <c>null</c>.</param>
    /// <returns>A JSON status object.</returns>
    /// <remarks>
    /// The command's own <c>CanExecute</c> is not consulted — a command that reports it cannot run is still run.
    /// </remarks>
    public static string Invoke(object component, string commandName, string? jsonParameter)
    {
        var accessor = AIContextTreeRegistry.FindAccessor(component);
        if (accessor is null)
            return Error("Component is null, or its type is not in the agent context tree.");

        // Normalize command name
        if (!commandName.EndsWith("Command"))
            commandName += "Command";

        var node = AIContextDirectory.Shared.MemberAcross(accessor.TypeName, "Commands", commandName);
        if (node is null)
            return Error($"Command '{commandName}' not found on type '{accessor.TypeName}'.");

        if (!accessor.TryGet(component, commandName, out var value) || value is not ICommand command)
            return Error($"Command '{commandName}' is null.");

        // Resolve parameter
        object? parameter = null;
        var paramType = accessor.ParameterType(commandName);

        if (paramType != null && !string.IsNullOrWhiteSpace(jsonParameter))
        {
            try
            {
                parameter = VeloxJsonSerializer.Deserialize(jsonParameter!, paramType);
            }
            catch (Exception ex)
            {
                return Error($"Failed to deserialize parameter as '{paramType.FullName}': {ex.Message}");
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
            return new VeloxJsonObject { ["status"] = "ok", ["message"] = $"Command '{commandName}' executed." }.ToJson();
        }
        catch (Exception ex)
        {
            return new VeloxJsonObject { ["status"] = "error", ["message"] = $"Command execution failed: {ex.Message}" }.ToJson();
        }
    }

    private static string Error(string message)
        => new VeloxJsonObject { ["status"] = "error", ["message"] = message }.ToJson();
}

/// <summary>
/// One command property a component exposes to the agent.
/// </summary>
public class CommandDescriptor
{
    /// <summary>The command property's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The parameter type <c>[AgentCommandParameter]</c> declared, or <see langword="null"/> when the command
    /// takes no parameter. A <c>typeof</c> literal from the accessor, not a reflection lookup.
    /// </summary>
    public Type? ParameterType { get; set; }

    /// <summary>The command's descriptions, each with the language it was written in.</summary>
    public IReadOnlyList<KeyValuePair<AgentLanguages, string>> Descriptions { get; set; } = [];
}
