using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms does not support attached properties, but this type mirrors the workflow surface API shape used by other adapters.
/// </summary>
public sealed class WorkflowSurfaceBehavior
{
    private sealed class SurfaceState
    {
        public bool IsEnabled { get; set; }
        public string? ScrollViewerName { get; set; }
        public string? CanvasName { get; set; }
        public string? GridDecoratorName { get; set; }
        public string? PointerPressSourceName { get; set; }
        public string? MinimapOverlayName { get; set; }
        public IWorkflowTreeViewModel? WorkflowTree { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, SurfaceState> States = new();

    /// <summary>
    /// Gets whether the workflow surface behavior is enabled for the specified control.
    /// </summary>
    public static bool GetIsEnabled(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).IsEnabled;
    }

    /// <summary>
    /// Sets whether the workflow surface behavior is enabled for the specified control.
    /// </summary>
    public static void SetIsEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).IsEnabled = value;
    }

    /// <summary>
    /// Gets the configured scroll viewer host name.
    /// </summary>
    public static string? GetScrollViewerName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).ScrollViewerName;
    }

    /// <summary>
    /// Sets the configured scroll viewer host name.
    /// </summary>
    public static void SetScrollViewerName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).ScrollViewerName = value;
    }

    /// <summary>
    /// Gets the configured canvas host name.
    /// </summary>
    public static string? GetCanvasName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).CanvasName;
    }

    /// <summary>
    /// Sets the configured canvas host name.
    /// </summary>
    public static void SetCanvasName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).CanvasName = value;
    }

    /// <summary>
    /// Gets the configured grid decorator host name.
    /// </summary>
    public static string? GetGridDecoratorName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).GridDecoratorName;
    }

    /// <summary>
    /// Sets the configured grid decorator host name.
    /// </summary>
    public static void SetGridDecoratorName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).GridDecoratorName = value;
    }

    /// <summary>
    /// Gets the configured pointer press source host name.
    /// </summary>
    public static string? GetPointerPressSourceName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).PointerPressSourceName;
    }

    /// <summary>
    /// Sets the configured pointer press source host name.
    /// </summary>
    public static void SetPointerPressSourceName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).PointerPressSourceName = value;
    }

    /// <summary>
    /// Gets the configured minimap overlay host name.
    /// </summary>
    public static string? GetMinimapOverlayName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).MinimapOverlayName;
    }

    /// <summary>
    /// Sets the configured minimap overlay host name. When the named control implements
    /// <see cref="IWorkflowMinimapOverlay"/>, <see cref="Refresh"/> pushes scroll, content
    /// offset, viewport, and tree values into it on every refresh cycle.
    /// </summary>
    public static void SetMinimapOverlayName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).MinimapOverlayName = value;
    }

    /// <summary>
    /// Gets the workflow tree bound to the surface host, if one was set via <see cref="SetWorkflowTree"/>.
    /// </summary>
    public static IWorkflowTreeViewModel? GetWorkflowTree(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).WorkflowTree;
    }

    /// <summary>
    /// Explicitly binds the workflow tree to the surface host so <see cref="Refresh"/> can push the
    /// visible viewport and decorator/minimap offsets. Mirrors setting the host <c>DataContext</c> in
    /// the XAML adapters.
    /// </summary>
    public static void SetWorkflowTree(Control element, IWorkflowTreeViewModel? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).WorkflowTree = value;
    }

    /// <summary>
    /// Requests the host to refresh its layout and redraw, mirroring other workflow surface adapters.
    /// In addition to <c>PerformLayout</c>/<c>Invalidate</c>, this pushes the current scroll/content
    /// offsets into any named <see cref="IWorkflowGridDecorator"/>/<see cref="IWorkflowMinimapOverlay"/>
    /// controls and updates the workflow tree's visible viewport from the host scroll position.
    /// </summary>
    public static void Refresh(Control host)
    {
        if (host is null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        var state = GetState(host);
        var tree = ResolveTree(host);
        var scrollOffset = ResolveScrollOffset(host, tree);
        var clientSize = ResolveClientSize(host);
        var contentOffset = tree?.Layout?.ActualOffset ?? new Offset();

        // Update the tree viewport so consumers (e.g. spatial virtualization) observe
        // the current visible region. Best-effort: never throw from a refresh cycle.
        if (tree is not null && clientSize.Width > 0 && clientSize.Height > 0)
        {
            try
            {
                tree.GetHelper().Viewport = new Viewport(
                    scrollOffset.Horizontal - contentOffset.Horizontal,
                    scrollOffset.Vertical - contentOffset.Vertical,
                    clientSize.Width,
                    clientSize.Height);
            }
            catch
            {
                // The tree helper may not support viewport writes on some hosts; ignore.
            }
        }

        if (!string.IsNullOrWhiteSpace(state.GridDecoratorName)
            && FindControlByName(host, state.GridDecoratorName!) is IWorkflowGridDecorator decorator)
        {
            decorator.ScrollOffsetX = scrollOffset.Horizontal;
            decorator.ScrollOffsetY = scrollOffset.Vertical;
            decorator.ContentOffsetX = contentOffset.Horizontal;
            decorator.ContentOffsetY = contentOffset.Vertical;
        }

        if (!string.IsNullOrWhiteSpace(state.MinimapOverlayName)
            && FindControlByName(host, state.MinimapOverlayName!) is IWorkflowMinimapOverlay minimap)
        {
            minimap.ScrollOffsetX = scrollOffset.Horizontal;
            minimap.ScrollOffsetY = scrollOffset.Vertical;
            minimap.ContentOffsetX = contentOffset.Horizontal;
            minimap.ContentOffsetY = contentOffset.Vertical;
            minimap.ViewportWidth = clientSize.Width;
            minimap.ViewportHeight = clientSize.Height;
            minimap.WorkflowTree = tree;
        }

        // Publish the translate transform as a notification carrier for host canvases.
        WorkflowCanvasTransformBehavior.Apply(host, contentOffset);

        host.PerformLayout();
        host.Invalidate();
    }

    private static IWorkflowTreeViewModel? ResolveTree(Control host)
    {
        if (GetWorkflowTree(host) is { } explicitTree)
        {
            return explicitTree;
        }

        var current = host;
        while (current is not null)
        {
            IWorkflowTreeViewModel? tree = ResolveValue(current, "ViewModel") as IWorkflowTreeViewModel;
            tree ??= ResolveValue(current, "DataContext") as IWorkflowTreeViewModel;
            tree ??= ResolveValue(current, "BindingContext") as IWorkflowTreeViewModel;
            tree ??= current.Tag as IWorkflowTreeViewModel;
            if (tree is not null)
            {
                return tree;
            }

            current = current.Parent;
        }

        return null;
    }

    private static Offset ResolveScrollOffset(Control host, IWorkflowTreeViewModel? tree)
    {
        if (host is ScrollableControl scrollable && scrollable.AutoScroll)
        {
            return new Offset(-scrollable.AutoScrollPosition.X, -scrollable.AutoScrollPosition.Y);
        }

        // Fall back to the persisted viewport offset when no scrollable container is exposed.
        return tree?.Layout?.ViewportOffset ?? new Offset();
    }

    private static System.Drawing.Size ResolveClientSize(Control host)
        => host.ClientSize.Width > 0 ? host.ClientSize : host.Size;

    private static Control? FindControlByName(Control root, string name)
    {
        foreach (var control in EnumerateSelfAndDescendants(root))
        {
            if (string.Equals(control.Name, name, StringComparison.Ordinal))
            {
                return control;
            }
        }

        return null;
    }

    private static System.Collections.Generic.IEnumerable<Control> EnumerateSelfAndDescendants(Control root)
    {
        yield return root;
        foreach (var child in root.Controls.OfType<Control>())
        {
            yield return child;
            foreach (var descendant in EnumerateSelfAndDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static object? ResolveValue(Control control, string propertyName)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var property = control.GetType().GetProperty(propertyName, flags);
        if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        return property.GetValue(control);
    }

    private static SurfaceState GetState(Control element)
        => States.GetValue(element, static _ => new SurfaceState());
}
