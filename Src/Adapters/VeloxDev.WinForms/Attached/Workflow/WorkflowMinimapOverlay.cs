using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms analogue of the <c>WorkflowMinimapOverlay</c> control shipped with the
/// XAML adapters. A minimap control implementing <see cref="IWorkflowMinimapOverlay"/>
/// is bound to a workflow tree through this static behavior; whenever the tree
/// structure, node geometry, or layout changes, the control is invalidated so its
/// <c>OnPaint</c> re-renders.
/// </summary>
/// <remarks>
/// The actual offset values (<see cref="IWorkflowMinimapOverlay.ScrollOffsetX"/> and
/// friends) are pushed by <see cref="WorkflowSurfaceBehavior.Refresh"/> during each
/// refresh cycle. This type only manages the data subscription and repaint trigger.
/// </remarks>
public static class WorkflowMinimapOverlay
{
    private sealed class OverlayState
    {
        public bool IsEnabled { get; set; }
        public IWorkflowTreeViewModel? Tree { get; set; }
        public bool IsDirty { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, OverlayState> States = new();

    // Tracks the controls that have tracking enabled so change notifications can be
    // fanned out to them. Unlike ConditionalWeakTable (not enumerable on all target
    // frameworks), a plain list is enumerable; entries are removed on dispose.
    private static readonly List<Control> BoundControls = [];

    /// <summary>
    /// Gets whether workflow minimap tracking is enabled for the specified control.
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
    /// Enables or disables workflow minimap tracking for the specified control.
    /// When enabled the control is bound to its workflow tree and repaints on change.
    /// </summary>
    public static void SetIsEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var state = GetState(element);
        if (state.IsEnabled == value)
        {
            return;
        }

        Detach(element, state);
        state.IsEnabled = value;
        if (value)
        {
            Attach(element, state);
        }
    }

    /// <summary>
    /// Gets the workflow tree bound to the specified minimap control.
    /// </summary>
    public static IWorkflowTreeViewModel? GetWorkflowTree(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).Tree;
    }

    /// <summary>
    /// Binds the specified minimap control to a workflow tree, replacing any previous binding.
    /// </summary>
    public static void SetWorkflowTree(Control element, IWorkflowTreeViewModel? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var state = GetState(element);
        if (ReferenceEquals(state.Tree, value))
        {
            return;
        }

        UnsubscribeTree(element, state);
        state.Tree = value;
        SubscribeTree(element, state);
        Invalidate(element, state);
    }

    private static void Attach(Control control, OverlayState state)
    {
        if (!BoundControls.Contains(control))
        {
            BoundControls.Add(control);
        }

        control.HandleCreated += OnHandleCreated;
        control.Disposed += OnDisposed;
        control.Resize += OnResize;
        SubscribeTree(control, state);
        Invalidate(control, state);
    }

    private static void Detach(Control control, OverlayState state)
    {
        BoundControls.Remove(control);
        control.HandleCreated -= OnHandleCreated;
        control.Disposed -= OnDisposed;
        control.Resize -= OnResize;
        UnsubscribeTree(control, state);
        state.Tree = null;
    }

    private static void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            Invalidate(control, GetState(control));
        }
    }

    private static void OnDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control && States.TryGetValue(control, out var state))
        {
            Detach(control, state);
        }
    }

    private static void OnResize(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            Invalidate(control, GetState(control));
        }
    }

    private static void SubscribeTree(Control control, OverlayState state)
    {
        var tree = state.Tree;
        if (tree is null)
        {
            return;
        }

        if (tree is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnTreePropertyChanged;
        }

        if (tree.Layout is INotifyPropertyChanged lpc)
        {
            lpc.PropertyChanged += OnTreePropertyChanged;
        }

        if (tree.Nodes is INotifyCollectionChanged nodes)
        {
            nodes.CollectionChanged += OnCollectionChanged;
            foreach (var node in tree.Nodes)
            {
                if (node is INotifyPropertyChanged n)
                {
                    n.PropertyChanged += OnNodePropertyChanged;
                }
            }
        }

        if (tree.Links is INotifyCollectionChanged links)
        {
            links.CollectionChanged += OnCollectionChanged;
        }
    }

    private static void UnsubscribeTree(Control control, OverlayState state)
    {
        var tree = state.Tree;
        if (tree is null)
        {
            return;
        }

        if (tree is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnTreePropertyChanged;
        }

        if (tree.Layout is INotifyPropertyChanged lpc)
        {
            lpc.PropertyChanged -= OnTreePropertyChanged;
        }

        if (tree.Nodes is INotifyCollectionChanged nodes)
        {
            nodes.CollectionChanged -= OnCollectionChanged;
            foreach (var node in tree.Nodes)
            {
                if (node is INotifyPropertyChanged n)
                {
                    n.PropertyChanged -= OnNodePropertyChanged;
                }
            }
        }

        if (tree.Links is INotifyCollectionChanged links)
        {
            links.CollectionChanged -= OnCollectionChanged;
        }
    }

    private static void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged n)
                {
                    n.PropertyChanged += OnNodePropertyChanged;
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged n)
                {
                    n.PropertyChanged -= OnNodePropertyChanged;
                }
            }
        }
    }

    private static void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is Control control)
        {
            Invalidate(control, GetState(control));
        }
        else
        {
            InvalidateBound(sender);
        }
    }

    private static void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            InvalidateBound(sender);
        }
    }

    private static void InvalidateBound(object? source)
    {
        // Repaint every tracked minimap control; node/layout changes may affect any of them.
        for (var i = BoundControls.Count - 1; i >= 0; i--)
        {
            var control = BoundControls[i];
            if (control.IsDisposed)
            {
                BoundControls.RemoveAt(i);
                continue;
            }

            if (States.TryGetValue(control, out var state))
            {
                Invalidate(control, state);
            }
        }
    }

    private static void Invalidate(Control control, OverlayState state)
    {
        if (control.IsDisposed || !state.IsEnabled)
        {
            return;
        }

        state.IsDirty = true;
        control.Invalidate();
    }

    private static OverlayState GetState(Control element)
        => States.GetValue(element, static _ => new OverlayState());
}
