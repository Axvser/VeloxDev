using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>The default implementation of <see cref="ISlotComponent"/>.</summary>
public sealed partial class SlotDefaultViewModel : ISlotComponent, IRuntimeIdentifiable
{
    private ISlotHelper helper = new SlotHelper();

    /// <summary>The helper that carries this component's behaviour.</summary>
    public ISlotHelper Helper
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
    public SlotDefaultViewModel() { InitializeWorkflow(); }

    [VeloxProperty] private ObservableCollection<ISlotComponent> targets = [];
    [VeloxProperty] private ObservableCollection<ISlotComponent> sources = [];
    [VeloxProperty] private INodeComponent? parent = null;
    [VeloxProperty] private SlotChannel channel = SlotChannel.OneBoth;
    [VeloxProperty] private SlotState state = SlotState.StandBy;
    [VeloxProperty] private Anchor anchor = new(double.NaN, double.NaN, 0);

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

    /// <inheritdoc />
    public ISlotHelper GetHelper() => Helper;
    /// <inheritdoc />
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    /// <inheritdoc />
    public void SetHelper(ISlotHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
