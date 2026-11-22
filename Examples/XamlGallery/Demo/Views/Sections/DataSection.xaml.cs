using System.Windows.Forms;

namespace Demo.Views.Sections;

/// <summary>The data and selection examples.</summary>
public sealed partial class DataSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private const int VirtualRowHeight = 28;
    private bool virtualConfigured;
    private int lastVirtualFirstRow = -1;
    private int lastVirtualCount = -1;

    /// <summary>Creates the section.</summary>
    public DataSection()
    {
        InitializeComponent();
        virtualViewer.ViewChanged += (_, _) => MoveVirtualContent();
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        if (virtualConfigured || virtualCanvas.ClientSize.Width <= 0)
        {
            return;
        }

        virtualConfigured = true;
        var count = (DataContext as GalleryModel)?.VirtualRows.Count ?? 400;
        virtualViewer.ExtentHeight = count * VirtualRowHeight;
        virtualContent.Width = virtualCanvas.ClientSize.Width;
        virtualContent.Height = count * VirtualRowHeight;
        virtualList.Width = virtualContent.Width;
        virtualList.Height = virtualContent.Height;
        MoveVirtualContent();
    }

    private void MoveVirtualContent()
    {
        if (!virtualConfigured)
        {
            return;
        }

        var offset = (int)virtualViewer.ScrollOffset.Y;
        var width = virtualCanvas.ClientSize.Width;
        if (virtualContent.Width != width)
        {
            virtualContent.Width = width;
            virtualList.Width = width;
        }

        // 移动只有一个孩子的包装 Canvas，而不是逐个移动已实现的行；ItemsControl 的局部布局不参与。
        var top = -offset;
        if (virtualContent.Top != top)
        {
            virtualContent.Top = top;
        }

        // 拖条每像素都会进这里；只在跨行或可见数量变化时改状态文字，避免逐像素重排文本。
        var firstRow = offset / VirtualRowHeight;
        var count = virtualList.RealizedItemCount;
        if (firstRow == lastVirtualFirstRow && count == lastVirtualCount)
        {
            return;
        }

        lastVirtualFirstRow = firstRow;
        lastVirtualCount = count;
        virtualStatus.Text = $"Realized {count} of {((DataContext as GalleryModel)?.VirtualRows.Count ?? 400)} · first row {firstRow + 1}";
    }

    private void OnSortClick(object? sender, EventArgs e)
    {
        if (DataContext is GalleryModel model)
        {
            model.SortRows();
        }
    }

    private void OnFilterClick(object? sender, EventArgs e)
    {
        if (DataContext is GalleryModel model)
        {
            model.FilterRows();
        }
    }
}
