namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A vertical stack of item visuals that realizes only the rows inside the viewport.
/// </summary>
/// <remarks>
/// <para>
/// This is an <see cref="ItemsControl"/> with <see cref="ItemsControl.Virtualizing"/> already on. Rows are a fixed
/// <see cref="ItemsControl.ItemHeight"/> tall; the panel reads the nearest <see cref="ScrollViewer"/>'s
/// <see cref="ScrollViewer.ScrollOffset"/> and <see cref="ScrollViewer.ViewportHeight"/> to decide which rows exist,
/// exactly as a virtualizing <see cref="ItemsControl"/> does. Setting <see cref="ItemsControl.Virtualizing"/> to
/// <see langword="false"/> turns the fixed-row viewport window off.
/// </para>
/// <para>
/// Variable row heights are not measured: every item gets the same row height. Use a plain
/// <see cref="ItemsControl"/> or a custom <see cref="ItemsPanel"/> when rows need to size to their content.
/// </para>
/// </remarks>
public class VirtualizingStackPanel : ItemsControl
{
    /// <summary>Creates the panel with viewport virtualization enabled.</summary>
    public VirtualizingStackPanel()
    {
        // ItemsControl 的默认是 false；这个类型的全部意义就是固定行高的视口窗口。
        Virtualizing = true;
    }
}
