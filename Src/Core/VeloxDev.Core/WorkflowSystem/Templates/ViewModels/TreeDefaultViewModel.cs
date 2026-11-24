using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>The default implementation of <see cref="ITreeComponent"/>.</summary>
public sealed partial class TreeDefaultViewModel : ITreeComponent, IRuntimeIdentifiable
{
    private ITreeHelper helper = new TreeHelper();

    /// <summary>The helper that carries this component's behaviour.</summary>
    public ITreeHelper Helper
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
    public TreeDefaultViewModel() { InitializeWorkflow(); }

    [VeloxProperty] private CanvasLayout layout = new();
    [VeloxProperty] private ILinkComponent virtualLink = new LinkDefaultViewModel() { Sender = new SlotDefaultViewModel(), Receiver = new SlotDefaultViewModel() };
    [VeloxProperty] private ObservableCollection<INodeComponent> nodes = [];
    [VeloxProperty] private ObservableCollection<ILinkComponent> links = [];
    [VeloxProperty] private Dictionary<ISlotComponent, Dictionary<ISlotComponent, ILinkComponent>> linksMap = [];

    [VeloxCommand]
    private Task CreateNode(object? parameter, CancellationToken ct)
    {
        if (parameter is not INodeComponent node) return Task.CompletedTask;
        Helper.CreateNode(node);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SetPointer(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.SetPointer(anchor);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task ResetVirtualLink(object? parameter, CancellationToken ct)
    {
        Helper.ResetVirtualLink();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SendConnection(object? parameter, CancellationToken ct)
    {
        if (parameter is not ISlotComponent slot) return Task.CompletedTask;
        Helper.SendConnection(slot);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task ReceiveConnection(object? parameter, CancellationToken ct)
    {
        if (parameter is not ISlotComponent slot) return Task.CompletedTask;
        Helper.ReceiveConnection(slot);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Submit(object? parameter, CancellationToken ct)
    {
        if (parameter is not IActionPair actionPair) return Task.CompletedTask;
        Helper.Submit(actionPair);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Redo(object? parameter, CancellationToken ct)
    {
        Helper.Redo();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Undo(object? parameter, CancellationToken ct)
    {
        Helper.Undo();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }

    /// <inheritdoc />
    public ITreeHelper GetHelper() => Helper;
    /// <inheritdoc />
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    /// <inheritdoc />
    public void SetHelper(ITreeHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
