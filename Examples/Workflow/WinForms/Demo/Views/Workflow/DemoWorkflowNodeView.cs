using System.Drawing;
using System.Reflection;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.VisualHosting;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace Demo.Views.Workflow;

/// <summary>
/// The demo's node card on the visual backend: the same title/subtitle language as the HWND card, without a window.
/// </summary>
internal sealed class DemoWorkflowNodeView : VisualWorkflowNodeView
{
    private static readonly Font TitleFont = CardTheme.Font(CardTheme.TitleSize, FontStyle.Bold);
    private static readonly Font SubtitleFont = CardTheme.Font(CardTheme.LabelSize);
    private static readonly Dictionary<(Type Type, string Name), PropertyInfo?> Properties = [];

    /// <inheritdoc />
    protected override VisualElement BuildContent(INodeComponent node)
    {
        var stack = new StackPanel { Spacing = 3 };
        stack.Add(new TextBlock
        {
            Text = TitleOf(node),
            Font = TitleFont,
            Foreground = CardTheme.Title,
        });
        stack.Add(new TextBlock
        {
            Text = SubtitleOf(node),
            Font = SubtitleFont,
            Foreground = CardTheme.Label,
        });

        return new Border
        {
            Background = CardTheme.Surface,
            BorderBrush = CardTheme.Border,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = CardTheme.CardRadius,
            Padding = new XamlThickness(CardTheme.BodyPaddingX, CardTheme.BodyPaddingY),
            Child = stack,
        };
    }

    private static string TitleOf(INodeComponent node)
    {
        var title = ReadString(node, "Title");
        return string.IsNullOrWhiteSpace(title) ? node.GetType().Name : title!;
    }

    private static string SubtitleOf(INodeComponent node)
    {
        var description = ReadString(node, "Description");
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description!.Length <= 48 ? description : description.Substring(0, 48) + "…";
        }

        return node.GetType().Name;
    }

    private static string? ReadString(INodeComponent node, string name)
    {
        var key = (node.GetType(), name);
        if (!Properties.TryGetValue(key, out var property))
        {
            property = key.Item1.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Properties[key] = property;
        }

        return property?.PropertyType == typeof(string) ? property.GetValue(node) as string : null;
    }
}
