using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

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
    /// Requests the host to refresh its layout and redraw, mirroring other workflow surface adapters.
    /// </summary>
    public static void Refresh(Control host)
    {
        if (host is null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        host.PerformLayout();
        host.Invalidate();
    }

    private static SurfaceState GetState(Control element)
        => States.GetValue(element, static _ => new SurfaceState());
}
