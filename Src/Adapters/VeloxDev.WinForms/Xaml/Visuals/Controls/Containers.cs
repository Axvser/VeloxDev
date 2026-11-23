using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A header row that shows or hides one content element.</summary>
public class Expander : VisualElement
{
    /// <summary>The text shown in the header.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(Expander),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Whether the content is shown.</summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(Expander),
        new PropertyMetadata(true) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The content shown while expanded.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(VisualElement), typeof(Expander),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    private const float HeaderHeight = 32f;

    /// <summary>Creates an expanded expander with the current theme metrics.</summary>
    public Expander()
    {
        IsHitTestVisible = true;
        Focusable = true;
        Padding = new XamlThickness(0f);
        CornerRadius = Theme.Current.CornerRadius;
    }

    /// <summary>The text shown in the header.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty)!;
        set => SetValue(HeaderProperty, value ?? string.Empty);
    }

    /// <summary>Whether the content is shown.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>The content shown while expanded.</summary>
    public VisualElement? Content
    {
        get => (VisualElement?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0;
        var height = (int)Math.Ceiling(HeaderHeight + Padding.Vertical);
        if (IsExpanded && Content is { } content)
        {
            var measured = content.Measure(new Size(
                Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal)),
                0));
            width = measured.Width;
            height += measured.Height;
        }

        return new Size(width + (int)Math.Ceiling(Padding.Horizontal), height);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (!IsExpanded || Content is not { } content)
        {
            return;
        }

        content.Arrange(new RectangleF(
            (int)Math.Ceiling(Padding.Left),
            HeaderHeight,
            Math.Max(0f, finalSize.Width - Padding.Horizontal),
            Math.Max(0f, finalSize.Height - HeaderHeight - Padding.Vertical)));
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var header = new RectangleF(0f, 0f, Bounds.Width, HeaderHeight);
        using (var background = new SolidBrush(theme.SurfaceAlt))
        using (var path = XamlDrawing.RoundedRectangle(header, CornerRadius > 0 ? CornerRadius : theme.CornerRadius))
        {
            graphics.FillPath(background, path);
        }

        using (var border = new Pen(theme.ControlBorder, 1f))
        using (var path = XamlDrawing.RoundedRectangle(
                   new RectangleF(0.5f, 0.5f, Math.Max(0f, Bounds.Width - 1f), Math.Max(0f, HeaderHeight - 1f)),
                   CornerRadius > 0 ? CornerRadius : theme.CornerRadius))
        {
            graphics.DrawPath(border, path);
        }

        using (var chevron = new SolidBrush(theme.MutedForeground))
        {
            var x = 12f;
            var y = HeaderHeight / 2f;
            graphics.FillPolygon(chevron, IsExpanded
                ? new[]
                {
                    new PointF(x - 1f, y - 2f),
                    new PointF(x + 5f, y - 2f),
                    new PointF(x + 2f, y + 2f),
                }
                : new[]
                {
                    new PointF(x, y - 4f),
                    new PointF(x + 4f, y),
                    new PointF(x, y + 4f),
                });
        }

        VisualText.Draw(this,
            graphics,
            Header,
            ResolveFont(DefaultFont),
            theme.Foreground,
            new Rectangle(24, 0, Math.Max(0, (int)Bounds.Width - 32), (int)HeaderHeight),
            wrap: false,
            StringAlignment.Near,
            StringAlignment.Center);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ContentProperty))
        {
            if (e.OldValue is VisualElement old && Children.Contains(old))
            {
                RemoveChild(old);
            }

            if (e.NewValue is VisualElement next)
            {
                if (next.Parent is ContainerVisual parent)
                {
                    parent.Remove(next);
                }

                Add(next);
                next.IsVisible = IsExpanded;
            }

            InvalidateMeasure();
        }
        else if (ReferenceEquals(e.Property, IsExpandedProperty) && Content is { } content)
        {
            content.IsVisible = IsExpanded;
            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (args.Button != MouseButtons.Left || args.GetPosition(this).Y > HeaderHeight)
        {
            return;
        }

        Focus();
        IsExpanded = !IsExpanded;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.KeyCode is Keys.Enter or Keys.Space)
        {
            IsExpanded = !IsExpanded;
            args.Handled = true;
        }
    }
}

/// <summary>A titled frame with one child and a gap in its top border for the header.</summary>
public class GroupBox : VisualElement
{
    /// <summary>The text drawn in the top border gap.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(GroupBox),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The single child.</summary>
    public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        nameof(Child), typeof(VisualElement), typeof(GroupBox),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Creates an empty group box.</summary>
    public GroupBox()
    {
        IsHitTestVisible = false;
        Padding = new XamlThickness(10f);
        CornerRadius = 6f;
    }

    /// <summary>The text drawn in the top border gap.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty)!;
        set => SetValue(HeaderProperty, value ?? string.Empty);
    }

    /// <summary>The single child.</summary>
    public VisualElement? Child
    {
        get => (VisualElement?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>Adds the single child through <see cref="Child"/> so XAML content maps to it.</summary>
    /// <param name="child">The child to add.</param>
    /// <typeparam name="T">The child's concrete type.</typeparam>
    /// <returns>The added child.</returns>
    public new T Add<T>(T child) where T : VisualElement
    {
        Child = child;
        return child;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var inner = new Size(
            Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal)),
            Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical) - 8));
        var desired = Child?.Measure(inner) ?? Size.Empty;
        return new Size(
            desired.Width + (int)Math.Ceiling(padding.Horizontal),
            desired.Height + (int)Math.Ceiling(padding.Vertical) + 8);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child)
        {
            return;
        }

        var padding = Padding;
        child.Arrange(new RectangleF(
            (int)Math.Ceiling(padding.Left),
            14f + (int)Math.Ceiling(padding.Top),
            Math.Max(0f, finalSize.Width - padding.Horizontal),
            Math.Max(0f, finalSize.Height - padding.Vertical - 14f)));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!ReferenceEquals(e.Property, ChildProperty))
        {
            return;
        }

        if (e.OldValue is VisualElement old && Children.Contains(old))
        {
            RemoveChild(old);
        }

        if (e.NewValue is VisualElement next)
        {
            if (next.Parent is ContainerVisual parent)
            {
                parent.Remove(next);
            }

            // 本类隐藏了 Add<T> 去写 Child；这里必须走基类，否则不会挂进 Children 参与绘制。
            base.Add(next);
        }

        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var headerWidth = string.IsNullOrEmpty(Header)
            ? 0f
            : VisualText.Measure(Header, ResolveFont(DefaultFont), 0f, wrap: false, RenderDpi).Width + 12f;
        var border = BorderBrush?.ApproximateColor ?? theme.ControlBorder;
        using var pen = new Pen(border, 1f);
        using var path = XamlDrawing.RoundedRectangle(
            new RectangleF(0.5f, 0.5f, Math.Max(0f, Bounds.Width - 1f), Math.Max(0f, Bounds.Height - 1f)),
            CornerRadius);
        graphics.DrawPath(pen, path);

        if (headerWidth > 0f)
        {
            using var background = new SolidBrush(Background?.ApproximateColor ?? theme.Surface);
            graphics.FillRectangle(background, 10f, 0f, headerWidth, 14f);
            VisualText.Draw(this,
                graphics,
                Header,
                ResolveFont(DefaultFont),
                theme.Foreground,
                new Rectangle(14, 0, (int)headerWidth, 14),
                wrap: false,
                StringAlignment.Near,
                StringAlignment.Center);
        }
    }
}

/// <summary>One page of a <see cref="TabControl"/>.</summary>
public class TabItem : VisualElement
{
    /// <summary>The text shown on the tab.</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(TabItem),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The content shown while this tab is selected.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(VisualElement), typeof(TabItem),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Creates an empty tab.</summary>
    public TabItem()
    {
        Padding = new XamlThickness(0f);
    }

    /// <summary>The text shown on the tab.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty)!;
        set => SetValue(HeaderProperty, value ?? string.Empty);
    }

    /// <summary>The content shown while this tab is selected.</summary>
    public VisualElement? Content
    {
        get => (VisualElement?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
}

/// <summary>A tab strip with one visible page.</summary>
public class TabControl : VisualElement
{
    /// <summary>The zero-based index of the selected page, or -1.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(TabControl),
        new PropertyMetadata(-1) { AffectsMeasure = true, AffectsRender = true });

    private const float TabHeight = 32f;
    private readonly List<RectangleF> _tabs = [];

    /// <summary>Creates an empty tab control.</summary>
    public TabControl()
    {
        IsHitTestVisible = true;
        Focusable = true;
        Padding = new XamlThickness(0f);
    }

    /// <summary>Raised after <see cref="SelectedIndex"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The pages.</summary>
    public IList<TabItem> Items { get; } = new List<TabItem>();

    /// <summary>The zero-based index of the selected page, or -1.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0f;
        var pageWidth = 0;
        var pageHeight = 0;
        foreach (var item in Items)
        {
            width += VisualText.Measure(item.Header, ResolveFont(DefaultFont), 0f, wrap: false, RenderDpi).Width + 28f;
        }

        if (SelectedIndex >= 0 && SelectedIndex < Items.Count && Items[SelectedIndex].Content is { } content)
        {
            var measured = content.Measure(new Size(
                Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal)),
                0));
            pageWidth = measured.Width;
            pageHeight = measured.Height;
        }

        return new Size(
            Math.Max((int)Math.Ceiling(width), pageWidth) + (int)Math.Ceiling(Padding.Horizontal),
            (int)Math.Ceiling(TabHeight + pageHeight + Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (SelectedIndex >= 0 && SelectedIndex < Items.Count && Items[SelectedIndex].Content is { } content)
        {
            content.Arrange(new RectangleF(
                (int)Math.Ceiling(Padding.Left),
                TabHeight,
                Math.Max(0f, finalSize.Width - Padding.Horizontal),
                Math.Max(0f, finalSize.Height - TabHeight - Padding.Vertical)));
        }
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        _tabs.Clear();
        var x = (float)Math.Ceiling(Padding.Left);
        for (var index = 0; index < Items.Count; index++)
        {
            var width = (float)Math.Ceiling(
                VisualText.Measure(Items[index].Header, ResolveFont(DefaultFont), 0f, wrap: false, RenderDpi).Width + 28f);
            var tab = new RectangleF(x, 0f, width, TabHeight);
            _tabs.Add(tab);
            if (index == SelectedIndex)
            {
                using var background = new SolidBrush(theme.SurfaceAlt);
                graphics.FillRectangle(background, tab);
                using var accent = new Pen(theme.Accent, 2f);
                graphics.DrawLine(accent, tab.Left + 6f, tab.Bottom - 1f, tab.Right - 6f, tab.Bottom - 1f);
            }

            VisualText.Draw(this,
                graphics,
                Items[index].Header,
                ResolveFont(DefaultFont),
                index == SelectedIndex ? theme.Foreground : theme.MutedForeground,
                new Rectangle((int)tab.X, (int)tab.Y, (int)tab.Width, (int)tab.Height),
                wrap: false,
                StringAlignment.Center,
                StringAlignment.Center);
            x += width;
        }

        using var line = new Pen(theme.ControlBorder, 1f);
        graphics.DrawLine(line, 0f, TabHeight - 0.5f, Bounds.Width, TabHeight - 0.5f);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        var point = args.GetPosition(this);
        for (var index = 0; index < _tabs.Count; index++)
        {
            if (!_tabs[index].Contains(point))
            {
                continue;
            }

            Focus();
            SelectedIndex = index;
            args.Handled = true;
            return;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.KeyCode is not (Keys.Left or Keys.Right) || Items.Count == 0)
        {
            return;
        }

        var delta = args.KeyCode == Keys.Right ? 1 : -1;
        SelectedIndex = XamlMath.Clamp(SelectedIndex + delta, 0, Items.Count - 1);
        args.Handled = true;
    }

    /// <summary>Re-syncs the visible page after <see cref="Items"/> changed.</summary>
    public void Refresh()
    {
        SyncContent();
        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, SelectedIndexProperty))
        {
            SyncContent();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncContent()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index].Content is not { } content)
            {
                continue;
            }

            if (index == SelectedIndex)
            {
                if (!ReferenceEquals(content.ParentElement, this))
                {
                    if (content.Parent is ContainerVisual parent)
                    {
                        parent.Remove(content);
                    }

                    Add(content);
                }
            }
            else if (ReferenceEquals(content.ParentElement, this))
            {
                RemoveChild(content);
            }
        }
    }
}
