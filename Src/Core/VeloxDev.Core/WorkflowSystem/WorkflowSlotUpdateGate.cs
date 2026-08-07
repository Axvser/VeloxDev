namespace VeloxDev.WorkflowSystem;

/// <summary>
/// 虚拟化画布 Link 渲染就绪的核心判定。
///
/// Slot 锚点默认值即"无值"占位 —— <see cref="Anchor"/> 默认水平/垂直坐标为
/// <see cref="double.NaN"/>(语义:未测量 / 待 GUI 定位),任何真实 GUI 测量都不可能
/// 恰好产生 NaN。链接渲染前检查双端端点锚点是否已就位:任一端点仍为 NaN 则拦截渲染,
/// 避免读到"无值"坐标画出一帧错位后跳回(视觉闪烁)。
///
/// 放行后的重绘是自动的:slot.Anchor 由 GUI 测量写入真实坐标后,现有 StartLeft/EndLeft
/// 锚点绑定更新 → 视图 InvalidateVisual → 重写 OnRender → 于正确位置绘制。无需任何
/// 事件订阅、时间戳排队或 GUI 显式通知;非虚拟化树照常渲染(NaN 判定对任意 GUI 通用)。
/// </summary>
public static class WorkflowSlotUpdateGate
{
    /// <summary>链接是否可渲染:双端端点锚点均已就位。</summary>
    public static bool IsLinkRenderReady(IWorkflowLinkViewModel link)
        => EndpointReady(link.Sender) && EndpointReady(link.Receiver);

    /// <summary>
    /// 端点就绪判定:
    /// 未挂载到节点(Parent 为 null,如拖拽预览/虚拟链接占位端点)没有可等待的布局 → 就绪;
    /// 否则锚点水平/垂直坐标均非 NaN(已被 GUI 测量写入真实值)→ 就绪。
    /// </summary>
    private static bool EndpointReady(IWorkflowSlotViewModel? slot)
    {
        if (slot is null || slot.Parent is null) return true;
        var anchor = slot.Anchor;
        return !double.IsNaN(anchor.Horizontal) && !double.IsNaN(anchor.Vertical);
    }
}
