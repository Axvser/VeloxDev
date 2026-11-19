using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Orchestrates Win32 window styles for the workflow host to eliminate flicker/ghosting caused
/// by the repaint separation between the WinForms self-drawn canvas and its child windows (node cards):
///   - <c>WS_CLIPCHILDREN</c>: clips the child-window region while the parent repaints, preventing the parent's BitBlt buffer from
///     covering child windows before they asynchronously repaint — this is the root cause of "all nodes flicker while dragging".
///   - <c>WS_EX_COMPOSITED</c>: enables window composition on the top-level form so the whole form tree (canvas, cards,
///     in-card controls, semi-transparent slots) is double-buffered by DWM as a unit, eliminating the parent/child repaint
///     separation from the root. The style cannot be injected from outside via <see cref="Control.CreateParams"/>
///     (that property is protected); it can only be applied with <c>SetWindowLong</c> after the window handle is created.
///     After a handle rebuild (RecreateHandle) the styles reset, so they are re-applied on each handle creation via <c>HandleCreated</c>.
/// </summary>
internal static class NativeWindowStyleHelper
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_EX_COMPOSITED = 0x02000000;

    // SetWindowPos 标志：强制系统在设置后重读样式（SWP_FRAMECHANGED），运行时经 SetWindowLong/SetWindowLongPtr 应用的样式才会立即生效。
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    // WS_EX_COMPOSITED 让 DWM 为每个子窗口维护一个重定向表面再合成为一，子窗口越多开销越高。复杂窗体（SplitContainer/TabControl/ListBox/工具栏 + 复杂节点卡片，几百个子窗口）会明显卡顿，
    // 而普通工作流宿主（模板）只有十几个子窗口、不受影响。超过此阈值就跳过 composited，闪烁改由已应用的 WS_CLIPCHILDREN 加拖拽手势内的同步重绘消除。
    private const int CompositedMaxControlCount = 100;

    // 弱引用跟踪：不阻止控件被回收，且保证每个控件只订阅一次 HandleCreated。
    private static readonly ConditionalWeakTable<Control, object> ClipChildrenTracked = new();
    private static readonly ConditionalWeakTable<Control, object> CompositedTracked = new();
    private static readonly ConditionalWeakTable<Control, object> CompositedForms = new();

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Ensures the control's window carries <c>WS_CLIPCHILDREN</c> so parent repaints do not cover its child controls.
    /// </summary>
    public static void EnsureClipChildren(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        if (ClipChildrenTracked.TryGetValue(control, out _))
        {
            return;
        }

        ClipChildrenTracked.Add(control, new object());
        control.HandleCreated += OnClipChildrenHandleCreated;
        if (control.IsHandleCreated)
        {
            ApplyClipChildren(control.Handle);
        }
    }

    /// <summary>
    /// Ensures the control's top-level form carries <c>WS_EX_COMPOSITED</c> so the whole form tree is composited into one buffer.
    /// When the form handle is not yet created, subscribes to its <c>HandleCreated</c> so the style is applied as soon as
    /// the window is created (the earlier composition is enabled, the fewer non-composited frames are drawn).
    /// </summary>
    public static void EnsureComposited(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        if (CompositedTracked.TryGetValue(control, out _))
        {
            return;
        }

        CompositedTracked.Add(control, new object());
        control.HandleCreated += OnCompositedHandleCreated;
        if (control.IsHandleCreated)
        {
            ApplyCompositedToTopLevel(control);
        }
    }

    private static void OnClipChildrenHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control && control.IsHandleCreated)
        {
            ApplyClipChildren(control.Handle);
        }
    }

    private static void OnCompositedHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            ApplyCompositedToTopLevel(control);
        }
    }

    private static void ApplyCompositedToTopLevel(Control control)
    {
        // FindForm 返回包含该控件的顶层 Form；控件本身已是顶层时返回它自己。
        var top = control.FindForm() ?? control.TopLevelControl;
        if (top is null)
        {
            return;
        }

        // 复杂窗体树（多标准控件 + 复杂节点卡片）跳过 composited：DWM 为每个子窗口维护重定向表面再合成，几百个子窗口的开销会让整个窗体（含 ListBox、TextBox、TabControl 等）卡顿。
        // 工作流区域的无闪烁由 WS_CLIPCHILDREN 加拖拽期间同步重绘保证，与模板一致。
        if (CountDescendants(top) > CompositedMaxControlCount)
        {
            return;
        }

        // 绑到顶层窗体自己的 HandleCreated，句柄重建后能自动恢复。
        if (CompositedForms.TryGetValue(top, out _))
        {
            return;
        }

        CompositedForms.Add(top, new object());
        top.HandleCreated += OnTopLevelHandleCreated;
        if (top.IsHandleCreated)
        {
            ApplyComposited(top.Handle);
        }
    }

    private static int CountDescendants(Control root)
    {
        var count = 0;
        var stack = new Stack<Control>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            count++;
            foreach (Control? child in current.Controls)
            {
                if (child is not null)
                {
                    stack.Push(child);
                }
            }
        }

        return count;
    }

    private static void OnTopLevelHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control form && form.IsHandleCreated)
        {
            ApplyComposited(form.Handle);
        }
    }

    private static void ApplyComposited(IntPtr hwnd)
    {
        ApplyStyle(hwnd, GWL_EXSTYLE, WS_EX_COMPOSITED);
    }

    private static void ApplyClipChildren(IntPtr hwnd)
    {
        ApplyStyle(hwnd, GWL_STYLE, WS_CLIPCHILDREN);
    }

    private static void ApplyStyle(IntPtr hwnd, int index, int style)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var current = GetLong(hwnd, index);
        var value = current.ToInt64();
        if ((value & (uint)style) == 0)
        {
            SetLong(hwnd, index, new IntPtr(value | (uint)style));

            // SWP_FRAMECHANGED：通知系统窗口样式已变、强制重算/重读样式；否则运行时设置的样式（尤其 WS_EX_COMPOSITED）可能不生效。
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
        }
    }

    private static IntPtr GetLong(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);

    private static IntPtr SetLong(IntPtr hwnd, int index, IntPtr value)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : SetWindowLong32(hwnd, index, value);
}
