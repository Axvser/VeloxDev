// VeloxDev customization: override BuildContent to change the title/body; the base lays out the output slots.
using System.Drawing;
using System.Reflection;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.VisualHosting;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace Demo.Views.Workflow;

/// <summary>A visual workflow node card: a titled surface whose slot glyphs are arranged by the base class.</summary>
public sealed partial class NodeView : VisualWorkflowNodeView
{
    private static readonly Font TitleFont = new("Segoe UI Semibold", 10.5f);
    private static readonly Font SubtitleFont = new("Segoe UI", 8.5f);

    /// <summary>Creates the card and runs the generated initializer.</summary>
    public NodeView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override VisualElement BuildContent(INodeComponent node)
    {
        var foreground = XamlRuntime.ParseColor("#DD1E1E1E");
        var stack = new StackPanel
        {
            Spacing = 3,
            Margin = new XamlThickness(12f, 9f, 12f, 9f),
        };
        stack.Add(new TextBlock
        {
            Text = TitleOf(node),
            Font = TitleFont,
            Foreground = foreground,
        });
        stack.Add(new TextBlock
        {
            Text = node.GetType().Name,
            Font = SubtitleFont,
            Foreground = Color.FromArgb(0x99, foreground),
        });

        return stack;
    }

    // 先读 Name 再读 Title，与节点的默认标题规则一致；都没有才退回类型名。
    private static string TitleOf(INodeComponent node)
    {
        foreach (var name in new[] { "Name", "Title" })
        {
            if (node.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(node) is string text
                && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return node.GetType().Name;
    }
}
