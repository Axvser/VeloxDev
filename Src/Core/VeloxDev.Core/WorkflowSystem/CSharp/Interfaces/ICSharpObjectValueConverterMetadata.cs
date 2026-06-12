namespace VeloxDev.WorkflowSystem.CSharp;

/// <summary>Declares which target types a converter can expose to string-bound UI.</summary>
public interface ICSharpObjectValueConverterMetadata
{
    bool CanConvert(Type targetType);
}
