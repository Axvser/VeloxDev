using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using Size = System.Drawing.Size;

namespace Demo.Views.Workflow;

/// <summary>
/// Realtime floating-text info layer (bottom-left corner HUD): translucent-rounded panel showing
/// canvas actual size, the visible viewport (canvas + world), zoom/origin and the visible node/link
/// elements materialized by the Core virtualization. Purely model-driven — it reads
/// <see cref="ITreeHelper.Viewport"/> (kept current by the surface's pan/layout
/// pump), <see cref="CanvasLayout"/> and <see cref="ITreeHelper.VisibleItems"/>.
/// The surface repaints it with the canvas frame and binds the tree in <see cref="Bind"/>; the 复制 button
/// copies the current multi-line info to the clipboard.
/// </summary>
public sealed class InfoOverlay : Panel
{
    // Fixed metrics are 96-DPI logical units; Scaled turns them into the current monitor's pixels.
    private const int PadX = 10;
    private const int PadTop = 8;
    private const int LineGap = 4;
    private const int RightZone = 66; // leave room for the bottom-right copy button

    private static readonly Color s_bg = Color.FromArgb(0xFF, 0x12, 0x15, 0x1B);
    private static readonly Color s_border = Color.FromArgb(0xFF, 0x8E, 0xA3, 0xB8);
    private static readonly Color s_text = Color.FromArgb(0xDD, 0xE4, 0xEA);
    private static readonly Color s_btnBg = Color.FromArgb(0x1E, 0x3A, 0x5F);
    private static readonly Color s_btnFg = Color.FromArgb(0x7E, 0xC8, 0xFF);

    private readonly Font _font = new("Segoe UI", 9f);
    private readonly Button _copy;
    private readonly GraphicsPath _round = new();

    private ITreeComponent? _tree;
    private INotifyPropertyChanged? _layoutNotify;
    private string[] _lines = [];
    private string _copyText = "";
    private Graphics? _measure;

    private int Scaled(int logical) => (int)Math.Round(logical * DeviceDpi / 96f);

    // Measuring with the control's own Graphics keeps the point font resolved at the current monitor DPI,
    // matching the Graphics.DrawString pass. TextRenderer's static overload uses a screen DC.
    private Graphics? MeasureGraphics
    {
        get
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return null;
            }

            return _measure ??= CreateGraphics();
        }
    }

    /// <summary>Creates a self-painting info HUD with a bottom-right copy button.</summary>
    public InfoOverlay()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        _copy = new Button
        {
            Text = "复制",
            FlatStyle = FlatStyle.Flat,
            BackColor = s_btnBg,
            ForeColor = s_btnFg,
            Cursor = Cursors.Hand,
            Size = new Size(Scaled(56), Scaled(24)),
        };
        _copy.FlatAppearance.BorderColor = s_btnFg;
        _copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_copyText);
                _copy.Text = "已复制";
            }
            catch
            {
                _copy.Text = "复制失败";
            }
        };
        Controls.Add(_copy);
    }

    /// <summary>Binds a workflow tree (model events refresh layout/scale/visible counts).</summary>
    public void Bind(ITreeComponent? tree)
    {
        Unsubscribe();
        _tree = tree;
        if (_tree?.Layout is INotifyPropertyChanged layout)
        {
            _layoutNotify = layout;
            layout.PropertyChanged += OnModelChanged;
        }

        if (_tree is not null)
        {
            _tree.Nodes.CollectionChanged += OnModelChanged;
            _tree.Links.CollectionChanged += OnModelChanged;
            _tree.GetHelper().VisibleItems.CollectionChanged += OnModelChanged;
        }

        UpdateText();
    }

    private void Unsubscribe()
    {
        if (_layoutNotify is not null)
        {
            _layoutNotify.PropertyChanged -= OnModelChanged;
            _layoutNotify = null;
        }

        if (_tree is not null)
        {
            _tree.Nodes.CollectionChanged -= OnModelChanged;
            _tree.Links.CollectionChanged -= OnModelChanged;
            _tree.GetHelper().VisibleItems.CollectionChanged -= OnModelChanged;
            _tree = null;
        }
    }

    private void OnModelChanged(object? sender, EventArgs e) => UpdateText();

    /// <summary>Recomputes lines from the model and sizes/paints the panel.</summary>
    /// <remarks>
    /// Called from the canvas' <c>Paint</c>; it must be a no-op when nothing the HUD shows has changed, otherwise
    /// the overlay invalidates itself inside the canvas' paint and the two windows keep re-painting each other.
    /// </remarks>
    public void UpdateText()
    {
        var lines = BuildLines();
        var copyText = string.Join(Environment.NewLine, lines);
        var changed = copyText != _copyText || !LinesEqual(_lines, lines);

        _lines = lines;
        _copyText = copyText;

        if (_copy.Text != "复制")
        {
            _copy.Text = "复制";
        }

        int maxW = 0;
        int lineH = 0;
        var measure = MeasureGraphics;
        foreach (var line in _lines)
        {
            var size = measure is null
                ? TextRenderer.MeasureText(line, _font)
                : Size.Ceiling(measure.MeasureString(line, _font));
            maxW = Math.Max(maxW, size.Width);
            lineH = Math.Max(lineH, size.Height);
        }

        int padX = Scaled(PadX);
        int padTop = Scaled(PadTop);
        int lineGap = Scaled(LineGap);
        int w = Math.Max(Scaled(120), maxW + padX * 2 + Scaled(RightZone));
        int h = Math.Max(Scaled(40), _lines.Length * (lineH + lineGap) - lineGap + padTop * 2);
        if (Width != w || Height != h)
        {
            SetBounds(Left, Math.Max(0, (Parent?.ClientSize.Height ?? 0) - h - Scaled(12)), w, h);
            changed = true;
        }

        LayoutButton();
        if (changed)
        {
            Invalidate();
        }
    }

    private static bool LinesEqual(string[] left, string[] right)
    {
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return false;
        }

        return true;
    }

    /// <summary>Positions the copy button at the panel's bottom-right and pins the overlay bottom-left of its parent.</summary>
    private void LayoutButton()
    {
        var inset = Scaled(6);
        _copy.Location = new Point(Width - _copy.Width - inset, Height - _copy.Height - inset);

        if (Parent is not null)
        {
            int left = Math.Max(Scaled(4), Scaled(40)); // clear the 28-DIP ruler band
            int top = Math.Max(Scaled(4), Parent.ClientSize.Height - Height - Scaled(12));
            if (Left != left || Top != top)
            {
                Location = new Point(left, top);
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // A recreated handle invalidates the cached Graphics.
        _measure?.Dispose();
        _measure = null;
        ApplyDpiScale();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);

        // The cached Graphics still reports the old monitor DPI.
        _measure?.Dispose();
        _measure = null;
        ApplyDpiScale();
    }

    // The native panel and its button are sized in pixels; recompute the physical metrics when the
    // monitor DPI changes so the HUD keeps the same logical size and still clears the ruler band.
    private void ApplyDpiScale()
    {
        _copy.Size = new Size(Scaled(56), Scaled(24));
        UpdateText();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutButton();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        _round.Reset();
        _round.AddRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), Scaled(6));
        using (var bg = new SolidBrush(s_bg))
        using (var border = new Pen(s_border))
        {
            g.FillPath(bg, _round);
            g.DrawPath(border, _round);
        }

        int y = Scaled(PadTop);
        foreach (var line in _lines)
        {
            using var b = new SolidBrush(s_text);
            g.DrawString(line, _font, b, Scaled(PadX), y);
            y += (int)g.MeasureString("Ag", _font).Height + Scaled(LineGap);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _measure?.Dispose();
            _measure = null;
        }

        base.Dispose(disposing);
    }

    private string[] BuildLines()
    {
        if (_tree is null)
        {
            return new[] { "VeloxDev Workflow — 未绑定画布" };
        }

        // Layout 是可写的模型属性；清空它（换文档）时 HUD 不能跟着空引用。
        if (_tree.Layout is not { } layout)
        {
            return new[] { "VeloxDev Workflow — 画布布局不可用" };
        }

        var actual = layout.ActualSize;
        var vp = _tree.GetHelper().Viewport;
        double ox = layout.ActualOffset.Horizontal, oy = layout.ActualOffset.Vertical;
        double sx = vp.Horizontal + ox, sy = vp.Vertical + oy;
        double vw = vp.Width, vh = vp.Height;

        double scale = layout.Scale.Horizontal;
        double zoomPercent = scale > 0 ? 100.0 / scale : 100.0;

        int totalNodes = _tree.Nodes.Count;
        int totalLinks = _tree.Links.Count;
        int visibleNodes = 0;
        int visibleLinks = 0;
        var virtualLink = _tree.VirtualLink;
        foreach (var item in _tree.GetHelper().VisibleItems)
        {
            if (item is INodeComponent)
            {
                visibleNodes++;
            }
            else if (item is ILinkComponent link && !ReferenceEquals(link, virtualLink))
            {
                visibleLinks++;
            }
        }

        return new[]
        {
            "画布 " + Fmt(actual.Width) + " × " + Fmt(actual.Height),
            "视口(画布) " + Fmt(sx) + ", " + Fmt(sy) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "视口(世界) " + Fmt(vp.Horizontal) + ", " + Fmt(vp.Vertical) + "  " + Fmt(vw) + "×" + Fmt(vh),
            "缩放 " + Math.Round(zoomPercent).ToString() + "%  ·  Scale " + scale.ToString("0.00"),
            "原点 " + Fmt(ox) + ", " + Fmt(oy),
            "元素 节点 " + visibleNodes + "/" + totalNodes + " · 连线 " + visibleLinks + "/" + totalLinks,
        };
    }

    private static string Fmt(double value)
    {
        double abs = Math.Abs(value);
        if (abs < 10000) return Math.Round(value).ToString();
        if (abs < 1000000) return Math.Round(value / 1000.0, 1).ToString() + "K";
        return Math.Round(value / 1000000.0, 1).ToString() + "M";
    }
}

/// <summary>GraphicsPath rounded-rectangle helper (single corner radius).</summary>
internal static class RoundedRectPath
{
    public static void AddRoundedRectangle(this GraphicsPath path, Rectangle bounds, int radius)
    {
        int d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
    }
}
