namespace VeloxDev.AI;

/// <summary>
/// Describes and invokes an object's methods through the compiled agent context tree.
/// </summary>
/// <remarks>
/// <para>
/// Framework-agnostic — works with any object the tree carries an entry for. Nothing here reflects: the method
/// list, its return type, its parameters and their optionality were recorded when the declaring assembly was
/// compiled, and the call goes through that type's generated <see cref="IAIContextAccessor"/>.
/// </para>
/// <para>
/// <see cref="Invoke"/> matches an overload by argument count. A call that omits trailing optional parameters is
/// completed by the generated call site, which leaves them out and lets the compiler supply the declared defaults.
/// </para>
/// </remarks>
public static class AgentMethodInvoker
{
    /// <summary>
    /// Describes a method on an object for Agent consumption.
    /// </summary>
    public sealed class MethodDescriptor
    {
        /// <summary>The method's name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The return type's full name as the tree records it — e.g. <c>System.Int32</c>, or <c>System.Void</c>.
        /// </summary>
        public string ReturnType { get; set; } = "System.Void";

        /// <summary>The method's parameters.</summary>
        public IReadOnlyList<ParameterDescriptor> Parameters { get; set; } = [];
        /// <summary>The <c>[AgentContext]</c> descriptions for this method.</summary>
        public IReadOnlyList<string> AgentDescriptions { get; set; } = [];
    }

    /// <summary>
    /// Describes a method parameter.
    /// </summary>
    public sealed class ParameterDescriptor
    {
        /// <summary>The parameter's name.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>The parameter type's full name as the tree records it.</summary>
        public string ParameterType { get; set; } = "System.Object";
        /// <summary>Whether the parameter is optional.</summary>
        public bool IsOptional { get; set; }
    }

    /// <summary>
    /// Result of a method invocation.
    /// </summary>
    public sealed class InvokeResult
    {
        /// <summary>Whether the call ran successfully.</summary>
        public bool Success { get; set; }
        /// <summary>The value the method returned, when <see cref="Success"/>.</summary>
        public object? ReturnValue { get; set; }
        /// <summary>The reason the call did not run, when not <see cref="Success"/>.</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Discovers the public instance methods the tree records for the target object, including the ones it
    /// inherits. Property accessors and the common <see cref="object"/> methods are not in the tree to begin with.
    /// </summary>
    /// <param name="target">The object to inspect.</param>
    /// <param name="language">Language for <c>[AgentContext]</c> lookup.</param>
    /// <param name="filter">Optional predicate over the method name (return <c>false</c> to skip).</param>
    /// <returns>The descriptors; empty when the target is <c>null</c> or its type has no entry in the tree.</returns>
    /// <remarks>
    /// One descriptor per method name. The tree keys a member by name, so two overloads of one name cannot both
    /// appear; <see cref="Invoke"/> still reaches every one of them by argument count.
    /// </remarks>
    public static IReadOnlyList<MethodDescriptor> DiscoverMethods(
        object target,
        AgentLanguages language = AgentLanguages.English,
        Func<string, bool>? filter = null)
    {
        if (AIContextMembers.TypeNameOf(target) is not { } typeName) return [];

        var result = new List<MethodDescriptor>();

        foreach (var node in AIContextDirectory.Shared.MembersAcross(typeName, "Methods"))
        {
            if (filter != null && !filter(node.Name)) continue;

            result.Add(new MethodDescriptor
            {
                Name = node.Name,
                ReturnType = node.TypeName ?? string.Empty,
                AgentDescriptions = AIContextMembers.DescriptionsFor(node, language),
                Parameters = [.. node.Children.Select(p => new ParameterDescriptor
                {
                    Name = p.Name,
                    ParameterType = p.TypeName ?? string.Empty,
                    IsOptional = p.Has(AIContextFlags.Optional),
                })],
            });
        }

        return result;
    }

    /// <summary>
    /// Invokes a named method on the target object with the given arguments.
    /// </summary>
    /// <param name="target">The object on which to invoke the method.</param>
    /// <param name="methodName">The name of the method.</param>
    /// <param name="args">Arguments to pass. If <c>null</c>, invokes with no arguments.</param>
    /// <returns>The result, carrying the reason when the call did not run.</returns>
    public static InvokeResult Invoke(object target, string methodName, params object?[]? args)
    {
        if (target == null)
            return new InvokeResult { Error = "Target is null." };

        if (AIContextMembers.TypeNameOf(target) is not { } typeName)
            return new InvokeResult { Error = $"Type of '{methodName}' target is not in the agent context tree." };

        var node = AIContextMembers.Find(typeName, "Methods", methodName);
        if (node == null)
            return new InvokeResult { Error = $"Method '{methodName}' is not in the agent context tree for type '{typeName}'." };

        var accessor = AIContextMembers.AccessorFor(node);
        if (accessor == null)
            return new InvokeResult { Error = $"No accessor is registered for type '{node.OwnerTypeName}'." };

        if (!accessor.TryInvoke(target, methodName, args ?? [], out var returnValue, out var error))
            return new InvokeResult { Error = error };

        return new InvokeResult { Success = true, ReturnValue = returnValue };
    }
}
