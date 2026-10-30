using Demo.Workflow;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

/// <summary>
/// Demo-only: attaches the node layout menu to a card as its context flyout.
/// </summary>
/// <remarks>
/// MAUI shows a view's context flyout on the platform's own right-click / long-press gesture, so the card
/// only declares the menu once; there is no per-platform popup code and no pointer coordinate to convert.
/// The card's BindingContext is the node, which is what the menu item reads when it runs.
/// </remarks>
internal static class LayoutMenu
{
    /// <summary>Where the demo's status line listens; <see cref="WorkflowView"/> sets it on construction.</summary>
    public static Action<string>? StatusSink { get; set; }

    /// <summary>Builds the shared layout submenu and installs it as <paramref name="view"/>'s context flyout.</summary>
    public static void Attach(View view)
    {
        var tolerance = 8d;
        var menu = new MenuFlyout();

        var layout = new MenuFlyoutSubItem { Text = "自动布局" };
        foreach (var (label, traversal) in WorkflowLayoutDemo.Traversals)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Clicked += (_, _) => Apply(view, traversal, tolerance);
            layout.Add(item);
        }
        menu.Add(layout);

        var toleranceMenu = new MenuFlyoutSubItem { Text = "布局容差" };
        foreach (var (label, value) in WorkflowLayoutDemo.Tolerances)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Clicked += (_, _) => tolerance = value;
            toleranceMenu.Add(item);
        }
        menu.Add(new MenuFlyoutSeparator());
        menu.Add(toleranceMenu);

        FlyoutBase.SetContextFlyout(view, menu);
    }

    private static void Apply(View view, WorkflowLayoutTraversal traversal, double tolerance)
    {
        if (view.BindingContext is not IWorkflowNodeViewModel node) return;

        try
        {
            var result = WorkflowLayoutDemo.Arrange(node, traversal, tolerance);
            StatusSink?.Invoke(WorkflowLayoutDemo.Describe(result, $"从 {node.GetType().Name}"));
        }
        catch (Exception ex)
        {
            StatusSink?.Invoke($"布局失败：{ex.Message}");
        }
    }
}
