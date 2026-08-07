namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Link 渲染就绪契约。
///
/// 虚拟化画布中,Link 与其双端节点在 ViewManager 的相邻批次中实现;Slot 锚点由各 GUI 的
/// 槽布局行为在渲染优先级异步测量后写入 <c>slot.Anchor</c>。若 Link 在测量落地前渲染,
/// 会读到默认"无值"锚点(<see cref="double.NaN"/>),画出一帧错位后跳回,即视觉闪烁。
///
/// 机制在核心(<see cref="WorkflowSlotUpdateGate"/>):Slot 锚点默认即 NaN(未测量),链接
/// 渲染前检查双端端点锚点是否已就位。锚点由 GUI 测量写入真实坐标后,现有锚点绑定自动
/// 更新视图并重绘到正确位置 —— 无事件订阅、无时间戳、无 GUI 显式通知。
/// </summary>
public static class WorkflowLinkRenderEx
{
    /// <summary>
    /// 链接是否可渲染:可见,且双端端点锚点均已就位(<see cref="WorkflowSlotUpdateGate.IsLinkRenderReady"/>。
    ///
    /// 消费方式(各 GUI 的 link view OnRender 顶部一行):
    /// <code>
    /// if (DataContext is IWorkflowLinkViewModel link &amp;&amp; !link.IsRenderReady()) return;
    /// </code>
    /// </summary>
    public static bool IsRenderReady(this IWorkflowLinkViewModel link)
    {
        if (!link.IsVisible) return false;
        return WorkflowSlotUpdateGate.IsLinkRenderReady(link);
    }
}
