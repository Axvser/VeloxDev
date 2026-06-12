using System.Reflection;

namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectMethodTool
{
    internal static async Task<object?> InvokeAsync(
        object host,
        string selectedMethod,
        IEnumerable<MethodMember> methods,
        object? workflowInput,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var configuration = methods.FirstOrDefault(method =>
            string.Equals(
                method.Signature,
                selectedMethod,
                StringComparison.Ordinal));
        if (configuration is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(selectedMethod)
                    ? "No host method has been selected."
                    : $"Selected method '{selectedMethod}' was not discovered.");
        }

        var methodInfo = host.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                string.Equals(
                    GetSignature(method),
                    configuration.Signature,
                    StringComparison.Ordinal));
        if (methodInfo is null)
        {
            throw new MissingMethodException(
                host.GetType().FullName,
                configuration.Signature);
        }

        if (!CSharpObjectMethodClassificationTool.TryClassify(
                methodInfo,
                out var role)
            || role != configuration.Role)
        {
            throw new InvalidOperationException(
                $"Method '{configuration.Signature}' is not a supported " +
                "CSharp workflow method.");
        }

        var parameters = methodInfo.GetParameters();
        object?[] arguments;
        if (role == CSharpObjectMethodRole.Start)
        {
            arguments = [];
        }
        else
        {
            var parameter = parameters[0];
            if (!CSharpObjectTypeTool.IsRuntimeValueCompatible(
                    workflowInput,
                    parameter.ParameterType))
            {
                throw new InvalidOperationException(
                    $"Workflow input is incompatible with parameter " +
                    $"'{parameter.Name}' " +
                    $"({CSharpObjectTypeTool.GetTypeName(parameter.ParameterType)}).");
            }

            arguments = [workflowInput];
        }

        object? invocation;
        try
        {
            invocation = methodInfo.Invoke(host, arguments);
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                $"Method '{configuration.Signature}' failed.",
                ex.InnerException ?? ex);
        }

        if (invocation is Task task)
        {
            await task.ConfigureAwait(false);
            if (role == CSharpObjectMethodRole.Terminal)
            {
                return null;
            }

            return task.GetType()
                .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(task);
        }

        if (invocation is not null)
        {
            var asTask = invocation.GetType().GetMethod(
                "AsTask",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (asTask?.Invoke(invocation, null) is Task valueTask)
            {
                await valueTask.ConfigureAwait(false);
                if (role == CSharpObjectMethodRole.Terminal)
                {
                    return null;
                }

                return valueTask.GetType()
                    .GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(valueTask);
            }
        }

        return invocation;
    }

    internal static bool TryCreateMember(
        MethodInfo method,
        out MethodMember member)
    {
        member = null!;
        if (!CSharpObjectMethodClassificationTool.TryClassify(
                method,
                out var role))
        {
            return false;
        }

        member = new MethodMember();
        PopulateMember(method, role, member);
        return true;
    }

    internal static bool TryNormalizeMember(
        Type hostType,
        MethodMember member)
    {
        var method = hostType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate => string.Equals(
                GetSignature(candidate),
                member.Signature,
                StringComparison.Ordinal));
        if (method is null
            || !CSharpObjectMethodClassificationTool.TryClassify(
                method,
                out var role))
        {
            return false;
        }

        PopulateMember(method, role, member);
        return true;
    }

    internal static string GetSignature(MethodInfo method)
        => $"{method.Name}({string.Join(",", method.GetParameters().Select(parameter => CSharpObjectTypeTool.GetTypeName(parameter.ParameterType)))})";

    internal static string GetDisplayName(MethodInfo method)
        => $"{method.Name}({string.Join(", ", method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .Select((parameter, index) =>
                $"{CSharpObjectTypeTool.GetSimpleTypeName(parameter.ParameterType)} " +
                $"{parameter.Name ?? $"arg{index}"}"))})";

    private static void PopulateMember(
        MethodInfo method,
        CSharpObjectMethodRole role,
        MethodMember member)
    {
        member.Name = method.Name;
        member.Signature = GetSignature(method);
        member.DisplayName = GetDisplayName(method);
        member.Role = role;
        member.Parameters.Clear();
        member.Return.Clear();

        var parameters = method.GetParameters();
        if (parameters.Length == 1)
        {
            var parameter = parameters[0];
            member.Parameters.Add(new ParameterItem
            {
                Position = 0,
                Name = parameter.Name ?? "arg0",
                ValueType = CSharpObjectTypeTool.GetTypeName(
                    parameter.ParameterType),
                UseWorkflowInput = true
            });
        }

        var returnInfo = GetReturnInfo(method.ReturnType);
        member.Return.Add(new ReturnItem
        {
            ValueType = returnInfo.ResultType is null
                ? string.Empty
                : CSharpObjectTypeTool.GetTypeName(returnInfo.ResultType),
            IsVoid = returnInfo.ResultType is null,
            IsAsync = returnInfo.IsAsync
        });
    }

    internal static (Type? ResultType, bool IsAsync) GetReturnInfo(
        Type returnType)
    {
        if (returnType == typeof(void)) return (null, false);
        if (returnType == typeof(Task)) return (null, true);
        if (returnType.IsGenericType
            && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return (returnType.GetGenericArguments()[0], true);
        }

        if (returnType.FullName == "System.Threading.Tasks.ValueTask")
        {
            return (null, true);
        }

        if (returnType.IsGenericType
            && returnType.GetGenericTypeDefinition().FullName
                == "System.Threading.Tasks.ValueTask`1")
        {
            return (returnType.GetGenericArguments()[0], true);
        }

        return (returnType, false);
    }
}
