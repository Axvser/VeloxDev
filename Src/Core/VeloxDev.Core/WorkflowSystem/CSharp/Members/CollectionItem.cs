using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.CSharp;

public sealed partial class CollectionItem
{
    [VeloxProperty] private int index = -1;
    [VeloxProperty] private string _value = string.Empty;

    internal CollectionMember? Parent { get; set; }

    [VeloxCommand]
    private void Delete() => Parent?.Items.Remove(this);
}
