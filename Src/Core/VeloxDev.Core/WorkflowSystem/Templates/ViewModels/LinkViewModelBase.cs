using VeloxDev.AI;
using VeloxDev.AOT;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Link组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Link component interface")]
[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public sealed partial class LinkViewModelBase : IWorkflowLinkViewModel, IWorkflowIdentifiable
{
    private IWorkflowLinkViewModelHelper helper = new LinkHelper();
    public IWorkflowLinkViewModelHelper Helper
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

    public LinkViewModelBase() { InitializeWorkflow(); }

    [VeloxProperty] private IWorkflowSlotViewModel sender = new SlotViewModelBase();
    [VeloxProperty] private IWorkflowSlotViewModel receiver = new SlotViewModelBase();
    [VeloxProperty] private bool isVisible = false;

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

    public IWorkflowLinkViewModelHelper GetHelper() => Helper;
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    public void SetHelper(IWorkflowLinkViewModelHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
