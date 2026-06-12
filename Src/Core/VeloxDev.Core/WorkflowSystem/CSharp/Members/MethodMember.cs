using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem.CSharp;

public sealed partial class MethodMember
{
    [VeloxProperty] private string name = string.Empty;
    [VeloxProperty] private string signature = string.Empty;
    [VeloxProperty] private string displayName = string.Empty;
    [VeloxProperty] private CSharpObjectMethodRole role;
    [VeloxProperty] public partial ObservableCollection<ReturnItem> Return { get; set; }
    [VeloxProperty] private ObservableCollection<ParameterItem> parameters = null!;

    internal CSharpObject? Parent { get; set; }

    public bool AcceptsWorkflowInput
        => Role is CSharpObjectMethodRole.Intermediate
            or CSharpObjectMethodRole.Terminal;

    public bool ProducesWorkflowOutput
        => Role is CSharpObjectMethodRole.Start
            or CSharpObjectMethodRole.Intermediate;

    public MethodMember()
    {
        Return = [];
        Parameters = [];
    }

    partial void OnRoleChanged(
        CSharpObjectMethodRole oldValue,
        CSharpObjectMethodRole newValue)
    {
        OnPropertyChanged(nameof(AcceptsWorkflowInput));
        OnPropertyChanged(nameof(ProducesWorkflowOutput));
    }
}
