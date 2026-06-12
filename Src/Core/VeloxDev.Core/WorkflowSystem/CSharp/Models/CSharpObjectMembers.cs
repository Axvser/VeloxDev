namespace VeloxDev.WorkflowSystem.CSharp;

public sealed class CSharpObjectMembers
{
    public CSharpObjectMembers(
        IEnumerable<ValueMember>? values = null,
        IEnumerable<CollectionMember>? collections = null,
        IEnumerable<MethodMember>? methods = null)
    {
        Values = values?.ToArray() ?? [];
        Collections = collections?.ToArray() ?? [];
        Methods = methods?.ToArray() ?? [];
    }

    public IReadOnlyList<ValueMember> Values { get; }
    public IReadOnlyList<CollectionMember> Collections { get; }
    public IReadOnlyList<MethodMember> Methods { get; }
}
