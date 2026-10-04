using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// [ Component Helper ] Provide standard supports for Link Component.
/// </summary>
public class LinkHelper : LinkHelper<IWorkflowLinkViewModel>
{

}

/// <summary>
/// [ Component Helper ] Provide standard supports for Link Component.
/// </summary>
/// <typeparam name="T">The type of the Link ViewModel that this helper is designed for. </typeparam>
public class LinkHelper<T> : IWorkflowLinkViewModelHelper, ILinkHitTestable
    where T : class, IWorkflowLinkViewModel
{
    /// <summary>The link this helper is installed on, when it matches <typeparamref name="T"/>.</summary>
    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];

    // 视图发布的曲线与那个控件。存引用不拷贝 —— 它每帧可能重发一次，发布路径上不该有分配。
    private LinkCurve? hitCurve;

    /// <inheritdoc />
    public virtual object? Visual { get; private set; }

    /// <inheritdoc />
    public virtual void SetCurve(LinkCurve? curve, object? visual = null)
    {
        hitCurve = curve;
        // 曲线撤掉时控件跟着走，否则会把一个已经不画这条线的控件当成事件的 sender 报出去。
        Visual = curve is null ? null : visual;
    }

    /// <inheritdoc />
    public virtual bool Contains(double x, double y, double radius)
        => hitCurve is not null && hitCurve.Contains(x, y, radius);

    /// <inheritdoc />
    public virtual void Install(IWorkflowLinkViewModel link)
    {
        Component = link as T;
        commands = link.GetStandardCommands();
    }
    /// <inheritdoc />
    public virtual void Uninstall(IWorkflowLinkViewModel link)
    {
        Component = null;
        commands = [];
        // 解绑之后视图不会再发布，留着旧曲线等于让一条已经不画了的线继续可命中。
        hitCurve = null;
        Visual = null;
    }
    /// <inheritdoc />
    public virtual void Closing() => commands.StandardClosing();
    /// <inheritdoc />
    public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
    /// <inheritdoc />
    public virtual void Closed() => commands.StandardClosed();

    /// <inheritdoc />
    public virtual void Delete() => Component?.StandardDelete();
}
