using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Freezes a control tree's painting while it is rebuilt, then commits the finished tree in one repaint.
/// </summary>
/// <remarks>
/// <para>
/// WinForms gives every child its own window handle, so a structural change (a content swap, an item rebuild,
/// re-templating) is painted window by window. The window paints that land in between show half of the old tree and
/// half of the new one. A scope stops that: every handle in the tree is sent <c>WM_SETREDRAW(false)</c>, the tree is
/// mutated and laid out, and a single <c>RedrawWindow</c> paints the result.
/// </para>
/// <para>
/// Nested scopes are reference-counted. A scope started on a descendant joins the active scope on its nearest
/// ancestor instead of releasing that subtree early, so the usual "outer host, inner content control" nesting is safe.
/// </para>
/// </remarks>
public sealed class XamlRenderScope : IDisposable
{
    private const int WM_SETREDRAW = 0x000B;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_NOERASE = 0x0020;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;

    private static readonly ConditionalWeakTable<System.Windows.Forms.Control, BatchState> Batches = new();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    private readonly System.Windows.Forms.Control? _root;
    private readonly BatchState? _state;
    private bool _disposed;

    private XamlRenderScope(System.Windows.Forms.Control? root, BatchState? state)
    {
        _root = root;
        _state = state;
    }

    /// <summary>Freezes redraw for <paramref name="root"/> and its descendants until the returned scope is disposed.</summary>
    /// <param name="root">The outermost control whose subtree will be rebuilt.</param>
    /// <returns>A scope that commits one repaint when disposed; it is a no-op for a disposed control.</returns>
    public static XamlRenderScope Begin(System.Windows.Forms.Control root)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));
        if (root.IsDisposed || root.Disposing)
        {
            return new XamlRenderScope(null, null);
        }

        // 祖先已有活动 scope 时并进它：否则内层先归还句柄，会把外层还在冻结的子树提前放出来。
        var state = FindActiveAncestor(root) ?? Batches.GetOrCreateValue(root);
        if (state.Depth++ == 0)
        {
            state.Reset();
            state.Hook(root);
        }

        return new XamlRenderScope(root, state);
    }

    /// <summary>Releases this scope and repaints the tree once when the outermost scope ends.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_root is null || _state is null)
        {
            return;
        }

        if (--_state.Depth > 0)
        {
            return;
        }

        _state.Release();
        if (!_root.IsDisposed && !_root.Disposing && _root.IsHandleCreated)
        {
            // 先恢复所有句柄再一次性重画；RDW_NOERASE 避免底色闪一帧。
            RedrawWindow(
                _root.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                RDW_INVALIDATE | RDW_NOERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
        }
    }

    private static BatchState? FindActiveAncestor(System.Windows.Forms.Control root)
    {
        for (var parent = root.Parent; parent is not null; parent = parent.Parent)
        {
            if (Batches.TryGetValue(parent, out var state) && state.Depth > 0)
            {
                return state;
            }
        }

        return null;
    }

    private sealed class BatchState
    {
        private readonly HashSet<System.Windows.Forms.Control> _visited = [];
        private readonly HashSet<System.Windows.Forms.Control> _frozen = [];
        private readonly HashSet<System.Windows.Forms.Control> _addedHooks = [];
        private readonly HashSet<System.Windows.Forms.Control> _createdHooks = [];
        private bool _released;

        public int Depth;

        public void Reset() => _released = false;

        public void Hook(System.Windows.Forms.Control control)
        {
            if (_released || control.IsDisposed)
            {
                return;
            }

            // 新加入的子树也要被冻结；visited 同时挡住递归重入。
            if (!_visited.Add(control))
            {
                return;
            }

            if (_addedHooks.Add(control))
            {
                control.ControlAdded += OnControlAdded;
            }

            if (control.IsHandleCreated)
            {
                Freeze(control);
            }
            else if (_createdHooks.Add(control))
            {
                control.HandleCreated += OnHandleCreated;
            }

            foreach (System.Windows.Forms.Control child in control.Controls)
            {
                Hook(child);
            }
        }

        public void Release()
        {
            _released = true;
            foreach (var control in _addedHooks)
            {
                control.ControlAdded -= OnControlAdded;
            }

            foreach (var control in _createdHooks)
            {
                control.HandleCreated -= OnHandleCreated;
            }

            foreach (var control in _frozen)
            {
                if (!control.IsDisposed && control.IsHandleCreated)
                {
                    SendMessage(control.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                }
            }

            _visited.Clear();
            _frozen.Clear();
            _addedHooks.Clear();
            _createdHooks.Clear();
        }

        private void Freeze(System.Windows.Forms.Control control)
        {
            if (control.IsDisposed || !control.IsHandleCreated)
            {
                return;
            }

            if (_frozen.Add(control))
            {
                SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void OnControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control is { } child)
            {
                Hook(child);
            }
        }

        private void OnHandleCreated(object? sender, EventArgs e)
        {
            if (sender is not System.Windows.Forms.Control control || control.IsDisposed)
            {
                return;
            }

            Freeze(control);
            foreach (System.Windows.Forms.Control child in control.Controls)
            {
                Hook(child);
            }
        }
    }
}
