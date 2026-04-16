using System.Reflection;
using System.Windows.Input;

namespace VeloxDev.AI;

/// <summary>
/// Provides generic <see cref="ICommand"/> discovery and execution for Agent scenarios.
/// Framework-agnostic — works with any object that exposes <see cref="ICommand"/> properties,
/// including MVVM ViewModels, workflow nodes, or any custom component.
/// </summary>
public static class AgentCommandDiscoverer
{
    /// <summary>
    /// Describes a discovered command property on an object.
    /// </summary>
    public sealed class CommandDescriptor
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The expected parameter type from <see cref="AgentCommandParameterAttribute"/>,
        /// or <c>null</c> if the command takes no parameter.
        /// </summary>
        public Type? ParameterType { get; set; }

        /// <summary>
        /// Agent context descriptions (from <see cref="AgentContextAttribute"/>) for the command.
        /// </summary>
        public IReadOnlyList<string> AgentDescriptions { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Whether CanExecute currently returns true (checked with null parameter if no ParameterType).
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
    /// Discovers all <see cref="ICommand"/>-typed properties on the target object,
    /// including both interface-declared and concrete type properties.
    /// Reads <see cref="AgentCommandParameterAttribute"/> and <see cref="AgentContextAttribute"/>.
    /// </summary>
    /// <param name="target">The object to inspect.</param>
    /// <param name="language">Language for <see cref="AgentContextAttribute"/> lookup.</param>
    public static IReadOnlyList<CommandDescriptor> DiscoverCommands(
        object target,
        AgentLanguages language = AgentLanguages.English)
    {
        if (target == null) return Array.Empty<CommandDescriptor>();

        var type = target.GetType();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CommandDescriptor>();

        // Scan interface properties first (these carry the authoritative attributes)
        foreach (var iface in type.GetInterfaces())
        {
            foreach (var prop in iface.GetProperties())
            {
                if (!typeof(ICommand).IsAssignableFrom(prop.PropertyType)) continue;
                if (!seen.Add(prop.Name)) continue;

                var paramAttr = prop.GetCustomAttribute<AgentCommandParameterAttribute>();
                var descriptions = prop.GetCustomAttributes<AgentContextAttribute>(inherit: false)
                    .Where(a => a.Language == language)
                    .Select(a => a.Context)
                    .ToArray();

                var command = GetCommandInstance(target, type, prop.Name);

                result.Add(new CommandDescriptor
                {
                    Name = prop.Name,
                    ParameterType = paramAttr?.ParameterType,
                    AgentDescriptions = descriptions,
                    CanExecute = command != null && TryCanExecute(command, paramAttr?.ParameterType),
                });
            }
        }

        // Scan concrete type properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(ICommand).IsAssignableFrom(prop.PropertyType)) continue;
            if (!seen.Add(prop.Name)) continue;

            var paramAttr = FindParameterAttribute(type, prop.Name);
            var descriptions = prop.GetCustomAttributes<AgentContextAttribute>(inherit: false)
                .Where(a => a.Language == language)
                .Select(a => a.Context)
                .ToArray();

            var command = GetCommandInstance(target, type, prop.Name);

            result.Add(new CommandDescriptor
            {
                Name = prop.Name,
                ParameterType = paramAttr?.ParameterType,
                AgentDescriptions = descriptions,
                CanExecute = command != null && TryCanExecute(command, paramAttr?.ParameterType),
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
    public static ExecuteResult Execute(object target, string commandName, object? parameter = null)
    {
        var normalized = NormalizeCommandName(commandName);
        var result = new ExecuteResult { CommandName = normalized };

        if (target == null)
        {
            result.Error = "Target is null.";
            return result;
        }

        var type = target.GetType();
        var command = GetCommandInstance(target, type, normalized);

        if (command == null)
        {
            result.Error = $"Command '{normalized}' not found or is null on type '{type.FullName}'.";
            return result;
        }

        try
        {
            command.Execute(parameter);
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
    public static bool CanExecuteCommand(object target, string commandName, object? parameter = null)
    {
        if (target == null) return false;
        var normalized = NormalizeCommandName(commandName);
        var command = GetCommandInstance(target, target.GetType(), normalized);
        if (command == null) return false;

        try { return command.CanExecute(parameter); }
        catch { return false; }
    }

    /// <summary>
    /// Checks whether a property has a backing command (e.g. "Title" → "SetTitleCommand" or "TitleCommand").
    /// </summary>
    /// <param name="type">The type to search.</param>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>The command property name if found, <c>null</c> otherwise.</returns>
    public static string? FindBackingCommand(Type type, string propertyName)
    {
        var candidates = new[] { $"Set{propertyName}Command", $"{propertyName}Command" };

        foreach (var cmdName in candidates)
        {
            var cmdProp = type.GetProperty(cmdName, BindingFlags.Public | BindingFlags.Instance);
            if (cmdProp != null && typeof(ICommand).IsAssignableFrom(cmdProp.PropertyType))
                return cmdName;

            foreach (var iface in type.GetInterfaces())
            {
                cmdProp = iface.GetProperty(cmdName);
                if (cmdProp != null && typeof(ICommand).IsAssignableFrom(cmdProp.PropertyType))
                    return cmdName;
            }
        }

        return null;
    }

    // ── Helpers ──

    private static string NormalizeCommandName(string name)
        => name.EndsWith("Command") ? name : name + "Command";

    private static ICommand? GetCommandInstance(object target, Type type, string commandName)
    {
        // Search concrete type
        var prop = type.GetProperty(commandName, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && typeof(ICommand).IsAssignableFrom(prop.PropertyType))
            return prop.GetValue(target) as ICommand;

        // Search interfaces
        foreach (var iface in type.GetInterfaces())
        {
            prop = iface.GetProperty(commandName);
            if (prop != null && typeof(ICommand).IsAssignableFrom(prop.PropertyType))
            {
                var concreteProp = type.GetProperty(commandName, BindingFlags.Public | BindingFlags.Instance);
                return concreteProp?.GetValue(target) as ICommand;
            }
        }

        return null;
    }

    private static AgentCommandParameterAttribute? FindParameterAttribute(Type type, string commandName)
    {
        // Check interfaces
        foreach (var iface in type.GetInterfaces())
        {
            var prop = iface.GetProperty(commandName);
            var attr = prop?.GetCustomAttribute<AgentCommandParameterAttribute>();
            if (attr != null) return attr;
        }

        // Check concrete property
        var concreteProp = type.GetProperty(commandName, BindingFlags.Public | BindingFlags.Instance);
        var concreteAttr = concreteProp?.GetCustomAttribute<AgentCommandParameterAttribute>();
        if (concreteAttr != null) return concreteAttr;

        // Check backing method
        var methodName = commandName.Replace("Command", "");
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        return method?.GetCustomAttribute<AgentCommandParameterAttribute>();
    }

    private static bool TryCanExecute(ICommand command, Type? paramType)
    {
        try { return command.CanExecute(paramType == null ? null : (object?)null); }
        catch { return false; }
    }
}
