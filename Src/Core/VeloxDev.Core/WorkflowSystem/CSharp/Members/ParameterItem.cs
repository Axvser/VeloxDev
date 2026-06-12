using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.CSharp;

public sealed partial class ParameterItem
{
    [VeloxProperty] private int position = -1;
    [VeloxProperty] private string name = string.Empty;
    [VeloxProperty] private string valueType = string.Empty;
    [VeloxProperty] private bool useWorkflowInput;
}
