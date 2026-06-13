using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.AOT;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Slot组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Slot component interface")]
[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public sealed partial class SlotViewModelBase : IWorkflowSlotViewModel, IWorkflowIdentifiable
{
    private IWorkflowSlotViewModelHelper helper = new SlotHelper();
    public IWorkflowSlotViewModelHelper Helper
    {
        get => helper;
        private set
        {
            if (ReferenceEquals(helper, value)) return;
            OnPropertyChanging(nameof(Helper));
            helper = value;
            OnPropertyChanged(nameof(Helper));
        }
    }

    public string RuntimeId { get; } = Guid.NewGuid().ToString("N");

    public SlotViewModelBase() { InitializeWorkflow(); }

    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> targets = [];
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> sources = [];
    [VeloxProperty] private IWorkflowNodeViewModel? parent = null;
    [VeloxProperty] private SlotChannel channel = SlotChannel.OneBoth;
    [VeloxProperty] private SlotState state = SlotState.StandBy;
    [VeloxProperty] private Anchor anchor = new();

    [VeloxCommand]
    private Task SetChannel(object? parameter, CancellationToken ct)
    {
        if (parameter is not SlotChannel slotChannel) return Task.CompletedTask;
        Helper.SetChannel(slotChannel);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SendConnection(object? parameter, CancellationToken ct)
    {
        Helper.SendConnection();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task ReceiveConnection(object? parameter, CancellationToken ct)
    {
        Helper.ReceiveConnection();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Delete(object? parameter, CancellationToken ct)
    {
        Helper.Delete();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }

    public IWorkflowSlotViewModelHelper GetHelper() => Helper;
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    public void SetHelper(IWorkflowSlotViewModelHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
