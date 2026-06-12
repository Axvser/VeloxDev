namespace VeloxDev.WorkflowSystem.CSharp;

internal static class CSharpObjectMemberConfigurationTool
{
    internal static void Apply(
        object host,
        IEnumerable<ValueMember> values,
        IEnumerable<CollectionMember> collections,
        object? conversionParameter,
        IReadOnlyList<ICSharpObjectValueConverter> converters)
    {
        foreach (var member in values.Where(member => member.IsEnabled))
        {
            try
            {
                var accessor = CSharpObjectMemberAccessor.Resolve(
                    host,
                    member.Path,
                    true);
                var converted = CSharpObjectConversionTool.ConvertValue(
                    member.Value,
                    accessor.ValueType,
                    conversionParameter,
                    converters);
                accessor.SetValue(converted);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to configure value path '{member.Path}'.",
                    ex);
            }
        }

        foreach (var member in collections.Where(member => member.IsEnabled))
        {
            try
            {
                CSharpObjectCollectionTool.Apply(
                    host,
                    member,
                    conversionParameter,
                    converters);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to configure collection path '{member.Path}'.",
                    ex);
            }
        }
    }
}
