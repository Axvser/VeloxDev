using System.Windows.Forms;
using XamlAdorner = VeloxDev.WinForms.Xaml.Controls.Adorner;
using XamlAdornerLayer = VeloxDev.WinForms.Xaml.Controls.AdornerLayer;
using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;
using XamlScrollableControl = VeloxDev.WinForms.Xaml.Controls.ScrollableControl;

namespace Demo;

/// <summary>
/// Resolves the element under a preview click and paints the accent outline around it.
/// </summary>
/// <remarks>
/// Native child controls are separate HWNDs and do not bubble pointer events to XAML ancestors, so selection
/// listens to the thread message queue instead. The click is never swallowed: buttons, editors and toggles keep
/// their normal behaviour while the inspector follows them.
/// </remarks>
internal sealed class GallerySelection : IMessageFilter, IDisposable
{
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;

    private readonly HashSet<XamlScrollableControl> _scrollHosts = [];
    private System.Windows.Forms.Control? _previewRoot;
    private XamlAdornerLayer? _layer;
    private XamlAdorner? _adorner;
    private System.Windows.Forms.Control? _defaultSelection;
    private System.Windows.Forms.Control? _selected;
    private Region? _layerRegion;
    private bool _attached;

    /// <summary>Raised after the selected element changes (including a clear).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The control the inspector is currently describing.</summary>
    public System.Windows.Forms.Control? Selected => _selected;

    /// <summary>Installs the message filter for one preview host and its adorner layer.</summary>
    public void Attach(System.Windows.Forms.Control previewRoot, XamlAdornerLayer layer)
    {
        _previewRoot = previewRoot;
        _layer = layer;
        _adorner = new XamlAdorner { Focusable = false };
        if (!_attached)
        {
            Application.AddMessageFilter(this);
            _attached = true;
        }
    }

    /// <summary>Sets the control selected when the user clicks preview chrome rather than a card element.</summary>
    public void SetDefault(System.Windows.Forms.Control? value) => _defaultSelection = value;

    /// <summary>Follows the scroll and layout of the preview's scrollable hosts so the outline stays on target.</summary>
    public void TrackScroll(IEnumerable<XamlScrollableControl> hosts)
    {
        foreach (var host in _scrollHosts)
        {
            if (host.IsDisposed)
            {
                continue;
            }

            host.Scroll -= OnScrollHostChanged;
            host.Layout -= OnScrollHostChanged;
        }

        _scrollHosts.Clear();
        foreach (var host in hosts)
        {
            if (host is null || host.IsDisposed)
            {
                continue;
            }

            host.Scroll += OnScrollHostChanged;
            host.Layout += OnScrollHostChanged;
            _scrollHosts.Add(host);
        }
    }

    /// <summary>Selects a control and updates the adorner and the inspector.</summary>
    public void Select(System.Windows.Forms.Control? target)
    {
        if (target is not null && target.IsDisposed)
        {
            target = null;
        }

        if (ReferenceEquals(_selected, target) && target is not null)
        {
            RefreshAdorner();
            return;
        }

        if (_selected is not null && !_selected.IsDisposed)
        {
            _selected.LocationChanged -= OnSelectedGeometryChanged;
            _selected.SizeChanged -= OnSelectedGeometryChanged;
        }

        _selected = target;

        if (_selected is not null)
        {
            _selected.LocationChanged += OnSelectedGeometryChanged;
            _selected.SizeChanged += OnSelectedGeometryChanged;
        }

        UpdateAdorner();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes the outline without clearing the selected control (used by capture mode).</summary>
    public void ClearVisual()
    {
        if (_layer is not null && _adorner is not null && !_layer.IsDisposed)
        {
            _layer.Adorners.Remove(_adorner);
            ClearLayerRegion();
            _layer.Invalidate();
        }
    }

    /// <summary>Recomputes the outline geometry after an edit, scroll or resize.</summary>
    public void RefreshAdorner()
    {
        if (_layer is null || _layer.IsDisposed)
        {
            return;
        }

        if (_adorner is not null && _adorner.AdornedElement is { IsDisposed: true })
        {
            _layer.Adorners.Remove(_adorner);
        }

        UpdateLayerRegion();
        _layer.Invalidate();
    }

    /// <inheritdoc />
    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg != WmLButtonDown && message.Msg != WmRButtonDown)
        {
            return false;
        }

        var root = _previewRoot;
        if (root is null || root.IsDisposed || root.Disposing)
        {
            return false;
        }

        var managed = System.Windows.Forms.Control.FromChildHandle(message.HWnd);
        if (managed is null || managed.IsDisposed || !IsInsidePreview(managed, root))
        {
            return false;
        }

        // The preview's native AutoScroll bars belong to the host, not to the content.
        if (managed is System.Windows.Forms.ScrollBar)
        {
            return false;
        }

        var target = Resolve(managed);
        if (target is not null)
        {
            Select(target);
        }

        // Never swallow: controls keep their click behaviour while the inspector follows.
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_attached)
        {
            Application.RemoveMessageFilter(this);
            _attached = false;
        }

        TrackScroll([]);

        if (_selected is not null && !_selected.IsDisposed)
        {
            _selected.LocationChanged -= OnSelectedGeometryChanged;
            _selected.SizeChanged -= OnSelectedGeometryChanged;
        }

        _selected = null;

        if (_layer is not null && !_layer.IsDisposed && _adorner is not null)
        {
            _layer.Adorners.Remove(_adorner);
        }

        ClearLayerRegion();
        _adorner?.Dispose();
        _adorner = null;
        _layer = null;
        _previewRoot = null;
    }

    private System.Windows.Forms.Control? Resolve(System.Windows.Forms.Control managed)
    {
        // Alt+Click exposes the exact managed HWND (useful for a native: element), including native internals.
        if ((System.Windows.Forms.Control.ModifierKeys & Keys.Alt) != 0)
        {
            return managed;
        }

        for (var current = managed; current is not null; current = current.Parent)
        {
            if (current is not XamlControl xaml)
            {
                continue;
            }

            if (ReferenceEquals(xaml, _previewRoot))
            {
                return ValidDefault() ?? xaml;
            }

            var fallback = ValidDefault();
            if (fallback is not null && IsAncestorOrSelf(xaml, fallback))
            {
                return fallback;
            }

            return xaml;
        }

        return ValidDefault() ?? managed;
    }

    private System.Windows.Forms.Control? ValidDefault()
        => _defaultSelection is { IsDisposed: false, Disposing: false } ? _defaultSelection : null;

    private void UpdateAdorner()
    {
        if (_layer is null || _adorner is null || _layer.IsDisposed)
        {
            return;
        }

        if (_selected is null || _selected.IsDisposed)
        {
            _layer.Adorners.Remove(_adorner);
            _layer.Invalidate();
            return;
        }

        _adorner.AdornedElement = _selected;
        if (!_layer.Adorners.Contains(_adorner))
        {
            _layer.Add(_adorner);
        }

        UpdateLayerRegion();
        _layer.Invalidate();
    }

    // The runtime layer does not reliably receive WS_EX_LAYERED in this environment. A native region
    // clipped to the outline keeps its colour-key fill and hit testing off the rest of the preview.
    private void UpdateLayerRegion()
    {
        if (_layer is null || _layer.IsDisposed)
        {
            return;
        }

        if (_selected is null || _selected.IsDisposed || !_selected.IsHandleCreated || !_layer.IsHandleCreated)
        {
            ClearLayerRegion();
            return;
        }

        var origin = _selected.PointToScreen(Point.Empty);
        var local = _layer.PointToClient(origin);
        var rect = new Rectangle(local, _selected.Size);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            ClearLayerRegion();
            return;
        }

        var outer = Rectangle.Inflate(rect, 2, 2);
        var inner = Rectangle.Inflate(rect, -1, -1);
        var region = new Region(outer);
        if (inner.Width > 0 && inner.Height > 0)
        {
            region.Exclude(inner);
        }

        var previous = _layerRegion;
        _layer.Region = region;
        _layerRegion = region;
        previous?.Dispose();
    }

    private void ClearLayerRegion()
    {
        if (_layer is not null && !_layer.IsDisposed)
        {
            _layer.Region = null;
        }

        _layerRegion?.Dispose();
        _layerRegion = null;
    }

    private void OnSelectedGeometryChanged(object? sender, EventArgs e) => RefreshAdorner();

    private void OnScrollHostChanged(object? sender, EventArgs e) => RefreshAdorner();

    private static bool IsInsidePreview(System.Windows.Forms.Control control, System.Windows.Forms.Control root)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAncestorOrSelf(System.Windows.Forms.Control ancestor, System.Windows.Forms.Control descendant)
    {
        for (var current = descendant; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
