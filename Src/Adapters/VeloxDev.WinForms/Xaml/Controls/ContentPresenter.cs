using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Creates the visual for a piece of content: a control, a templated object, or text.
/// </summary>
/// <remarks>
/// <para>
/// This is the template-facing counterpart of <see cref="ContentControl"/>. Inside a
/// <see cref="VeloxDev.WinForms.Xaml.ControlTemplate"/> the templated parent and the inherited data context are already
/// in place, so a presenter with only <see cref="ContentTemplate"/> set templates the effective data context.
/// </para>
/// <para>
/// When <see cref="Content"/> is already a <see cref="System.Windows.Forms.Control"/> it is hosted directly; otherwise
/// <see cref="ContentTemplateSelector"/> or <see cref="ContentTemplate"/> creates the visual and receives the content
/// object as its <see cref="Control.DataContext"/>. With neither, the content's string form is drawn directly.
/// </para>
/// </remarks>
public class ContentPresenter : Control
{
    /// <summary>The object shown by this presenter.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content),
        typeof(object),
        typeof(ContentPresenter),
        new PropertyMetadata(null, OnContentPropertyChanged));

    /// <summary>The template used when <see cref="Content"/> is not already a control.</summary>
    public static readonly DependencyProperty ContentTemplateProperty = DependencyProperty.Register(
        nameof(ContentTemplate),
        typeof(DataTemplate),
        typeof(ContentPresenter),
        new PropertyMetadata(null, OnContentTemplatePropertyChanged));

    /// <summary>Chooses a template per content object; a non-null result wins over <see cref="ContentTemplate"/>.</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty = DependencyProperty.Register(
        nameof(ContentTemplateSelector),
        typeof(TemplateSelector),
        typeof(ContentPresenter),
        new PropertyMetadata(null, OnContentTemplatePropertyChanged));

    private System.Windows.Forms.Control? _contentChild;
    private System.Windows.Forms.Control? _templateContent;

    // 模板内容在工厂里构造时 TemplatedParent 还没设；布局时比对一次，把模板重新套到真正的模板父上。
    private Control? _observedTemplatedParent;

    /// <summary>Creates the presenter with double buffering.</summary>
    public ContentPresenter()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    /// <summary>Gets or sets the object shown by this presenter.</summary>
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

    /// <summary>The control currently representing the content, or <see langword="null"/> when it is drawn as text.</summary>
    /// <remarks>A child declared directly in XAML becomes the content visual when no content object or template supplies one.</remarks>
    public System.Windows.Forms.Control? ContentVisual
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

        var text = EffectiveContent()?.ToString() ?? string.Empty;
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
        SyncTemplatedParent();
        base.OnLayout(e);
        if (ContentVisual is not { } visual)
        {
            return;
        }

        var bounds = XamlLayout.ContentBounds(this);
        if (visual.Bounds != bounds)
        {
            visual.Bounds = bounds;
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

        var text = EffectiveContent()?.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            XamlLayout.ContentBounds(this),
            EffectiveForeground,
            TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    /// <inheritdoc />
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);

        // 模板内容先构造成员、后设 TemplatedParent；挂进树时重新套一次模板，才能读到模板父的内容。
        if (!IsDisposed && !Disposing
            && Content is null
            && (ContentTemplate is not null || ContentTemplateSelector is not null))
        {
            RebuildTemplate();
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged()
    {
        base.OnDataContextChanged();

        if (!IsDisposed && !Disposing
            && Content is null
            && (ContentTemplate is not null || ContentTemplateSelector is not null))
        {
            RebuildTemplate();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ObserveTemplatedParent(null);
            RemoveTemplateContent();
        }

        base.Dispose(disposing);
    }

    // TemplatedParent 的内容变化没有经过本控件的 DP 管道；订阅模板父的变更事件，内容换了就重套模板。
    private void OnTemplatedParentPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsDisposed || Disposing || Content is not null)
        {
            return;
        }

        if (ReferenceEquals(e.Property, ContentControl.ContentProperty))
        {
            RebuildTemplate();
        }
    }

    private void ObserveTemplatedParent(Control? parent)
    {
        if (ReferenceEquals(parent, _observedTemplatedParent))
        {
            return;
        }

        if (_observedTemplatedParent is not null)
        {
            _observedTemplatedParent.DependencyPropertyChanged -= OnTemplatedParentPropertyChanged;
        }

        _observedTemplatedParent = parent;
        if (parent is not null)
        {
            parent.DependencyPropertyChanged += OnTemplatedParentPropertyChanged;
        }
    }

    // TemplatedParent 不是依赖属性，没有变更通知；布局时补一次检查，模板父从无到有或更换时重套模板。
    private void SyncTemplatedParent()
    {
        if (IsDisposed || Disposing
            || Content is not null
            || (ContentTemplate is null && ContentTemplateSelector is null))
        {
            return;
        }

        var current = TemplatedParent;
        if (ReferenceEquals(current, _observedTemplatedParent))
        {
            return;
        }

        ObserveTemplatedParent(current);
        RebuildTemplate();
    }

    // Content 为空但给了模板时，按 WPF ContentPresenter 的默认模板绑定取模板父的内容，再退回继承的 DataContext。
    private object? EffectiveContent()
    {
        if (Content is not null)
        {
            return Content;
        }

        if (ContentTemplate is null && ContentTemplateSelector is null)
        {
            return null;
        }

        if (TemplatedParent is ContentControl templated && templated.Content is not null)
        {
            return templated.Content;
        }

        return DataContext;
    }

    private static void OnContentPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ContentPresenter presenter && !presenter.IsDisposed)
        {
            presenter.ApplyContent();
        }
    }

    private static void OnContentTemplatePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ContentPresenter presenter && !presenter.IsDisposed)
        {
            presenter.RebuildTemplate();
        }
    }

    private void ApplyContent()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (Content is System.Windows.Forms.Control control)
        {
            RemoveTemplateContent();

            // 单内容集合拒绝第二个子控件；直接声明的旧子控件也要让位。
            RemoveOtherChildren(control);
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
        if (IsDisposed || Disposing)
        {
            return;
        }

        ObserveTemplatedParent(TemplatedParent);
        RemoveTemplateContent();
        var content = EffectiveContent();
        if (content is System.Windows.Forms.Control)
        {
            // 控件内容的归属归内容控件自己；模板型 presenter 只消费显式 Content，不从这里重新挂父。
            return;
        }

        var template = ContentTemplateSelector?.SelectTemplate(content) ?? ContentTemplate;
        if (template is null)
        {
            PerformLayout();
            Invalidate();
            return;
        }

        _templateContent = template.CreateContent();

        // 工厂是消费者提供的；返回 null 时退回文本绘制，而不是把 null 交给布局。
        if (_templateContent is null)
        {
            PerformLayout();
            Invalidate();
            return;
        }

        if (_templateContent is Control xaml)
        {
            xaml.DataContext = content;
        }
        else
        {
#if NET8_0_OR_GREATER
            _templateContent.DataContext = content;
#else
            _templateContent.Tag = content;
#endif
        }

        RemoveOtherChildren(_templateContent);
        Controls.Add(_templateContent);
        PerformLayout();
        Invalidate();
    }

    // 单子集合的约束在这里统一处理：模板内容或新内容控件要进来时，先摘掉不是它的直接子控件。
    private void RemoveOtherChildren(System.Windows.Forms.Control? keep)
    {
        foreach (var child in Controls.Cast<System.Windows.Forms.Control>().ToArray())
        {
            if (!ReferenceEquals(child, keep) && Controls.Contains(child))
            {
                Controls.Remove(child);
            }
        }
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

    // 内容槽只有一个：模板控件由本类管理，直接声明的子控件也只能有一个。
    private sealed class SingleContentCollection(ContentPresenter owner) : ControlCollection(owner)
    {
        public override void Add(System.Windows.Forms.Control? value)
        {
            if (value is not null && Count > 0 && !owner.Controls.Contains(value))
            {
                throw new InvalidOperationException("A ContentPresenter accepts a single content child.");
            }

            base.Add(value);
        }
    }
}
