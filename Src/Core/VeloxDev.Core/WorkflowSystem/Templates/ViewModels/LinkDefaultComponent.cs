using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>The default implementation of <see cref="ILinkComponent"/>.</summary>
public sealed partial class LinkDefaultComponent : IConnectionPreviewLink, IRuntimeIdentifiable
{
    private ILinkHelper helper = new LinkHelper();
    private ConnectionPreviewState preview = ConnectionPreviewState.None;

    /// <summary>The helper that carries this component's behaviour.</summary>
    public ILinkHelper Helper
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
    public LinkDefaultComponent() { InitializeWorkflow(); }

    [VeloxProperty] private ISlotComponent sender = new SlotDefaultComponent();
    [VeloxProperty] private ISlotComponent receiver = new SlotDefaultComponent();
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
    public ILinkHelper GetHelper() => Helper;
    /// <inheritdoc />
    public ConnectionPreviewState Preview => preview;
    /// <inheritdoc />
    public void SetPreview(ConnectionPreviewState state)
    {
        if (preview == state) return;
        preview = state;
        OnPropertyChanged(nameof(Preview));
    }
    /// <inheritdoc />
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    /// <inheritdoc />
    public void SetHelper(ILinkHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
