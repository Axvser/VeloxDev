using System.Drawing;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WinForms.Xaml.Test.Infrastructure;

// 视觉工作流表面的无头夹具：一个 VisualHost（不建句柄）+ 一棵虚拟化 tree + 一个 PART_Canvas。
// 只做搭台与拆卸；模板与断言留在各测试里。
internal sealed class WorkflowVisualFixture : IDisposable
{
    internal const int Width = 800;
    internal const int Height = 500;

    private WorkflowVisualFixture(TreeDefaultComponent tree, VisualHost host, VC.Canvas canvas)
    {
        Tree = tree;
        Host = host;
        Canvas = canvas;
    }

    internal TreeDefaultComponent Tree { get; }

    internal VisualHost Host { get; }

    internal VC.Canvas Canvas { get; }

    internal static WorkflowVisualFixture Create(VisualTemplateSelector selector, bool zoomEnabled = true)
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));

        var host = new VisualHost { Size = new DrawingSize(Width, Height) };
        var canvas = new VC.Canvas { Name = "PART_Canvas", IsHitTestVisible = true };
        host.Root = canvas;

        WorkflowSurfaceBehavior.SetCanvasName(host, "PART_Canvas");
        WorkflowSurfaceBehavior.SetWorkflowTree(host, tree);
        WorkflowSurfaceBehavior.SetVisualTemplateSelector(host, selector);
        WorkflowSurfaceBehavior.SetZoomEnabled(host, zoomEnabled);
        WorkflowSurfaceBehavior.SetIsEnabled(host, true);

        return new WorkflowVisualFixture(tree, host, canvas);
    }

    // 第一次布局把 pool 新建的视图排好，Surface 在这次 LayoutUpdated 里才把槽位行为挂到视图上；
    // 再强制一趟布局，已挂上的槽位行为才会收到它订阅的 LayoutUpdated 并写入锚点。
    internal void Layout()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        Host.RenderTo(graphics, new DrawingSize(Width, Height));
        Canvas.InvalidateMeasure();
        Host.RenderTo(graphics, new DrawingSize(Width, Height));
    }

    internal PointF ToSurface(PointF world) => WorkflowSurfaceBehavior.WorldToSurface(Host, world);

    internal PointF ToSurface(double worldX, double worldY) => ToSurface(new PointF((float)worldX, (float)worldY));

    internal VisualElement? FindView(object dataContext) => FindView(Canvas, dataContext);

    internal VisualElement NodeView(INodeComponent node)
        => FindView(node) ?? throw new InvalidOperationException("the node view was not materialized");

    internal VisualElement SlotView(ISlotComponent slot)
        => FindView(slot) ?? throw new InvalidOperationException("the slot view was not materialized");

    public void Dispose()
    {
        WorkflowSurfaceBehavior.SetWorkflowTree(Host, null);
        WorkflowSurfaceBehavior.SetIsEnabled(Host, false);
        Host.Dispose();
    }

    private static VisualElement? FindView(VisualElement root, object dataContext)
    {
        if (ReferenceEquals(root.DataContext, dataContext))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindView(element, dataContext) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    internal static SlotDefaultComponent AddSlot(INodeComponent node, SlotChannel channel)
    {
        var slot = new SlotDefaultComponent { Channel = channel };
        node.CreateSlotCommand.Execute(slot);
        return slot;
    }

    // 槽位必须在节点进入 tree 之前建好：节点一可见，模板就按当时的 Slots 建卡。
    internal static NodeDefaultComponent AddNode(
        ITreeComponent tree,
        double x,
        double y,
        params SlotChannel[] slotChannels)
    {
        var node = new NodeDefaultComponent
        {
            Anchor = new Anchor(x, y),
            Size = new WorkflowSystem.Size(200d, 120d),
        };

        foreach (var channel in slotChannels)
        {
            AddSlot(node, channel);
        }

        tree.CreateNodeCommand.Execute(node);
        return node;
    }
}
