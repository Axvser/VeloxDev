using System.Reflection;

namespace VeloxDev.AI;

/// <summary>
/// Provides generic reflection-based method discovery and invocation for Agent scenarios.
/// Framework-agnostic — works with any .NET object.
/// </summary>
public static class AgentMethodInvoker
{
    /// <summary>
    /// Describes a method on an object for Agent consumption.
    /// </summary>
    public sealed class MethodDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public Type ReturnType { get; set; } = typeof(void);
        public IReadOnlyList<ParameterDescriptor> Parameters { get; set; } = [];
        public bool IsStatic { get; set; }
        public IReadOnlyList<string> AgentDescriptions { get; set; } = [];
    }

    /// <summary>
    /// Describes a method parameter.
    /// </summary>
    public sealed class ParameterDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public Type ParameterType { get; set; } = typeof(object);
        public bool IsOptional { get; set; }
        public object? DefaultValue { get; set; }
    }

    /// <summary>
    /// Result of a method invocation.
    /// </summary>
    public sealed class InvokeResult
    {
        public bool Success { get; set; }
        public object? ReturnValue { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Discovers public methods on the target object, excluding property accessors and
    /// common <see cref="object"/> methods (ToString, GetHashCode, Equals, GetType).
    /// </summary>
    /// <param name="target">The object to inspect.</param>
    /// <param name="language">Language for <see cref="AgentContextAttribute"/> lookup.</param>
    /// <param name="includeStatic">Whether to include static methods.</param>
    /// <param name="filter">Optional predicate to exclude methods.</param>
    public static IReadOnlyList<MethodDescriptor> DiscoverMethods(
        object target,
        AgentLanguages language = AgentLanguages.English,
        bool includeStatic = false,
        Func<MethodInfo, bool>? filter = null)
    {
        if (target == null) return [];

        var type = target.GetType();
        var flags = BindingFlags.Public | BindingFlags.Instance;
        if (includeStatic) flags |= BindingFlags.Static;

        var result = new List<MethodDescriptor>();
        var objectMethods = new HashSet<string> { "ToString", "GetHashCode", "Equals", "GetType" };

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName) continue; // skip property accessors, event add/remove
            if (objectMethods.Contains(method.Name)) continue;
            if (filter != null && !filter(method)) continue;

            var desc = new MethodDescriptor
            {
                Name = method.Name,
                ReturnType = method.ReturnType,
                IsStatic = method.IsStatic,
                AgentDescriptions = [.. method.GetCustomAttributes<AgentContextAttribute>(inherit: false)
                    .Where(a => a.Language == language)
                    .Select(a => a.Context)],
                Parameters = [.. method.GetParameters().Select(p => new ParameterDescriptor
                {
                    Name = p.Name ?? string.Empty,
                    ParameterType = p.ParameterType,
                    IsOptional = p.IsOptional,
                    DefaultValue = p.HasDefaultValue ? p.DefaultValue : null,
                })],
            };

            result.Add(desc);
        }

        return result;
    }

    /// <summary>
    /// Invokes a named public method on the target object with the given arguments.
    /// Supports overload resolution by parameter count.
    /// </summary>
    /// <param name="target">The object on which to invoke the method.</param>
    /// <param name="methodName">The name of the method.</param>
    /// <param name="args">Arguments to pass. If <c>null</c>, invokes with no arguments.</param>
    public static InvokeResult Invoke(object target, string methodName, params object?[]? args)
    {
        if (target == null)
            return new InvokeResult { Error = "Target is null." };

        var type = target.GetType();
        args ??= [];

        // Find best matching method by name and parameter count
        var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == methodName && !m.IsSpecialName)
            .ToArray();

        if (candidates.Length == 0)
            return new InvokeResult { Error = $"Method '{methodName}' not found on type '{type.FullName}'." };

        MethodInfo? best = candidates.FirstOrDefault(m => m.GetParameters().Length == args.Length)
                        ?? candidates.FirstOrDefault(m => m.GetParameters().Count(p => !p.IsOptional) <= args.Length
                                                       && m.GetParameters().Length >= args.Length);

        if (best == null)
            return new InvokeResult { Error = $"No overload of '{methodName}' matches {args.Length} argument(s)." };

        try
        {
            // Pad with defaults if needed
            var parameters = best.GetParameters();
            if (args.Length < parameters.Length)
            {
                var padded = new object?[parameters.Length];
                Array.Copy(args, padded, args.Length);
                for (int i = args.Length; i < parameters.Length; i++)
                    padded[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
                args = padded;
            }

            // Attempt type conversion for each argument
            for (int i = 0; i < args.Length && i < parameters.Length; i++)
            {
                if (args[i] != null && !parameters[i].ParameterType.IsAssignableFrom(args[i]!.GetType()))
                {
                    try { args[i] = Convert.ChangeType(args[i], parameters[i].ParameterType); }
                    catch { /* let it fail at invoke time */ }
                }
            }

            var returnValue = best.Invoke(target, args);
            return new InvokeResult { Success = true, ReturnValue = returnValue };
        }
        catch (TargetInvocationException ex)
        {
            return new InvokeResult { Error = $"Method '{methodName}' threw: {ex.InnerException?.Message ?? ex.Message}" };
        }
        catch (Exception ex)
        {
            return new InvokeResult { Error = $"Failed to invoke '{methodName}': {ex.Message}" };
        }
    }

    /// <summary>
    /// Invokes a named static method on the specified type.
    /// </summary>
    public static InvokeResult InvokeStatic(Type type, string methodName, params object?[]? args)
    {
        if (type == null)
            return new InvokeResult { Error = "Type is null." };

        args ??= [];

        var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName && !m.IsSpecialName)
            .ToArray();

        if (candidates.Length == 0)
            return new InvokeResult { Error = $"Static method '{methodName}' not found on type '{type.FullName}'." };

        var best = candidates.FirstOrDefault(m => m.GetParameters().Length == args.Length)
                ?? candidates.FirstOrDefault();

        if (best == null)
            return new InvokeResult { Error = $"No overload of static '{methodName}' matches {args.Length} argument(s)." };

        try
        {
            var returnValue = best.Invoke(null, args);
            return new InvokeResult { Success = true, ReturnValue = returnValue };
        }
        catch (TargetInvocationException ex)
        {
            return new InvokeResult { Error = $"Static method '{methodName}' threw: {ex.InnerException?.Message ?? ex.Message}" };
        }
        catch (Exception ex)
        {
            return new InvokeResult { Error = $"Failed to invoke static '{methodName}': {ex.Message}" };
        }
    }
}
