using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>The default implementation of <see cref="IWorkflowLinkViewModel"/>.</summary>
[AgentContext(AgentLanguages.Chinese, "工作流Link组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Link component interface")]
public sealed partial class LinkDefaultViewModel : IWorkflowLinkViewModel, IWorkflowIdentifiable
{
    private IWorkflowLinkViewModelHelper helper = new LinkHelper();

    /// <summary>The helper that carries this component's behaviour.</summary>
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

    /// <inheritdoc />
    public string RuntimeId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Creates the component and installs its default helper.</summary>
    public LinkDefaultViewModel() { InitializeWorkflow(); }

    [VeloxProperty] private IWorkflowSlotViewModel sender = new SlotDefaultViewModel();
    [VeloxProperty] private IWorkflowSlotViewModel receiver = new SlotDefaultViewModel();
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

    /// <inheritdoc />
    public IWorkflowLinkViewModelHelper GetHelper() => Helper;
    /// <inheritdoc />
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    /// <inheritdoc />
    public void SetHelper(IWorkflowLinkViewModelHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
