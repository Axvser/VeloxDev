namespace VeloxDev.WorkflowSystem.CSharp;

/// <summary>Creates a runtime host for a type that may not have a public default constructor.</summary>
public interface ICSharpObjectActivator
{
    bool TryCreate(Type hostType, out object? host);
}
