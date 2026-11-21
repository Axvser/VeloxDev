using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A control that shows one piece of content: a child control, a templated object, or text.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="Content"/> is a <see cref="System.Windows.Forms.Control"/> it is added as the single content child;
/// when it is any other object and <see cref="ContentTemplate"/> is set, the template creates the visual and receives
/// the object as its <see cref="Control.DataContext"/>. With neither, the object's string form is drawn directly.
/// </para>
/// <para>
/// A content control accepts at most one ordinary child. Buttons and other derived controls use that child as their
/// content visual, which lets XAML write the content as a nested element.
/// </para>
/// </remarks>
public class ContentControl : Control
{
    /// <summary>The content shown by this control.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content),
        typeof(object),
        typeof(ContentControl),
        new PropertyMetadata(null, OnContentPropertyChanged));

    /// <summary>The template used when <see cref="Content"/> is not already a control.</summary>
    public static readonly DependencyProperty ContentTemplateProperty = DependencyProperty.Register(
        nameof(ContentTemplate),
        typeof(DataTemplate),
        typeof(ContentControl),
        new PropertyMetadata(null, OnContentTemplatePropertyChanged));

    /// <summary>Chooses a template from the content object; a non-null result wins over <see cref="ContentTemplate"/>.</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty = DependencyProperty.Register(
        nameof(ContentTemplateSelector),
        typeof(TemplateSelector),
        typeof(ContentControl),
        new PropertyMetadata(null, OnContentTemplateSelectorPropertyChanged));

    /// <summary>How the content is placed horizontally inside the padding.</summary>
    public static readonly DependencyProperty HorizontalContentAlignmentProperty = DependencyProperty.Register(
        nameof(HorizontalContentAlignment),
        typeof(HorizontalAlignment),
        typeof(ContentControl),
        new PropertyMetadata(HorizontalAlignment.Left, OnContentLayoutPropertyChanged));

    /// <summary>How the content is placed vertically inside the padding.</summary>
    public static readonly DependencyProperty VerticalContentAlignmentProperty = DependencyProperty.Register(
        nameof(VerticalContentAlignment),
        typeof(VerticalAlignment),
        typeof(ContentControl),
        new PropertyMetadata(VerticalAlignment.Top, OnContentLayoutPropertyChanged));

    private System.Windows.Forms.Control? _templateContent;
    private System.Windows.Forms.Control? _contentChild;

    /// <summary>Creates the control with double buffering.</summary>
    public ContentControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    /// <summary>Gets or sets the content shown by this control.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>Gets or sets the template used when the content is not already a control.</summary>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>Gets or sets the per-content template selector.</summary>
    /// <remarks>A selector that returns a template for the content overrides <see cref="ContentTemplate"/>.</remarks>
    public TemplateSelector? ContentTemplateSelector
    {
        get => (TemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>Gets or sets how the content is placed horizontally inside the padding.</summary>
    public HorizontalAlignment HorizontalContentAlignment
    {
        get => (HorizontalAlignment)GetValue(HorizontalContentAlignmentProperty)!;
        set => SetValue(HorizontalContentAlignmentProperty, value);
    }

    /// <summary>Gets or sets how the content is placed vertically inside the padding.</summary>
    public VerticalAlignment VerticalContentAlignment
    {
        get => (VerticalAlignment)GetValue(VerticalContentAlignmentProperty)!;
        set => SetValue(VerticalContentAlignmentProperty, value);
    }

    /// <summary>The control currently representing the content, or null when the content is drawn as text.</summary>
    /// <remarks>A child declared directly in XAML becomes the content visual when no content object or template supplies one.</remarks>
    protected System.Windows.Forms.Control? ContentVisual
        => _contentChild
        ?? _templateContent
        ?? (Controls.Count > 0 ? Controls[0] : null);

    /// <inheritdoc />
    protected override ControlCollection CreateControlsInstance() => new SingleContentCollection(this);

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (ContentVisual is { } visual)
        {
            var measured = XamlLayout.Measure(visual, proposedSize);
            return new Size(
                measured.Width + (int)Math.Ceiling(Padding.Horizontal),
                measured.Height + (int)Math.Ceiling(Padding.Vertical));
        }

        var text = Content?.ToString() ?? string.Empty;
        if (text.Length == 0)
        {
            return new Size(
                (int)Math.Ceiling(Padding.Horizontal),
                (int)Math.Ceiling(Padding.Vertical));
        }

        var measuredText = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
        return new Size(
            measuredText.Width + (int)Math.Ceiling(Padding.Horizontal),
            measuredText.Height + (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var content = XamlLayout.ContentBounds(this);
        if (ContentVisual is { } visual)
        {
            // 内容控件的孩子同样要遵守对齐/显式尺寸，不能一律撑满内容区。
            if (visual is Control xaml)
            {
                XamlLayout.Arrange(xaml, content);
            }
            else if (visual.Bounds != content)
            {
                visual.Bounds = content;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!IsPainted || ContentVisual is not null)
        {
            return;
        }

        var text = Content?.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var bounds = XamlLayout.ContentBounds(this);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        flags |= HorizontalContentAlignment switch
        {
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            HorizontalAlignment.Right => TextFormatFlags.Right,
            _ => TextFormatFlags.Left,
        };
        flags |= VerticalContentAlignment switch
        {
            VerticalAlignment.Center => TextFormatFlags.VerticalCenter,
            VerticalAlignment.Bottom => TextFormatFlags.Bottom,
            _ => TextFormatFlags.Top,
        };

        TextRenderer.DrawText(e.Graphics, text, Font, bounds, ForeColor, flags);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveTemplateContent();
        }

        base.Dispose(disposing);
    }

    private static void OnContentPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ContentControl control && !control.IsDisposed)
        {
            control.ApplyContent();
        }
    }

    private static void OnContentTemplatePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ContentControl control && !control.IsDisposed)
        {
            control.RebuildTemplate();
        }
    }

    private static void OnContentTemplateSelectorPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ContentControl control && !control.IsDisposed)
        {
            control.RebuildTemplate();
        }
    }

    private static void OnContentLayoutPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.PerformLayout();
            control.Invalidate();
        }
    }

    private void ApplyContent()
    {
        if (Content is System.Windows.Forms.Control control)
        {
            RemoveTemplateContent();

            // 从「内容 A」换到「内容 B」时先把 A 摘掉：单内容集合会拒绝第二个子控件。
            if (!ReferenceEquals(_contentChild, control)
                && _contentChild is not null
                && Controls.Contains(_contentChild))
            {
                Controls.Remove(_contentChild);
            }

            if (!Controls.Contains(control))
            {
                Controls.Add(control);
            }

            _contentChild = control;
            PerformLayout();
            Invalidate();
            return;
        }

        if (_contentChild is not null && Controls.Contains(_contentChild))
        {
            Controls.Remove(_contentChild);
        }

        _contentChild = null;
        RebuildTemplate();
        PerformLayout();
        Invalidate();
    }

    private void RebuildTemplate()
    {
        RemoveTemplateContent();
        var template = ContentTemplateSelector?.SelectTemplate(Content) ?? ContentTemplate;
        if (Content is System.Windows.Forms.Control || template is not { } selected)
        {
            return;
        }

        _templateContent = selected.CreateContent();

        // DataTemplate 的工厂是消费者提供的；返回 null 时不能落进下面的分支再解引用。
        if (_templateContent is null)
        {
            return;
        }

        if (_templateContent is Control xaml)
        {
            xaml.DataContext = Content;
        }
        else
        {
#if NET8_0_OR_GREATER
            _templateContent.DataContext = Content;
#else
            _templateContent.Tag = Content;
#endif
        }

        Controls.Add(_templateContent);
        PerformLayout();
        Invalidate();
    }

    private void RemoveTemplateContent()
    {
        var content = _templateContent;
        if (content is null)
        {
            return;
        }

        _templateContent = null;
        if (Controls.Contains(content))
        {
            Controls.Remove(content);
        }

        content.Dispose();
    }

    // 内容槽只有一个：第二个普通子控件会静默抢走布局；模板控件由本类自己管理。
    private sealed class SingleContentCollection(ContentControl owner) : ControlCollection(owner)
    {
        public override void Add(System.Windows.Forms.Control? value)
        {
            if (value is not null && Count > 0 && !owner.Controls.Contains(value))
            {
                throw new InvalidOperationException("A ContentControl accepts a single content child.");
            }

            base.Add(value);
        }
    }
}
