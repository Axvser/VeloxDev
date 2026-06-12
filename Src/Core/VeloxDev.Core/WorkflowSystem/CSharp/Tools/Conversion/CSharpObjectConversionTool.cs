namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectConversionTool
{
    private static readonly ICSharpObjectValueConverter DefaultConverter =
        new DefaultCSharpObjectValueConverter();

    internal static object ConvertValue(
        string value,
        Type targetType,
        object? parameter,
        IReadOnlyList<ICSharpObjectValueConverter> converters)
    {
        foreach (var converter in converters)
        {
            if (converter.TryConvert(
                    value,
                    targetType,
                    parameter,
                    out var result))
            {
                return ValidateConvertedValue(result, targetType);
            }
        }

        if (DefaultConverter.TryConvert(
                value,
                targetType,
                parameter,
                out var fallback))
        {
            return ValidateConvertedValue(fallback, targetType);
        }

        throw new InvalidOperationException(
            $"Unable to convert '{value}' to '{CSharpObjectTypeTool.GetTypeName(targetType)}'.");
    }

    private static object ValidateConvertedValue(
        object? value,
        Type targetType)
    {
        if (value is null)
        {
            if (!CSharpObjectTypeTool.CanAcceptNull(targetType))
            {
                throw new InvalidOperationException(
                    $"Converter returned null for non-nullable type " +
                    $"'{CSharpObjectTypeTool.GetTypeName(targetType)}'.");
            }

            return null!;
        }

        var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!actualType.IsInstanceOfType(value))
        {
            throw new InvalidOperationException(
                $"Converter returned '{value.GetType().FullName}' for " +
                $"'{CSharpObjectTypeTool.GetTypeName(targetType)}'.");
        }

        return value;
    }
}
