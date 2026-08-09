using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// 为工作流宿主编排 Win32 窗口样式，消除 WinForms 自绘画布与子窗口（节点卡片）
/// 重绘分离导致的闪烁/残影：
///   - <c>WS_CLIPCHILDREN</c>：父窗口重绘时裁剪子窗口区域，避免父缓冲 BitBlt 覆盖
///     子窗口后再由子窗口异步重绘恢复——这是"拖动时所有节点闪烁"的根源。
///   - <c>WS_EX_COMPOSITED</c>：顶级窗体启用窗口合成，整个窗体树（画布、卡片、卡片
///     内部控件、半透明槽位）由 DWM 统一双缓冲，从根上消除父子窗口重绘分离。
/// 样式无法通过 <see cref="Control.CreateParams"/> 从外部注入（该属性受保护），只能
/// 在窗口句柄创建后通过 <c>SetWindowLong</c> 应用；句柄重建（RecreateHandle）后样式
/// 会重置，因此通过 <c>HandleCreated</c> 事件在每次句柄创建时重新应用。
/// </summary>
internal static class NativeWindowStyleHelper
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_EX_COMPOSITED = 0x02000000;

    // SetWindowPos 标志：设置样式后强制系统重读（SWP_FRAMECHANGED），使运行时
    // 通过 SetWindowLong/SetWindowLongPtr 应用的样式立即生效。
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    // WS_EX_COMPOSITED 让 DWM 为窗体内每个子窗口维护重定向表面并统一合成；子窗口
    // 越多开销越大。对含大量标准控件的复杂窗体（SplitContainer/TabControl/ListBox/
    // 工具栏 + 复杂节点卡片，数百子窗口）启用会明显卡顿，而对纯工作流宿主（模板）
    // 的子窗口数量（十余个）无感。超过此阈值时跳过 composited，闪烁改由已生效的
    // WS_CLIPCHILDREN + 拖动手势内的同步重绘消除。
    private const int CompositedMaxControlCount = 100;

    // 弱引用跟踪：不阻止控件回收；同时保证每个控件只订阅一次 HandleCreated。
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
    /// 确保控件窗口带有 <c>WS_CLIPCHILDREN</c>，使父窗口重绘不覆盖其子控件。
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

        ClipChildrenTracked.Add(control, null);
        control.HandleCreated += OnClipChildrenHandleCreated;
        if (control.IsHandleCreated)
        {
            ApplyClipChildren(control.Handle);
        }
    }

    /// <summary>
    /// 确保控件的顶级窗体带有 <c>WS_EX_COMPOSITED</c>，让整个窗体树统一合成缓冲。
    /// 窗体句柄未创建时订阅其 <c>HandleCreated</c>，保证窗口一创建即应用（越早
    /// 启用合成，已绘制的非合成帧越少）。
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

        CompositedTracked.Add(control, null);
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
        // FindForm 返回控件所在的顶级 Form；对本身就是顶级窗体的控件返回其自身。
        var top = control.FindForm() ?? control.TopLevelControl;
        if (top is null)
        {
            return;
        }

        // 复杂窗体树（大量标准控件 + 复杂节点卡片）跳过 composited：DWM 为每个
        // 子窗口维护重定向表面并合成，数百子窗口下开销使整个窗体（含 ListBox、
        // TextBox、TabControl 等）卡顿。工作流区域自身的无闪烁由 WS_CLIPCHILDREN
        // + 拖动同步重绘保证，与模板一致。
        if (CountDescendants(top) > CompositedMaxControlCount)
        {
            return;
        }

        // 绑定到顶级窗体自身的 HandleCreated，句柄重建后自动恢复。
        if (CompositedForms.TryGetValue(top, out _))
        {
            return;
        }

        CompositedForms.Add(top, null);
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
            foreach (Control child in current.Controls)
            {
                stack.Push(child);
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
        if ((value & style) == 0)
        {
            SetLong(hwnd, index, new IntPtr(value | style));

            // SWP_FRAMECHANGED：通知系统窗口样式已变更，强制重新计算/重读样式，
            // 否则运行时设置的样式（尤其 WS_EX_COMPOSITED）可能不生效。
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
        }
    }

    private static IntPtr GetLong(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);

    private static IntPtr SetLong(IntPtr hwnd, int index, IntPtr value)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : SetWindowLong32(hwnd, index, value);
}
