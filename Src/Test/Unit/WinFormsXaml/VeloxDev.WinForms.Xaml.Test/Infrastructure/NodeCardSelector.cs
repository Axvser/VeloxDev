using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test.Infrastructure;

// 测试用的节点卡模板：一个卡片 Canvas（绑定锚点/尺寸）、标题行、正文、一个输入槽、一个槽位枚举器。
// 槽位测量、拖拽、连线三个行为都按真实模板的方式声明在具名部件上。
internal sealed class NodeCardSelector : VisualTemplateSelector
{
    internal bool EnableSlotLayout { get; init; } = true;

    internal bool EnableDrag { get; init; } = true;

    internal bool EnableConnection { get; init; } = true;

    internal string? DragCoordinateHost { get; init; } = "PART_Canvas";

    // 非 1 时标题行放进一个带缩放 Transform 的具名画布，用来验证拖拽差值取自具名坐标宿主。
    internal double DragSpaceScale { get; init; } = 1d;

    public override VisualElement? SelectTemplate(object? item)
    {
        if (item is ILinkComponent)
        {
            var link = new VC.Border { Width = 1f, Height = 1f, Background = new SolidColorBrush(Color.Transparent) };
            VC.Panel.SetZIndex(link, -1);
            return link;
        }

        if (item is not INodeComponent node)
        {
            return null;
        }

        var card = new VC.Canvas { Name = "Card", IsHitTestVisible = true };
        Bind(card, VC.Canvas.LeftProperty, "Anchor.Horizontal");
        Bind(card, VC.Canvas.TopProperty, "Anchor.Vertical");
        Bind(card, VisualElement.WidthProperty, "Size.Width");
        Bind(card, VisualElement.HeightProperty, "Size.Height");
        Bind(card, VC.Panel.ZIndexProperty, "Anchor.Layer");

        card.Add(new VC.Border
        {
            Name = "Body",
            Width = 200f,
            Height = 120f,
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x28, 0x28, 0x28)),
        });

        var title = new VC.Border
        {
            Name = "Title",
            Width = 200f,
            Height = 36f,
            Background = new SolidColorBrush(Color.White),
        };
        VC.Canvas.SetLeft(title, 0f);
        VC.Canvas.SetTop(title, 0f);

        if (Math.Abs(DragSpaceScale - 1d) > 0.0001d)
        {
            var local = new VC.Canvas
            {
                Name = "PART_Local",
                Width = 200f,
                Height = 36f,
                Transform = new Matrix((float)DragSpaceScale, 0f, 0f, (float)DragSpaceScale, 0f, 0f),
            };
            local.Add(title);
            VC.Canvas.SetLeft(local, 0f);
            VC.Canvas.SetTop(local, 0f);
            card.Add(local);
        }
        else
        {
            card.Add(title);
        }

        if (EnableDrag)
        {
            WorkflowNodeDragBehavior.SetIsEnabled(title, true);
            WorkflowNodeDragBehavior.SetCoordinateHostName(title, DragCoordinateHost);
        }

        if (node.Slots.Count > 0)
        {
            var input = new VC.Border
            {
                Name = "InPort",
                Width = 18f,
                Height = 18f,
                Background = new SolidColorBrush(Color.Lime),
                DataContext = node.Slots[0],
            };
            VC.Canvas.SetLeft(input, 0f);
            VC.Canvas.SetTop(input, 50f);
            WorkflowSlotConnectionBehavior.SetIsEnabled(input, EnableConnection);
            card.Add(input);
        }

        if (node.Slots.Count > 1)
        {
            var outputs = new VC.ItemsControl
            {
                Name = "OutPorts",
                Width = 60f,
                Height = 40f,
                ItemTemplate = new DataTemplate(() =>
                {
                    var output = new VC.Border
                    {
                        Width = 18f,
                        Height = 18f,
                        Background = new SolidColorBrush(Color.Orange),
                    };
                    WorkflowSlotConnectionBehavior.SetIsEnabled(output, EnableConnection);
                    return output;
                }),
            };
            VC.Canvas.SetLeft(outputs, 140f);
            VC.Canvas.SetTop(outputs, 40f);
            outputs.ItemsSource = node.Slots.Skip(1).ToArray();
            card.Add(outputs);
        }

        if (EnableSlotLayout)
        {
            WorkflowSlotLayoutBehavior.SetIsEnabled(card, true);
            WorkflowSlotLayoutBehavior.SetSlotNames(card, "InPort");
            WorkflowSlotLayoutBehavior.SetSlotEnumeratorNames(card, "OutPorts");
            WorkflowSlotLayoutBehavior.SetCoordinateHostName(card, "PART_Canvas");
        }

        return card;
    }

    private static void Bind(IWinformsObject target, DependencyProperty property, string path)
        => VisualBindingOperations.Bind(target, property, new Binding(path));
}
