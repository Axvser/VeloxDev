using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Adapters.Benchmarks.Razor;

// 与 demo 同形的卡片模板：节点按 Anchor/Size 绝对定位，连线是一张占位卡片。
// 它比 demo 的 TemplateNodeView/TemplateLinkView 轻——这里量的是 ViewPool 的渲染树，
// 模板越接近下限，数字越不会把模板组件的内部成本伪装成池的成本。
internal static class ViewPoolTemplate
{
    internal static readonly Func<object, object> KeySelector = static item => item;

    internal static readonly RenderFragment<object> Item = static item => builder =>
    {
        switch (item)
        {
            case INodeComponent node:
                RenderNode(builder, node);
                break;
            case ILinkComponent link:
                RenderLink(builder, link);
                break;
        }
    };

    private static void RenderNode(RenderTreeBuilder builder, INodeComponent node)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "veloxdev-wf-node");
        builder.AddAttribute(2, "style", string.Create(
            CultureInfo.InvariantCulture,
            $"position:absolute;left:{node.Anchor.Horizontal}px;top:{node.Anchor.Vertical}px;width:{node.Size.Width}px;height:{node.Size.Height}px"));
        builder.OpenElement(3, "div");
        builder.AddAttribute(4, "class", "veloxdev-wf-node-header");
        builder.OpenElement(5, "span");
        builder.AddAttribute(6, "class", "veloxdev-wf-node-title");
        builder.AddContent(7, "node");
        builder.CloseElement();
        builder.CloseElement();
        builder.OpenElement(8, "div");
        builder.AddAttribute(9, "class", "veloxdev-wf-slot");
        builder.CloseElement();
        builder.CloseElement();
    }

    private static void RenderLink(RenderTreeBuilder builder, ILinkComponent link)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "veloxdev-wf-link");
        builder.AddAttribute(2, "data-visible", link.IsVisible);
        builder.OpenElement(3, "span");
        builder.AddAttribute(4, "class", "veloxdev-wf-link-marker");
        builder.CloseElement();
        builder.CloseElement();
    }
}
