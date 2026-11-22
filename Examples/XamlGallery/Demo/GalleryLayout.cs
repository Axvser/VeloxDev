using System.Drawing;
using System.Windows.Forms;
using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;
using XamlScrollableControl = VeloxDev.WinForms.Xaml.Controls.ScrollableControl;

namespace Demo;

// 分页内容靠原生 AutoScroll 滚动；这里把内容面板的高度量成它自己的首选高度，
// 免得每个分页都手写一个总高度，改动卡片后还要数一遍。
internal static class GalleryLayout
{
    private static bool fitting;

    public static void FitScrolledContent(System.Windows.Forms.Control root)
    {
        foreach (var host in Descendants<XamlScrollableControl>(root))
        {
            if (host.Controls.Count != 1 || host.Controls[0] is not XamlControl content)
            {
                continue;
            }

            host.Layout += (_, _) => Fit(host, content);
            Fit(host, content);
        }
    }

    /// <summary>Fits the single XAML content child of one scroll host immediately.</summary>
    public static void FitNow(XamlScrollableControl host)
    {
        if (host.IsDisposed || host.Disposing || host.Controls.Count != 1 || host.Controls[0] is not XamlControl content)
        {
            return;
        }

        Fit(host, content);
    }

    public static IEnumerable<T> Descendants<T>(System.Windows.Forms.Control root) where T : System.Windows.Forms.Control
    {
        foreach (System.Windows.Forms.Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static void Fit(XamlScrollableControl host, XamlControl content)
    {
        if (fitting || host.IsDisposed || host.Disposing)
        {
            return;
        }

        var width = host.DisplayRectangle.Width;
        if (width <= 0)
        {
            return;
        }

        // DisplayRectangle 已经扣掉原生滚动条；以「测量高度」和「最后一个子项底边」的较大者为准，
        // 避免取整让最后一张卡片的底边落进滚动区外，也不额外垫出空白。
        var measured = content.Measure(new Size(width, 0)).Height;
        var bottom = measured;
        foreach (System.Windows.Forms.Control child in content.Controls)
        {
            if (child.Visible)
            {
                bottom = Math.Max(bottom, child.Bottom + 1);
            }
        }

        var height = Math.Max(measured, bottom);
        if (content.Height == height)
        {
            return;
        }

        fitting = true;
        try
        {
            content.Height = height;
        }
        finally
        {
            fitting = false;
        }

        // 透明子控件靠复制父容器像素垫底；内容整体移动后旧位置不会自己重画，这里把整条滚动区作废一次。
        host.Invalidate(true);
    }
}
