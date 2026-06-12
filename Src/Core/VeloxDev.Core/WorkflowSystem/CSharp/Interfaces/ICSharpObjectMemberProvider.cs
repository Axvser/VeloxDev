namespace VeloxDev.WorkflowSystem.CSharp;

/// <summary>Discovers configurable values, collections, and methods for a host type.</summary>
public interface ICSharpObjectMemberProvider
{
    bool TryGetMembers(Type hostType, out CSharpObjectMembers members);
}
