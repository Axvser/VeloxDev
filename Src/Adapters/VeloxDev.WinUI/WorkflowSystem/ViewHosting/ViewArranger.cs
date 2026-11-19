using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Applies a workflow item's geometry to its WinUI view imperatively and follows later geometry changes.</summary>
public sealed class ViewArranger : IViewArranger<IWorkflowViewModel, FrameworkElement>
{
    private readonly ViewLayoutTracker _layout;

    // 与 ViewContainer 共用订阅表，Deactivate/Destroy 时由对方释放。
    internal ViewArranger(ViewLayoutTracker layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    /// <inheritdoc />
    public void Arrange(FrameworkElement view, IWorkflowViewModel item)
    {
        // 先退掉上一份订阅，再摆新几何：复用池化视图时旧 item 的延迟闭包不能插在两者之间。
        _layout.Release(view);
        ApplyGeometry(view, item);
        // 按新 item 重订；Track 内部也会先退订，重复一次是幂等的。
        _layout.Track(view, item, ApplyGeometry);
    }

    private static void ApplyGeometry(FrameworkElement view, IWorkflowViewModel item)
    {
        switch (item)
        {
            case IWorkflowNodeViewModel node:
                Canvas.SetLeft(view, node.Anchor.Horizontal);
                Canvas.SetTop(view, node.Anchor.Vertical);
                Canvas.SetZIndex(view, node.Anchor.Layer);
                // Size 为 (0,0) 说明视图尚未测量；用 NaN 让平台按内容自适应，而不是塌成 0×0。
                view.Width = node.Size.Width > 0 ? node.Size.Width : double.NaN;
                view.Height = node.Size.Height > 0 ? node.Size.Height : double.NaN;
                break;

            case IWorkflowLinkViewModel:
                // 连线视图自己摆位置：Trimmed 用 offset frame，完整版用自身包围盒。
                // 在这里钉 (0,0) 会覆盖它的自定位，把整条线平移一个覆盖量。
                break;
        }
    }
}
