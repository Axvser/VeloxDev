using VeloxDev.AI;
using VeloxDev.AOT;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Link组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Link component interface")]
[AOTReflection(Constructors: true, Methods: true, Properties: true, Fields: true)]
public partial class LinkViewModelBase : IWorkflowLinkViewModel
{
    private IWorkflowLinkViewModelHelper Helper = new LinkHelper();

    public LinkViewModelBase() { InitializeWorkflow(); }

    [VeloxProperty] private IWorkflowSlotViewModel sender = new SlotViewModelBase();
    [VeloxProperty] private IWorkflowSlotViewModel receiver = new SlotViewModelBase();
    [VeloxProperty] private bool isVisible = false;

    [VeloxCommand]
    protected virtual Task Delete(object? parameter, CancellationToken ct)
    {
        Helper.Delete();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    protected virtual async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }

    public virtual IWorkflowLinkViewModelHelper GetHelper() => Helper;
    public virtual void InitializeWorkflow() => Helper.Install(this);
    public virtual void SetHelper(IWorkflowLinkViewModelHelper helper)
    {
        Helper.Uninstall(this);
        helper.Install(this);
        Helper = helper;
    }
}
