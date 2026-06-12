namespace VeloxDev.WorkflowSystem.CSharp;

/// <summary>Converts a UI-bound string into a runtime member value.</summary>
public interface ICSharpObjectValueConverter
{
    bool TryConvert(
        string value,
        Type targetType,
        object? parameter,
        out object? result);
}
