namespace VeloxDev.AI;

/// <summary>
/// Provides generic <c>ICommand</c> discovery and execution for Agent scenarios, through the compiled agent
/// context tree.
/// </summary>
/// <remarks>
/// <para>
/// Framework-agnostic — works with any object the tree carries an entry for, including MVVM ViewModels, workflow
/// nodes, or any custom component. Nothing here reflects: which properties are commands, what they are described
/// as and what parameter they take were all recorded when the declaring assembly was compiled.
/// </para>
/// <para>
/// A command written on an interface is the shape the workflow runtime uses, and it is the interface's
/// annotations that count — the context tree generator copies them onto the implementing type's command when it
/// builds the entry, so a descriptor reads the same either way.
/// </para>
/// <para>
/// <c>CanExecute</c> is reported and never enforced: <see cref="Execute"/> does not consult it, so a caller that
/// wants to refuse a disabled command has to check <see cref="CommandDescriptor.CanExecute"/> first.
/// </para>
/// </remarks>
public static class AgentCommandDiscoverer
{
    /// <summary>
    /// Describes a discovered command property on an object.
    /// </summary>
    public sealed class CommandDescriptor
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The parameter type's full name from <c>[AgentCommandParameter]</c>, or <c>null</c> if the command takes
        /// no parameter.
        /// </summary>
        public string? ParameterType { get; set; }

        /// <summary>
        /// Agent context descriptions (from <c>[AgentContext]</c>) for the command.
        /// </summary>
        public IReadOnlyList<string> AgentDescriptions { get; set; } = [];

        /// <summary>
        /// Whether <c>CanExecute</c> currently returns true, checked with a null parameter.
        /// </summary>
        public bool CanExecute { get; set; }
    }

    /// <summary>
    /// Result of a command execution attempt.
    /// </summary>
    public sealed class ExecuteResult
    {
        public string CommandName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Discovers every <c>ICommand</c> property the tree records for the target object, including the ones it
    /// inherits.
    /// </summary>
    /// <param name="target">The object to inspect.</param>
    /// <param name="language">Language for <c>[AgentContext]</c> lookup.</param>
    /// <returns>The descriptors; empty when the target is <c>null</c> or its type has no entry in the tree.</returns>
    public static IReadOnlyList<CommandDescriptor> DiscoverCommands(
        object target,
        AgentLanguages language = AgentLanguages.English)
    {
        if (target == null) return [];
        if (AIContextMembers.TypeNameOf(target) is not { } typeName) return [];

        var result = new List<CommandDescriptor>();

        foreach (var node in AIContextDirectory.Shared.MembersAcross(typeName, "Commands"))
        {
            result.Add(new CommandDescriptor
            {
                Name = node.Name,
                ParameterType = AIContextMembers.ReferenceName(node, AIContextRefKind.CommandParameterType),
                AgentDescriptions = AIContextMembers.DescriptionsFor(node, language),
                CanExecute = CanExecute(target, node, null),
            });
        }

        return result;
    }

    /// <summary>
    /// Executes a named command on the target object.
    /// Automatically normalizes the command name (appends "Command" suffix if missing).
    /// </summary>
    /// <param name="target">The object that owns the command.</param>
    /// <param name="commandName">The command property name (e.g. "Delete" or "DeleteCommand").</param>
    /// <param name="parameter">The parameter to pass to Execute. Can be <c>null</c>.</param>
    /// <returns>The result, carrying the reason when the command did not run.</returns>
    /// <remarks>
    /// The command's own <c>CanExecute</c> is not consulted — a command that reports it cannot run is still run.
    /// </remarks>
    public static ExecuteResult Execute(object target, string commandName, object? parameter = null)
    {
        var normalized = NormalizeCommandName(commandName);
        var result = new ExecuteResult { CommandName = normalized };

        if (target == null)
        {
            result.Error = "Target is null.";
            return result;
        }

        if (AIContextMembers.TypeNameOf(target) is not { } typeName)
        {
            result.Error = $"Type of '{normalized}' target is not in the agent context tree.";
            return result;
        }

        var node = AIContextMembers.Find(typeName, "Commands", normalized);
        if (node == null)
        {
            result.Error = $"Command '{normalized}' is not in the agent context tree for type '{typeName}'.";
            return result;
        }

        var accessor = AIContextMembers.AccessorFor(node);
        if (accessor == null)
        {
            result.Error = $"No accessor is registered for type '{node.OwnerTypeName}'.";
            return result;
        }

        try
        {
            if (!accessor.TryExecuteCommand(target, normalized, parameter, out var error))
            {
                result.Error = error;
                return result;
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Error = $"Command '{normalized}' threw: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Checks whether a named command can execute with the given parameter.
    /// </summary>
    /// <param name="target">The object that owns the command.</param>
    /// <param name="commandName">The command property name (e.g. "Delete" or "DeleteCommand").</param>
    /// <param name="parameter">The parameter that would be passed. Can be <c>null</c>.</param>
    /// <returns><see langword="false"/> when the command is not in the tree or reports it cannot run.</returns>
    public static bool CanExecuteCommand(object target, string commandName, object? parameter = null)
    {
        if (target == null) return false;

        var normalized = NormalizeCommandName(commandName);
        if (AIContextMembers.TypeNameOf(target) is not { } typeName) return false;

        var node = AIContextMembers.Find(typeName, "Commands", normalized);
        return node != null && CanExecute(target, node, parameter);
    }

    /// <summary>
    /// Checks whether a property has a backing command (e.g. "Title" → "SetTitleCommand" or "TitleCommand").
    /// </summary>
    /// <param name="type">The type to search.</param>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>The command property name if found, <c>null</c> otherwise.</returns>
    public static string? FindBackingCommand(Type type, string propertyName)
    {
        if (type?.FullName is not { } typeName) return null;

        foreach (var candidate in new[] { $"Set{propertyName}Command", $"{propertyName}Command" })
        {
            if (AIContextMembers.Find(typeName, "Commands", candidate) != null) return candidate;
        }

        return null;
    }

    // ── Helpers ──

    private static string NormalizeCommandName(string name)
        => name.EndsWith("Command") ? name : name + "Command";

    /// <summary>Asks the declaring accessor whether a command node would run, never letting a throw escape.</summary>
    private static bool CanExecute(object target, AIContextNode node, object? parameter)
    {
        var accessor = AIContextMembers.AccessorFor(node);
        if (accessor == null) return false;

        try { return accessor.CanExecuteCommand(target, node.Name, parameter); }
        catch { return false; }
    }
}
