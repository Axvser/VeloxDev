using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A container that paints a rounded background and border and lays out one child inside its padding.
/// </summary>
/// <remarks>
/// <para>
/// WinForms has no border control of its own, so this is the markup-facing stand-in: declare a background, a brush, a
/// thickness and a corner radius, then put the single content control inside. The surface is erased to
/// <see cref="Backdrop"/> before the rounded body is filled, because a WinForms child window does not composite over
/// its parent — without that fill a semi-transparent <see cref="Background"/> would blend against a stale buffer.
/// </para>
/// <para>
/// Exactly one child is accepted. Its bounds are the control's display rectangle inset by
/// <see cref="BorderThickness"/> and <see cref="System.Windows.Forms.Control.Padding"/>, so neither the stroke nor the
/// padding can be covered by the content.
/// </para>
/// </remarks>
public class Border : Control
{
    /// <summary>The rounded body brush; its alpha composites over <see cref="Backdrop"/>.</summary>
    public new static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background),
        typeof(Brush),
        typeof(Border),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>The brush of the border stroke.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Brush),
        typeof(Border),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>The width of the border stroke per side, in pixels; an empty thickness draws no stroke.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(Border),
        new PropertyMetadata(Thickness.Empty, OnVisualPropertyChanged));

    /// <summary>The corner radius, in pixels; it is clamped to half the shorter side.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(float),
        typeof(Border),
        new PropertyMetadata(0f, OnVisualPropertyChanged));

    /// <summary>The colour painted behind the rounded body, showing through the corners and any alpha body.</summary>
    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(
        nameof(Backdrop),
        typeof(Color),
        typeof(Border),
        new PropertyMetadata(Color.Empty, OnVisualPropertyChanged));

    /// <summary>Creates the border with the double-buffered paint style it needs.</summary>
    public Border()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the rounded body brush.</summary>
    public new Brush? Background
    {
        get => GetValue(BackgroundProperty) as Brush;
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Gets or sets the border stroke brush.</summary>
    public Brush? BorderBrush
    {
        get => GetValue(BorderBrushProperty) as Brush;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Gets or sets the border stroke width per side in pixels.</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>Gets or sets the corner radius in pixels.</summary>
    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Gets or sets the colour painted behind the rounded body.</summary>
    public Color Backdrop
    {
        get => (Color)GetValue(BackdropProperty)!;
        set => SetValue(BackdropProperty, value);
    }

    /// <inheritdoc />
    protected override ControlCollection CreateControlsInstance() => new SingleChildCollection(this);

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0, 0, Width, Height);

        // 圆角外侧不属于 Background 的路径：不垫父容器就会留下子窗口默认的黑色边。半透明/渐变同样要垫底，
        // 否则子窗口 DC 里的旧内容会混进来。
        if (Backdrop.IsEmpty)
        {
            PaintParentBackground(e);
            if (Background is null
                && !HasXamlValue(BackgroundProperty)
                && (BorderBrush is null || BorderBrush.ApproximateColor.A <= 0 || BorderThickness.IsEmpty))
            {
                // 只有描边的 Border 也必须画出来：早退条件只看底色会漏掉整圈边框。
                return;
            }
        }

        // 子窗口不与父窗口合成：先用不透明底色擦一遍，带 alpha 的 Background 才有确定的合成结果。
        var backdrop = Backdrop.IsEmpty
            ? (HasXamlValue(BackgroundProperty) ? EffectiveBackground : Color.Transparent)
            : Backdrop;
        if (backdrop.A > 0)
        {
            using var backdropBrush = new SolidBrush(backdrop);
            graphics.FillRectangle(backdropBrush, bounds);
        }

        var path = CreatePath(bounds, CornerRadius);
        if (path is null)
        {
            return;
        }

        using (path)
        {
            if (Background is { } body && body.ApproximateColor.A > 0)
            {
                body.FillPath(graphics, path);
            }

            var thickness = BorderThickness;
            if (BorderBrush is { } stroke && stroke.ApproximateColor.A > 0 && !thickness.IsEmpty)
            {
                using var ring = CreateBorderRing(StrokeBounds(bounds), CornerRadius, thickness);
                if (ring is not null)
                {
                    stroke.FillPath(graphics, ring);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        if (Controls.Count == 0)
        {
            return;
        }

        // 用原生 Padding：DP 的每次变化都会同步到它，而直接改原生属性的代码（缩放、设计器）也不会被忽略。
        // base 指向 Control（它已隐藏 Padding），所以必须显式落到 ScrollableControl 的成员上。
        var padding = ((System.Windows.Forms.ScrollableControl)this).Padding;
        var border = BorderThickness;
        var content = new Rectangle(
            padding.Left + (int)Math.Ceiling(border.Left),
            padding.Top + (int)Math.Ceiling(border.Top),
            Math.Max(0, ClientSize.Width - padding.Horizontal - (int)Math.Ceiling(border.Horizontal)),
            Math.Max(0, ClientSize.Height - padding.Vertical - (int)Math.Ceiling(border.Vertical)));

        var child = Controls[0];
        if (child is Control xamlChild)
        {
            // 单内容槽也要走对齐：VerticalAlignment="Center" 的文本在 48px 的圆角块里才会真正居中，
            // 直接 child.Bounds = content 会让 TextBlock 撑满内容区、文字落在顶部。
            XamlLayout.Arrange(xamlChild, content);
        }
        else if (child.Bounds != content)
        {
            child.Bounds = content;
        }
    }

    /// <inheritdoc />
    protected internal override Size MeasureCore(Size availableSize)
    {
        // Border 的首选尺寸来自唯一子控件 + Padding + 描边：卡片不再写死高度，改内容后也不会被裁掉。
        var padding = Padding;
        var border = BorderThickness;
        var horizontal = (int)Math.Ceiling(padding.Horizontal) + (int)Math.Ceiling(border.Horizontal);
        var vertical = (int)Math.Ceiling(padding.Vertical) + (int)Math.Ceiling(border.Vertical);
        var inner = new Size(
            availableSize.Width > 0 ? Math.Max(0, availableSize.Width - horizontal) : 0,
            availableSize.Height > 0 ? Math.Max(0, availableSize.Height - vertical) : 0);

        var measured = Controls.Count == 0
            ? Size.Empty
            : XamlLayout.Measure(Controls[0], inner);
        var preferred = new Size(measured.Width + horizontal, measured.Height + vertical);

        return new Size(
            MeasureAxis(WidthProperty, preferred.Width, MinWidth, MaxWidth, availableSize.Width),
            MeasureAxis(HeightProperty, preferred.Height, MinHeight, MaxHeight, availableSize.Height));
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Border border && !border.IsDisposed)
        {
            border.Invalidate();
        }
    }

    // GDI+ 的 AA 填充按像素中心取样：外轮廓贴着 0 时四边各占半个像素，右、下再被客户区裁掉一半。
    // 外轮廓压到 -0.5 后，1px 环正好覆盖最外一整像素，内缘落在子控件的起点之外。
    private static RectangleF StrokeBounds(RectangleF bounds)
        => bounds.Width > 0f && bounds.Height > 0f
            ? new RectangleF(bounds.X - 0.5f, bounds.Y - 0.5f, bounds.Width, bounds.Height)
            : bounds;

    // 边框按每边宽度画成「外轮廓减内轮廓」的环：GDI+ 的单支笔只能有一种宽度，WPF 的 BorderThickness 是四边各一个。
    private static GraphicsPath? CreateBorderRing(RectangleF bounds, float radius, Thickness thickness)
    {
        var left = Math.Max(0f, thickness.Left);
        var top = Math.Max(0f, thickness.Top);
        var right = Math.Max(0f, thickness.Right);
        var bottom = Math.Max(0f, thickness.Bottom);

        var outer = CreatePath(bounds, radius);
        if (outer is null)
        {
            return null;
        }

        var inner = new RectangleF(
            bounds.X + left,
            bounds.Y + top,
            bounds.Width - left - right,
            bounds.Height - top - bottom);
        if (inner.Width <= 0f || inner.Height <= 0f)
        {
            return outer;
        }

        var inset = Math.Max(Math.Max(left, right), Math.Max(top, bottom));
        var innerPath = CreatePath(inner, Math.Max(0f, radius - inset));
        if (innerPath is null)
        {
            return outer;
        }

        using (innerPath)
        {
            // 内轮廓整块落在里面：Alternate 填充下偶数次覆盖即被挖空，得到环。
            outer.FillMode = FillMode.Alternate;
            outer.AddPath(innerPath, connect: false);
        }

        return outer;
    }

    private static GraphicsPath? CreatePath(RectangleF bounds, float radius)
    {
        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return null;
        }

        var path = new GraphicsPath();
        if (radius <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        // 半径超过短边的一半时 GDI+ 的圆弧会互相覆盖，先夹到一半，形状与 WPF 的 Border 一致。
        var clamped = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        var diameter = 2f * clamped;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    // 内容槽只有一个：第二个子控件会静默破坏圆角与布局，因此直接报错而不是悄悄忽略。
    private sealed class SingleChildCollection(System.Windows.Forms.Control owner) : ControlCollection(owner)
    {
        public override void Add(System.Windows.Forms.Control? value)
        {
            if (value is not null && Count > 0)
            {
                throw new InvalidOperationException("A Border accepts a single child control.");
            }

            base.Add(value);
        }
    }
}
