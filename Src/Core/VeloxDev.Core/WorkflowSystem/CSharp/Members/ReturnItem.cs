using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.CSharp;

public sealed partial class ReturnItem
{
    [VeloxProperty] private string valueType = string.Empty;
    [VeloxProperty] private bool isVoid;
    [VeloxProperty] private bool isAsync;
}
