using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Node组件接口的默认实现类")]
[AgentContext(AgentLanguages.English, "The default implementation class of the workflow Node component interface")]
public sealed partial class NodeDefaultViewModel : IWorkflowNodeViewModel, IWorkflowIdentifiable
{
    private IWorkflowNodeViewModelHelper helper = new NodeHelper();
    public IWorkflowNodeViewModelHelper Helper
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

    public NodeDefaultViewModel() { InitializeWorkflow(); }

    [VeloxProperty] private IWorkflowTreeViewModel? parent = null;
    private Anchor anchor = new();
    private Size size = new();
    [VeloxProperty] private ObservableCollection<IWorkflowSlotViewModel> slots = [];
    private readonly WorkflowNodeScaleTracker _scaleTracker = new();

    // Anchor/Size are hand-written (not [VeloxProperty]) so their getters can collapse toward the
    // world origin by the layout scale. The stored fields keep the original world values. The canvas
    // geometry is the same in both zoom modes — ViewportCenter zoom keeps the pivot under the viewport
    // center purely by scrolling, so the origin collapse here stays correct for both.
    public Anchor Anchor
    {
        get => anchor.Collapse(Parent?.Layout?.Scale);
        set
        {
            if (global::System.Object.Equals(anchor, value)) return;
            var old = anchor;
            OnPropertyChanging(nameof(Anchor));
            OnAnchorChanging(old, value);
            anchor = value;
            OnAnchorChanged(old, value);
            OnPropertyChanged(nameof(Anchor));
        }
    }
    partial void OnAnchorChanging(Anchor oldValue, Anchor newValue);
    partial void OnAnchorChanged(Anchor oldValue, Anchor newValue);

    public Size Size
    {
        get => size.Collapse(Parent?.Layout?.Scale);
        set
        {
            if (global::System.Object.Equals(size, value)) return;
            var old = size;
            OnPropertyChanging(nameof(Size));
            OnSizeChanging(old, value);
            size = value;
            OnSizeChanged(old, value);
            OnPropertyChanged(nameof(Size));
        }
    }
    partial void OnSizeChanging(Size oldValue, Size newValue);
    partial void OnSizeChanged(Size oldValue, Size newValue);

    partial void OnParentChanged(IWorkflowTreeViewModel? oldValue, IWorkflowTreeViewModel? newValue)
        => _scaleTracker.Attach(newValue, OnScaleDirty);

    private void OnScaleDirty()
    {
        OnPropertyChanged(nameof(Anchor));
        OnPropertyChanged(nameof(Size));
    }

    [VeloxCommand]
    private Task Move(object? parameter, CancellationToken ct)
    {
        if (parameter is not Offset offset) return Task.CompletedTask;
        Helper.Move(offset);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SetAnchor(object? parameter, CancellationToken ct)
    {
        if (parameter is not Anchor anchor) return Task.CompletedTask;
        Helper.SetAnchor(anchor);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task SetSize(object? parameter, CancellationToken ct)
    {
        if (parameter is not Size scale) return Task.CompletedTask;
        Helper.SetSize(scale);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task CreateSlot(object? parameter, CancellationToken ct)
    {
        if (parameter is not IWorkflowSlotViewModel slot) return Task.CompletedTask;
        Helper.CreateSlot(slot);
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private Task Delete(object? parameter, CancellationToken ct)
    {
        Helper.Delete();
        return Task.CompletedTask;
    }
    [VeloxCommand]
    private async Task<object?> Receive(object? parameter, CancellationToken ct)
    {
        var ctx = parameter as ITaskContext ?? new TaskContext(parameter);
        return await Helper.ReceiveAsync(ctx, ct);
    }
    [VeloxCommand]
    private async Task Broadcast(object? parameter, CancellationToken ct)
    {
        await Helper.BroadcastAsync(parameter, ct);
    }
    [VeloxCommand]
    private async Task ReverseBroadcast(object? parameter, CancellationToken ct)
    {
        await Helper.ReverseBroadcastAsync(parameter, ct);
    }
    [VeloxCommand]
    private async Task Close(object? parameter, CancellationToken ct)
    {
        await Helper.CloseAsync();
    }

    public IWorkflowNodeViewModelHelper GetHelper() => Helper;
    public void InitializeWorkflow()
    {
        Helper.Install(this);
    }
    public void SetHelper(IWorkflowNodeViewModelHelper helper)
    {
        if (ReferenceEquals(Helper, helper)) return;
        Helper.Uninstall(this);
        Helper = helper;
        helper.Install(this);
    }
}
