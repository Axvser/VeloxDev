using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>How one scroll bar of a <see cref="ScrollViewer"/> decides whether it is shown.</summary>
public enum ScrollBarVisibility
{
    /// <summary>The bar is shown only when the extent is larger than the viewport.</summary>
    Auto = 0,

    /// <summary>The bar is always shown.</summary>
    Visible = 1,

    /// <summary>The bar is hidden, but its axis can still be scrolled programmatically.</summary>
    Hidden = 2,

    /// <summary>The bar is hidden and its axis stays at offset zero.</summary>
    Disabled = 3,
}

/// <summary>
/// A viewport that lays one content child into a bar-free area and drives panning through two styleable
/// <see cref="ScrollBar"/> children.
/// </summary>
/// <remarks>
/// <para>
/// The extent and the viewport are declared by the host rather than measured from the child: a virtualized
/// surface can be larger than the control it is rendered in. Assign <see cref="ExtentWidth"/>,
/// <see cref="ExtentHeight"/>, <see cref="ViewportWidth"/> and <see cref="ViewportHeight"/>, then
/// <see cref="ScrollOffset"/>; the bars are re-configured from those values.
/// </para>
/// <para>
/// <see cref="Scroll"/> is raised only for a button or thumb action on one of the bars. Writing
/// <see cref="ScrollOffset"/> never raises it, so a host can mirror its own pan into the viewer without a
/// feedback loop. When both axes are <see cref="ScrollBarVisibility.Auto"/>, the bars are measured in two
/// passes: a visible vertical bar narrows the horizontal viewport and may make the horizontal bar necessary,
/// and the other way round.
/// </para>
/// <para>
/// The bars are <see cref="ScrollBar"/> controls rather than native ones, so XAML can restyle them without the
/// system scrollbar theme. Assign <see cref="HorizontalBarStyle"/> and <see cref="VerticalBarStyle"/> to a
/// <see cref="Style"/> targeting <see cref="ScrollBar"/>, and <see cref="BarThickness"/> to size the gutter.
/// </para>
/// </remarks>
public class ScrollViewer : Control
{
    /// <summary>How the horizontal bar is shown; <see cref="ScrollBarVisibility.Auto"/> by default.</summary>
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(HorizontalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(ScrollViewer),
        new PropertyMetadata(ScrollBarVisibility.Auto, OnScrollLayoutChanged));

    /// <summary>How the vertical bar is shown; <see cref="ScrollBarVisibility.Auto"/> by default.</summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(VerticalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(ScrollViewer),
        new PropertyMetadata(ScrollBarVisibility.Auto, OnScrollLayoutChanged));

    /// <summary>The current scroll position, in pixels from the content origin.</summary>
    public static readonly DependencyProperty ScrollOffsetProperty = DependencyProperty.Register(
        nameof(ScrollOffset),
        typeof(Point),
        typeof(ScrollViewer),
        new PropertyMetadata(new Point(0, 0), OnScrollOffsetChanged));

    /// <summary>The width of the visible area the host reports, in pixels; zero means the control's client width.</summary>
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth),
        typeof(double),
        typeof(ScrollViewer),
        new PropertyMetadata(0d, OnScrollLayoutChanged));

    /// <summary>The height of the visible area the host reports, in pixels; zero means the control's client height.</summary>
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        nameof(ViewportHeight),
        typeof(double),
        typeof(ScrollViewer),
        new PropertyMetadata(0d, OnScrollLayoutChanged));

    /// <summary>The width of the scrollable content, in pixels.</summary>
    public static readonly DependencyProperty ExtentWidthProperty = DependencyProperty.Register(
        nameof(ExtentWidth),
        typeof(double),
        typeof(ScrollViewer),
        new PropertyMetadata(0d, OnScrollLayoutChanged));

    /// <summary>The height of the scrollable content, in pixels.</summary>
    public static readonly DependencyProperty ExtentHeightProperty = DependencyProperty.Register(
        nameof(ExtentHeight),
        typeof(double),
        typeof(ScrollViewer),
        new PropertyMetadata(0d, OnScrollLayoutChanged));

    /// <summary>The width of the scrollbar gutter, in pixels; 12 by default.</summary>
    public static readonly DependencyProperty BarThicknessProperty = DependencyProperty.Register(
        nameof(BarThickness),
        typeof(int),
        typeof(ScrollViewer),
        new PropertyMetadata(12, OnBarThicknessChanged));

    /// <summary>The style applied to the horizontal <see cref="ScrollBar"/>, or <see langword="null"/> for defaults.</summary>
    public static readonly DependencyProperty HorizontalBarStyleProperty = DependencyProperty.Register(
        nameof(HorizontalBarStyle),
        typeof(Style),
        typeof(ScrollViewer),
        new PropertyMetadata(null, OnBarStyleChanged));

    /// <summary>The style applied to the vertical <see cref="ScrollBar"/>, or <see langword="null"/> for defaults.</summary>
    public static readonly DependencyProperty VerticalBarStyleProperty = DependencyProperty.Register(
        nameof(VerticalBarStyle),
        typeof(Style),
        typeof(ScrollViewer),
        new PropertyMetadata(null, OnBarStyleChanged));

    private readonly ScrollBar horizontalScrollBar;
    private readonly ScrollBar verticalScrollBar;

    /// <summary>Raised after <see cref="ScrollOffset"/> or a viewport/extent value changes.</summary>
    public event EventHandler? ViewChanged;

    // 条的值与 ScrollOffset 互相写：任何一方的程序化同步都要把另一方的变更事件挡掉，否则会来回跳。
    private bool updatingBars;

    // 构造期 AutoScroll 就会触发一次布局，那时两条还不存在；这个标记把那次布局挡在 UpdateBars 之外。
    private bool barsReady;

    /// <summary>Creates the viewer and its two styleable bars.</summary>
    public ScrollViewer()
    {
        // 内容与滚动条都由本类摆放；AutoScroll 若打开，WinForms 会再挂一对窗口滚动条。
        AutoScroll = false;

        // 模板通常把视口设成 Transparent，让网格装饰器透上来；透明背景必须先声明支持。
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);

        // 两条都先建好再挂进 Controls：Controls.Add 会立刻触发一次布局，那时 UpdateBars 必须能读到它们。
        horizontalScrollBar = new ScrollBar
        {
            Name = "PART_HorizontalScrollBar",
            Orientation = ScrollBarOrientation.Horizontal,
            Minimum = 0,
            SmallChange = 16,
            Visible = false,
        };
        horizontalScrollBar.ValueChanged += OnBarValueChanged;

        verticalScrollBar = new ScrollBar
        {
            Name = "PART_VerticalScrollBar",
            Orientation = ScrollBarOrientation.Vertical,
            Minimum = 0,
            SmallChange = 16,
            Visible = false,
        };
        verticalScrollBar.ValueChanged += OnBarValueChanged;

        Controls.Add(horizontalScrollBar);
        Controls.Add(verticalScrollBar);
        barsReady = true;
        ApplyBarStyles();
    }

    /// <summary>Gets or sets how the horizontal bar is shown.</summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!;
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>Gets or sets how the vertical bar is shown.</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>Gets or sets the current scroll position, in pixels.</summary>
    /// <remarks>The value is clamped to <c>0..(Extent - Viewport)</c> on both axes.</remarks>
    public Point ScrollOffset
    {
        get => (Point)GetValue(ScrollOffsetProperty)!;
        set => SetValue(ScrollOffsetProperty, value);
    }

    /// <summary>Gets or sets the reported viewport width in pixels; zero means the client width.</summary>
    public double ViewportWidth
    {
        get => (double)GetValue(ViewportWidthProperty)!;
        set => SetValue(ViewportWidthProperty, value);
    }

    /// <summary>Gets or sets the reported viewport height in pixels; zero means the client height.</summary>
    public double ViewportHeight
    {
        get => (double)GetValue(ViewportHeightProperty)!;
        set => SetValue(ViewportHeightProperty, value);
    }

    /// <summary>Gets or sets the scrollable content width in pixels.</summary>
    public double ExtentWidth
    {
        get => (double)GetValue(ExtentWidthProperty)!;
        set => SetValue(ExtentWidthProperty, value);
    }

    /// <summary>Gets or sets the scrollable content height in pixels.</summary>
    public double ExtentHeight
    {
        get => (double)GetValue(ExtentHeightProperty)!;
        set => SetValue(ExtentHeightProperty, value);
    }

    /// <summary>Gets or sets the width of the scrollbar gutter in pixels.</summary>
    public int BarThickness
    {
        get => (int)GetValue(BarThicknessProperty)!;
        set => SetValue(BarThicknessProperty, value);
    }

    /// <summary>Gets or sets the style applied to the horizontal bar.</summary>
    /// <remarks>The style must target <see cref="ScrollBar"/>. Assigning it restyles the existing bar in place.</remarks>
    public Style? HorizontalBarStyle
    {
        get => (Style?)GetValue(HorizontalBarStyleProperty);
        set => SetValue(HorizontalBarStyleProperty, value);
    }

    /// <summary>Gets or sets the style applied to the vertical bar.</summary>
    /// <remarks>The style must target <see cref="ScrollBar"/>. Assigning it restyles the existing bar in place.</remarks>
    public Style? VerticalBarStyle
    {
        get => (Style?)GetValue(VerticalBarStyleProperty);
        set => SetValue(VerticalBarStyleProperty, value);
    }

    /// <summary>The horizontal bar, exposed so a host can drive, inspect or restyle it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ScrollBar HorizontalScrollBar => horizontalScrollBar;

    /// <summary>The vertical bar, exposed so a host can drive, inspect or restyle it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ScrollBar VerticalScrollBar => verticalScrollBar;

    /// <summary>Raised when a bar action produces a new <see cref="ScrollOffset"/>.</summary>
    /// <remarks>
    /// Not raised for a programmatic <see cref="ScrollOffset"/> write. This shadows the native
    /// <see cref="ScrollableControl.Scroll"/> event, which reports the viewport's own scrolling rather than this
    /// viewer's offset.
    /// </remarks>
    public new event EventHandler? Scroll;

    /// <inheritdoc />
    protected override ControlCollection CreateControlsInstance() => new SingleContentCollection(this);

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (!barsReady)
        {
            return;
        }

        UpdateBars();
        LayoutChildren();
    }

    private static void OnScrollLayoutChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ScrollViewer viewer && !viewer.IsDisposed)
        {
            viewer.UpdateBars();
            viewer.ViewChanged?.Invoke(viewer, EventArgs.Empty);
        }
    }

    private static void OnScrollOffsetChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ScrollViewer viewer && !viewer.IsDisposed && !viewer.updatingBars)
        {
            viewer.UpdateBars();
            viewer.ViewChanged?.Invoke(viewer, EventArgs.Empty);
        }
    }

    private static void OnBarThicknessChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ScrollViewer viewer && !viewer.IsDisposed)
        {
            viewer.UpdateBars();
            viewer.LayoutChildren();
        }
    }

    private static void OnBarStyleChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ScrollViewer viewer && !viewer.IsDisposed)
        {
            viewer.ApplyBarStyles();
        }
    }

    // 样式交给 Control 的显式 Style 管线立即应用；之后样式被修改时目标登记会重应用。
    private void ApplyBarStyles()
    {
        if (!barsReady || IsDisposed)
        {
            return;
        }

        if (!ReferenceEquals(horizontalScrollBar.Style, HorizontalBarStyle))
        {
            horizontalScrollBar.Style = HorizontalBarStyle;
        }

        if (!ReferenceEquals(verticalScrollBar.Style, VerticalBarStyle))
        {
            verticalScrollBar.Style = VerticalBarStyle;
        }
    }

    private void OnBarValueChanged(object? sender, EventArgs e)
    {
        if (updatingBars || IsDisposed)
        {
            return;
        }

        var offset = new Point(
            horizontalScrollBar.Visible ? horizontalScrollBar.Value : 0,
            verticalScrollBar.Visible ? verticalScrollBar.Value : 0);

        if (offset == ScrollOffset)
        {
            return;
        }

        updatingBars = true;
        try
        {
            SetValue(ScrollOffsetProperty, offset);
        }
        finally
        {
            updatingBars = false;
        }

        // 拖动条时 OnScrollOffsetChanged 被 updatingBars 门住，不会发 ViewChanged；
        // 这里补一发，虚拟化宿主的可见集才会跟着条走。
        ViewChanged?.Invoke(this, EventArgs.Empty);
        Scroll?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!IsEnabled || e.Delta == 0 || IsDisposed)
        {
            return;
        }

        var lines = SystemInformation.MouseWheelScrollLines;
        var step = lines > 0
            ? lines * 16
            : Math.Max(16, (int)Math.Round(EffectiveViewportHeight / 3d));
        var notches = e.Delta / (double)SystemInformation.MouseWheelScrollDelta;
        var shift = (ModifierKeys & Keys.Shift) != 0;
        var offset = ScrollOffset;
        var x = offset.X;
        var y = offset.Y;

        if (shift && horizontalScrollBar.Visible)
        {
            x = Clamp(x - (int)Math.Round(notches * step), 0, MaxOffsetX);
        }
        else if (verticalScrollBar.Visible)
        {
            y = Clamp(y - (int)Math.Round(notches * step), 0, MaxOffsetY);
        }
        else if (horizontalScrollBar.Visible)
        {
            x = Clamp(x - (int)Math.Round(notches * step), 0, MaxOffsetX);
        }
        else
        {
            return;
        }

        if (x == offset.X && y == offset.Y)
        {
            return;
        }

        // 与拖动滚动条走同一条 DP 通路；ViewChanged 会让虚拟化宿主重算可见集。
        SetValue(ScrollOffsetProperty, new Point(x, y));
    }

    private int EffectiveViewportWidth => ViewportWidth > 0d ? (int)Math.Round(ViewportWidth) : ClientSize.Width;

    private int EffectiveViewportHeight => ViewportHeight > 0d ? (int)Math.Round(ViewportHeight) : ClientSize.Height;

    private int MaxOffsetX => (int)Math.Round(Math.Max(0d, ExtentWidth - EffectiveViewportWidth));

    private int MaxOffsetY => (int)Math.Round(Math.Max(0d, ExtentHeight - EffectiveViewportHeight));

    private static int Clamp(int value, int minimum, int maximum)
        => value < minimum ? minimum : value > maximum ? maximum : value;

    // 视口与范围先算，再夹偏移、同步两条原生滚动条；两次收敛处理纵横条互相占用空间。
    private void UpdateBars()
    {
        if (!barsReady || updatingBars || IsDisposed)
        {
            return;
        }

        updatingBars = true;
        try
        {
            var viewportWidth = ViewportWidth > 0d ? ViewportWidth : ClientSize.Width;
            var viewportHeight = ViewportHeight > 0d ? ViewportHeight : ClientSize.Height;
            var extentWidth = Math.Max(0d, ExtentWidth);
            var extentHeight = Math.Max(0d, ExtentHeight);

            // 条宽由本类声明，不再询问原生 PreferredSize 或系统度量 —— 句柄创建前它们都不可靠。
            var barHeight = ResolvedBarThickness;
            var barWidth = ResolvedBarThickness;

            var showVertical = NeedsBar(VerticalScrollBarVisibility, extentHeight, viewportHeight);
            var showHorizontal = NeedsBar(
                HorizontalScrollBarVisibility, extentWidth, viewportWidth - (showVertical ? barWidth : 0));
            showVertical = NeedsBar(
                VerticalScrollBarVisibility, extentHeight, viewportHeight - (showHorizontal ? barHeight : 0));

            horizontalScrollBar.Visible = showHorizontal;
            verticalScrollBar.Visible = showVertical;

            var innerWidth = Math.Max(0d, viewportWidth - (showVertical ? barWidth : 0));
            var innerHeight = Math.Max(0d, viewportHeight - (showHorizontal ? barHeight : 0));

            var maxX = HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled
                ? 0
                : (int)Math.Round(Math.Max(0d, extentWidth - innerWidth));
            var maxY = VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
                ? 0
                : (int)Math.Round(Math.Max(0d, extentHeight - innerHeight));

            var offset = ScrollOffset;
            var x = Clamp(offset.X, 0, maxX);
            var y = Clamp(offset.Y, 0, maxY);
            if (x != offset.X || y != offset.Y)
            {
                SetValue(ScrollOffsetProperty, new Point(x, y));
            }

            ConfigureBar(horizontalScrollBar, maxX, (int)Math.Round(innerWidth), x);
            ConfigureBar(verticalScrollBar, maxY, (int)Math.Round(innerHeight), y);
        }
        finally
        {
            updatingBars = false;
        }
    }

    private int ResolvedBarThickness => Math.Max(1, BarThickness);

    private void LayoutChildren()
    {
        var barWidth = verticalScrollBar.Visible ? ResolvedBarThickness : 0;
        var barHeight = horizontalScrollBar.Visible ? ResolvedBarThickness : 0;

        var width = ClientSize.Width;
        var height = ClientSize.Height;

        var horizontalBounds = new Rectangle(0, Math.Max(0, height - barHeight), Math.Max(0, width - barWidth), barHeight);
        var verticalBounds = new Rectangle(Math.Max(0, width - barWidth), 0, barWidth, Math.Max(0, height - barHeight));
        var contentBounds = new Rectangle(0, 0, Math.Max(0, width - barWidth), Math.Max(0, height - barHeight));

        if (horizontalScrollBar.Bounds != horizontalBounds)
        {
            horizontalScrollBar.Bounds = horizontalBounds;
        }

        if (verticalScrollBar.Bounds != verticalBounds)
        {
            verticalScrollBar.Bounds = verticalBounds;
        }

        if (Content is { } content && content.Bounds != contentBounds)
        {
            content.Bounds = contentBounds;
        }

        // 内容通常是在两条建好之后由 XAML 加进来的；把条抬回最上层，避免透明/自绘内容盖住它们。
        if (horizontalScrollBar.Visible)
        {
            horizontalScrollBar.BringToFront();
        }

        if (verticalScrollBar.Visible)
        {
            verticalScrollBar.BringToFront();
        }
    }

    private System.Windows.Forms.Control? Content
    {
        get
        {
            foreach (System.Windows.Forms.Control child in Controls)
            {
                if (!ReferenceEquals(child, horizontalScrollBar) && !ReferenceEquals(child, verticalScrollBar))
                {
                    return child;
                }
            }

            return null;
        }
    }

    private static bool NeedsBar(ScrollBarVisibility visibility, double extent, double viewport) => visibility switch
    {
        ScrollBarVisibility.Visible => true,
        ScrollBarVisibility.Auto => extent > viewport + 0.5d,
        _ => false,
    };

    // Value 的上界是 Maximum - LargeChange + 1；把 LargeChange 设成视口、Maximum 设成 max + 视口 - 1，
    // 滑块才能正好走到 max = extent - viewport。
    private static void ConfigureBar(ScrollBar bar, int max, int viewport, int value)
    {
        var largeChange = Math.Max(1, viewport);
        if (bar.Minimum != 0)
        {
            bar.Minimum = 0;
        }

        if (bar.LargeChange != largeChange)
        {
            bar.LargeChange = largeChange;
        }

        var maximum = max + largeChange - 1;
        if (bar.Maximum != maximum)
        {
            bar.Maximum = maximum;
        }

        if (bar.Value != value)
        {
            bar.Value = value;
        }
    }

    // 内容槽只有一个：第二个普通子控件会静默抢走布局；滚动条是本类自己的，不占这一槽。
    private sealed class SingleContentCollection(ScrollViewer owner) : ControlCollection(owner)
    {
        public override void Add(System.Windows.Forms.Control? value)
        {
            if (value is not null and not ScrollBar && owner.Content is not null)
            {
                throw new InvalidOperationException("A ScrollViewer accepts a single content control.");
            }

            base.Add(value);
        }
    }
}
