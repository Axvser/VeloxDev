using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

// 逐像素 alpha 的浮动标尺带。WinForms 的子控件永远画在父控件之上，画在视口里的半透明带压不住
// 从下面滚过的不透明卡片；唯一能压住的是 WS_EX_LAYERED 的顶层弹窗，由表面行为创建并跟随宿主。
internal sealed class WorkflowRulerOverlayForm : Form
{
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int HtTransparent = -1;
    private const int WmNcHitTest = 0x0084;
    private const uint UlwAlpha = 2;

    private const double GridSpacing = 40;
    private const int MajorFreq = 5;

    // 与表面同一套配色。alpha 取 0x70：带子合成在卡片与网格之上，太重会把带下的网格盖没。
    private readonly Color _rulerBackground = WorkflowSurfaceColors.Parse("#70252526");
    private readonly Color _labelColor = WorkflowSurfaceColors.Parse("#888888");
    private readonly Color _tickColor = WorkflowSurfaceColors.Parse("#555555");
    private readonly Color _axisColor = WorkflowSurfaceColors.Parse("#4D4D4D");
    private readonly Color _dividerColor = WorkflowSurfaceColors.Parse("#3A3D40");
    private readonly Font _labelFont = new("Segoe UI", 13f, GraphicsUnit.Pixel);
    private readonly StringFormat _labelFormat = new(StringFormat.GenericTypographic);
    private readonly CachedSolidBrush _rulerBrush = new();
    private readonly CachedSolidBrush _labelBrush = new();
    private readonly CachedPen _dividerPen = new(1f);
    private readonly CachedPen _tickPen = new(1f);
    private readonly CachedPen _axisPen = new(1f);

    private Bitmap? _surface;

    public WorkflowRulerOverlayForm(Control host, double rulerThickness)
    {
        HostControl = host ?? throw new ArgumentNullException(nameof(host));
        RulerThickness = rulerThickness;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ControlBox = false;
    }

    public Control HostControl { get; }

    public double RulerThickness { get; set; }

    public double ScrollOffsetX { get; set; }

    public double ScrollOffsetY { get; set; }

    public double ContentOffsetX { get; set; }

    public double ContentOffsetY { get; set; }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExLayered | WsExNoActivate | WsExToolWindow | WsExTransparent;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            m.Result = (IntPtr)HtTransparent;
            return;
        }

        base.WndProc(ref m);
    }

    // 把浮层对准视口并重画。宿主尺寸在排版之后才确定，所以每次同步都重算位置。
    public void SyncSurface()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (HostControl.Width < 1 || HostControl.Height < 1 || HostControl.IsDisposed)
        {
            return;
        }

        Location = HostControl.PointToScreen(Point.Empty);
        Size = HostControl.ClientSize;
        RefreshSurface();
    }

    // 重画分层表面。窗口自己的屏幕矩形就是位图尺寸；刻度与标签按逻辑空间画、再缩放填满。
    public void RefreshSurface()
    {
        if (IsDisposed || !IsHandleCreated || Width < 1 || Height < 1)
        {
            return;
        }

        GetWindowRect(Handle, out var rect);
        var pw = Math.Max(1, rect.Right - rect.Left);
        var ph = Math.Max(1, rect.Bottom - rect.Top);

        if (_surface is null || _surface.Width != pw || _surface.Height != ph)
        {
            _surface?.Dispose();
            _surface = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
        }

        using (var g = Graphics.FromImage(_surface))
        {
            g.Clear(Color.Transparent);
            g.ScaleTransform(
                pw / (float)Math.Max(1, Width),
                ph / (float)Math.Max(1, Height));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawBands(g, Width, Height);
        }

        UpdateLayeredSurface(rect, pw, ph);
    }

    private void DrawBands(Graphics g, int cw, int ch)
    {
        var ruler = (float)RulerThickness;
        var rulerBrush = _rulerBrush.Get(_rulerBackground);
        var dividerPen = _dividerPen.Get(_dividerColor);
        var tickPen = _tickPen.Get(_tickColor);
        var axisPen = _axisPen.Get(_axisColor);
        var labelBrush = _labelBrush.Get(_labelColor);
        var format = _labelFormat;

        g.FillRectangle(rulerBrush, 0, 0, cw, ruler);
        g.FillRectangle(rulerBrush, 0, 0, ruler, ch);

        g.DrawLine(dividerPen, ruler, 0, ruler, ch);
        g.DrawLine(dividerPen, 0, ruler, cw, ruler);

        var spacing = Math.Max(8, GridSpacing);
        var majorStep = spacing * Math.Max(1, MajorFreq);
        var worldLeft = WorkflowSurfaceMath.GridWorldLeft(ScrollOffsetX, ContentOffsetX);
        var worldTop = WorkflowSurfaceMath.GridWorldTop(ScrollOffsetY, ContentOffsetY);
        var worldRight = worldLeft + cw;
        var worldBottom = worldTop + ch;

        var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
        for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
        {
            var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, 0);
            if (x < ruler)
            {
                continue;
            }

            var isMajor = WorkflowSurfaceGrid.IsMajorLine(value, majorStep);
            var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
            var pen = WorkflowSurfaceGrid.IsNearZero(value) ? axisPen : tickPen;
            g.DrawLine(pen, x, ruler, x, (float)(ruler - tickLength));

            if (isMajor)
            {
                g.DrawString(WorkflowSurfaceGrid.FormatGridValue(value), _labelFont, labelBrush, x + 3, 2, format);
            }
        }

        var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
        for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
        {
            var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, 0);
            if (y < ruler)
            {
                continue;
            }

            var isMajor = WorkflowSurfaceGrid.IsMajorLine(value, majorStep);
            var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
            var pen = WorkflowSurfaceGrid.IsNearZero(value) ? axisPen : tickPen;
            g.DrawLine(pen, ruler, y, (float)(ruler - tickLength), y);

            if (isMajor)
            {
                g.DrawString(WorkflowSurfaceGrid.FormatGridValue(value), _labelFont, labelBrush, 3, y + 2, format);
            }
        }
    }

    private void UpdateLayeredSurface(RECT rect, int pw, int ph)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var hbitmap = _surface!.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(memDc, hbitmap);
        try
        {
            var ptDst = new POINT { X = rect.Left, Y = rect.Top };
            var size = new SIZE { cx = pw, cy = ph };
            var ptSrc = new POINT();
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,               // AC_SRC_OVER
                SourceConstantAlpha = 255,
                AlphaFormat = 1,           // AC_SRC_ALPHA —— 位图必须是预乘的
            };
            UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hbitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _surface?.Dispose();
            _surface = null;
            _labelFormat.Dispose();
            _labelFont.Dispose();
            _rulerBrush.Dispose();
            _labelBrush.Dispose();
            _dividerPen.Dispose();
            _tickPen.Dispose();
            _axisPen.Dispose();
        }

        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);
}
