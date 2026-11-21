using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Tracks which XAML control holds keyboard focus and provides simple focus scopes over this control tree.
/// </summary>
/// <remarks>
/// <para>
/// The effective focus owner is reported by <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> after
/// <see cref="System.Windows.Forms.Control.OnGotFocus"/> and cleared after
/// <see cref="System.Windows.Forms.Control.OnLostFocus"/>, so <see cref="FocusedElement"/> never goes stale after an
/// ordinary focus change. The manager keeps no per-control state and works on the UI thread only.
/// </para>
/// <para>
/// A focus scope is an ordinary control marked with <see cref="SetIsFocusScope"/>. It groups the elements a host
/// treats as one focus region; <see cref="GetFocusScope"/>,
/// <see cref="GetFocusedElement(VeloxDev.WinForms.Xaml.Controls.Control)"/> and <see cref="FocusFirst"/> use that
/// mark, and a scope nested inside another scope wins for its own subtree.
/// </para>
/// </remarks>
public static class FocusManager
{
    /// <summary>Marks a control as the root of a focus scope.</summary>
    public static readonly DependencyProperty IsFocusScopeProperty = DependencyProperty.RegisterAttached(
        "IsFocusScope",
        typeof(bool),
        typeof(FocusManager),
        new PropertyMetadata(false) { AffectsRender = false });

    private static XamlControl? _focusedElement;

    /// <summary>The control that most recently received keyboard focus, or <see langword="null"/>.</summary>
    public static XamlControl? FocusedElement => _focusedElement;

    /// <summary>Reads whether <paramref name="element"/> is the root of a focus scope.</summary>
    /// <param name="element">The control to read.</param>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static bool GetIsFocusScope(XamlControl element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return (bool)(IsFocusScopeProperty.GetValue(element) ?? false);
    }

    /// <summary>Writes whether <paramref name="element"/> is the root of a focus scope.</summary>
    /// <param name="element">The control to mark.</param>
    /// <param name="value">Whether the control starts a scope.</param>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static void SetIsFocusScope(XamlControl element, bool value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        IsFocusScopeProperty.SetValue(element, value);
    }

    /// <summary>Returns the control that currently holds keyboard focus, or <see langword="null"/>.</summary>
    public static XamlControl? GetFocusedElement() => _focusedElement;

    /// <summary>Returns the focused element when it belongs to <paramref name="scope"/>.</summary>
    /// <param name="scope">The focus scope to test against.</param>
    /// <returns>The focused element, or <see langword="null"/> when focus is outside the scope or nowhere.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    public static XamlControl? GetFocusedElement(XamlControl scope)
    {
        if (scope is null) throw new ArgumentNullException(nameof(scope));

        var focused = _focusedElement;
        return focused is not null && IsWithin(focused, scope) ? focused : null;
    }

    /// <summary>Moves keyboard focus to <paramref name="element"/>.</summary>
    /// <param name="element">The control to focus.</param>
    /// <returns><see langword="true"/> when the control received focus.</returns>
    /// <remarks>A non-focusable or disposed control is rejected without touching the current focus.</remarks>
    public static bool SetFocusedElement(XamlControl element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        if (!element.Focusable || element.IsDisposed || element.Disposing)
        {
            return false;
        }

        try
        {
            if (!element.Focus())
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }

        // OnGotFocus 已经在同一线程上同步写过一次；这里保证没有句柄回调的实现也拿到正确的值。
        _focusedElement = element;
        return true;
    }

    /// <summary>Returns the nearest scope at or above <paramref name="element"/>, or <see langword="null"/>.</summary>
    /// <param name="element">The control whose scope is requested.</param>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static XamlControl? GetFocusScope(XamlControl element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));

        for (System.Windows.Forms.Control? current = element; current is not null; current = current.Parent)
        {
            if (current is XamlControl xaml && GetIsFocusScope(xaml))
            {
                return xaml;
            }
        }

        return null;
    }

    /// <summary>Moves focus to the first tab stop in <paramref name="scope"/>.</summary>
    /// <param name="scope">The focus scope to search.</param>
    /// <returns><see langword="true"/> when a control received focus.</returns>
    /// <remarks>
    /// Tab order is <see cref="VeloxDev.WinForms.Xaml.Controls.Control.TabIndex"/> ascending, with the negative
    /// "no explicit position" value sorting last; ties and unset indexes keep document order. Only XAML descendants
    /// that are focusable, enabled and visible are candidates.
    /// </remarks>
    public static bool FocusFirst(XamlControl scope)
    {
        if (scope is null) throw new ArgumentNullException(nameof(scope));

        var target = FindFirstTabStop(scope);
        return target is not null && SetFocusedElement(target);
    }

    // 焦点进出的唯一入口；Control 的 OnGotFocus/OnLostFocus/句柄销毁都走这里。
    internal static void NotifyFocusEntered(XamlControl control) => _focusedElement = control;

    internal static void NotifyFocusLeft(XamlControl control)
    {
        if (ReferenceEquals(_focusedElement, control))
        {
            _focusedElement = null;
        }
    }

    private static bool IsWithin(XamlControl element, XamlControl scope)
    {
        for (System.Windows.Forms.Control? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, scope))
            {
                return true;
            }
        }

        return false;
    }

    private static XamlControl? FindFirstTabStop(XamlControl scope)
    {
        XamlControl? best = null;
        var bestIndex = int.MaxValue;

        void Walk(System.Windows.Forms.Control parent)
        {
            foreach (System.Windows.Forms.Control child in parent.Controls)
            {
                if (child is XamlControl xaml
                    && xaml.Focusable
                    && xaml.IsTabStop
                    && xaml.Enabled
                    && xaml.Visible)
                {
                    // 负值是「没有显式位置」，排在显式序号之后；同序号保持文档顺序（只看第一个）。
                    var index = xaml.TabIndex >= 0 ? xaml.TabIndex : int.MaxValue;
                    if (best is null || index < bestIndex)
                    {
                        best = xaml;
                        bestIndex = index;
                    }
                }

                Walk(child);
            }
        }

        Walk(scope);
        return best;
    }
}
