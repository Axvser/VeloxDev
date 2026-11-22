using System.Drawing;
using System.Reflection;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using VisualStackPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel;
using VisualTextBlock = VeloxDev.WinForms.Xaml.Visuals.Controls.TextBlock;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// The adapter's fallback card, used when a surface has no template selector or factory. It supplies passive
/// content only; drag, slot glyphs and anchors come from <see cref="VisualWorkflowNodeBehavior"/>.
/// </summary>
internal sealed class DefaultVisualWorkflowNodeView : VisualElement, IVisualWorkflowNodeContent
{
    private static readonly Font TitleFont = new("Segoe UI Semibold", 10.5f);
    private static readonly Font SubtitleFont = new("Segoe UI", 8.5f);
    private static readonly Dictionary<Type, PropertyInfo?> TitleProperties = [];

    /// <summary>Creates the fallback card.</summary>
    internal DefaultVisualWorkflowNodeView()
    {
        IsHitTestVisible = true;
        Focusable = false;
    }

    /// <inheritdoc />
    public VisualElement? BuildContent(INodeComponent node)
    {
        var stack = new VisualStackPanel { Spacing = 3 };
        stack.Add(new VisualTextBlock
        {
            Text = TitleOf(node),
            Font = TitleFont,
            Foreground = Theme.Current.Foreground,
        });
        stack.Add(new VisualTextBlock
        {
            Text = node.GetType().Name,
            Font = SubtitleFont,
            Foreground = Theme.Current.MutedForeground,
        });

        return new VisualBorder
        {
            Background = Theme.Current.Surface,
            BorderBrush = Theme.Current.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 8f,
            Padding = new XamlThickness(12f, 9f),
            Child = stack,
        };
    }

    private static string TitleOf(INodeComponent node)
    {
        var type = node.GetType();
        if (!TitleProperties.TryGetValue(type, out var property))
        {
            property = type.GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
            TitleProperties[type] = property;
        }

        return property?.GetValue(node) as string ?? type.Name;
    }
}
