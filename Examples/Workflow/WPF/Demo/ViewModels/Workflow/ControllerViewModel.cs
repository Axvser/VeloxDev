using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.MVVM;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem.StandardEx;

namespace Demo.ViewModels;

[WorkflowBuilder.ViewModel.Node<WorkflowHelper.ViewModel.Node>]
public partial class ControllerViewModel
{
    private SlotViewModel? _outputSlot;

    public ControllerViewModel()
    {
        InitializeWorkflow();
        BroadcastMode = WorkflowBroadcastMode.BreadthFirst;
        ReverseBroadcastMode = WorkflowBroadcastMode.DepthFirst;
    }

    public System.Array BroadcastModes => System.Enum.GetValues<WorkflowBroadcastMode>();
    public bool HasOutputSlot => _outputSlot is not null;

    public SlotViewModel? OutputSlot
    {
        get => _outputSlot;
        set
        {
            if (ReferenceEquals(_outputSlot, value))
            {
                return;
            }

            var oldValue = _outputSlot;
            OnPropertyChanging(nameof(OutputSlot));
            oldValue?.GetHelper().Delete();
            _outputSlot = value;
            if (value is not null)
            {
                GetHelper().CreateSlot(value);
            }

            OnPropertyChanged(nameof(OutputSlot));
            OnPropertyChanged(nameof(HasOutputSlot));
        }
    }

    // 当前系统是否处于运行中
    [VeloxProperty] private bool isActive = false;

    // 流程种子负载
    [VeloxProperty] private string seedPayload = "demo-request-chain";

    // 打开系统
    [VeloxCommand]
    private async Task OpenWorkflow(object? parameters, CancellationToken ct)
    {
        if (IsActive) return;
        IsActive = true;
        try
        {
            if (Parent is TreeViewModel tree)
            {
                tree.ResetExecutionLog();
            }

            var context = NetworkFlowContext.Create(SeedPayload);
            await this.StandardBroadcastAsync(context, BroadcastMode, ct);
        }
        finally
        {
            IsActive = false;
        }
    }

    // 安全地终结整个工作流
    [VeloxCommand]
    private async Task CloseWorkflow(object? parameters, CancellationToken ct)
    {
        if (Parent is null) return;
        await Parent.GetHelper().CloseAsync();
    }
}
