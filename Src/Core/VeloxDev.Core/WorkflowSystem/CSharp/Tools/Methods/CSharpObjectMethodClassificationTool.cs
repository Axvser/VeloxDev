using System.Reflection;

namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectMethodClassificationTool
{
    internal static bool TryClassify(
        MethodInfo method,
        out CSharpObjectMethodRole role)
    {
        role = CSharpObjectMethodRole.None;

        if (method.IsSpecialName
            || method.ContainsGenericParameters
            || method.DeclaringType == typeof(object))
        {
            return false;
        }

        var parameters = method.GetParameters();
        if (parameters.Length > 1
            || parameters.Any(parameter =>
                parameter.ParameterType == typeof(CancellationToken)
                || parameter.ParameterType.IsByRef
                || parameter.IsOut
                || parameter.ParameterType.IsPointer))
        {
            return false;
        }

        var returnInfo = CSharpObjectMethodTool.GetReturnInfo(method.ReturnType);
        if (parameters.Length == 0)
        {
            if (returnInfo.ResultType is null || returnInfo.IsAsync)
            {
                return false;
            }

            role = CSharpObjectMethodRole.Start;
            return true;
        }

        role = returnInfo.ResultType is null
            ? CSharpObjectMethodRole.Terminal
            : CSharpObjectMethodRole.Intermediate;
        return true;
    }
}
