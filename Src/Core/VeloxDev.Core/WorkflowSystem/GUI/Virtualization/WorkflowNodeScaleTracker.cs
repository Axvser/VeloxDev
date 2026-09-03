using System.ComponentModel;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Watches the parent tree's <see cref="CanvasLayout"/> and fires a callback when <see cref="CanvasLayout.Scale"/>
/// changes, so a node can re-raise Anchor/Size and mark the tree helper dirty for re-virtualization.
/// </summary>
public sealed class WorkflowNodeScaleTracker
{
    private IWorkflowTreeViewModel? _tree;
    private CanvasLayout? _layout;
    private Action? _onScaleDirty;

    /// <summary>Attaches to <paramref name="tree"/> (null detaches). The callback fires on Layout.Scale change.</summary>
    public void Attach(IWorkflowTreeViewModel? tree, Action onScaleDirty)
    {
        if (ReferenceEquals(_tree, tree))
        {
            _onScaleDirty = onScaleDirty;
            return;
        }

        Detach();
        _tree = tree;
        _onScaleDirty = onScaleDirty;
        if (tree is null)
        {
            return;
        }

        tree.PropertyChanged += OnTreePropertyChanged;
        _layout = tree.Layout;
        if (_layout is not null)
        {
            _layout.PropertyChanged += OnLayoutPropertyChanged;
        }
    }

    /// <summary>Detaches from the tree and layout, unsubscribing all handlers.</summary>
    public void Detach()
    {
        if (_tree is not null)
        {
            _tree.PropertyChanged -= OnTreePropertyChanged;
            _tree = null;
        }

        if (_layout is not null)
        {
            _layout.PropertyChanged -= OnLayoutPropertyChanged;
            _layout = null;
        }

        _onScaleDirty = null;
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IWorkflowTreeViewModel.Layout))
        {
            return;
        }

        if (ReferenceEquals(_tree?.Layout, _layout))
        {
            return;
        }

        if (_layout is not null)
        {
            _layout.PropertyChanged -= OnLayoutPropertyChanged;
        }

        _layout = _tree?.Layout;
        if (_layout is not null)
        {
            _layout.PropertyChanged += OnLayoutPropertyChanged;
        }
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CanvasLayout.Scale))
        {
            return;
        }

        _onScaleDirty?.Invoke();
        _tree?.GetHelper()?.MarkDirty();
    }
}
