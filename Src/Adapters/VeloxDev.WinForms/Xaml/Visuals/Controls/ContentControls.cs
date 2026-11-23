using System.Drawing;
using System.Globalization;
using VeloxDev.WinForms.Xaml;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A visual control that shows one piece of content, either directly or through a <see cref="VisualControlTemplate"/>.</summary>
/// <remarks>
/// <para>
/// Without a control template the content is the only child: a <see cref="VisualElement"/> is shown as-is, otherwise
/// <see cref="ContentTemplateSelector"/> picks a template before <see cref="ContentTemplate"/> is used, and content
/// without a template becomes a <see cref="TextBlock"/> through <see cref="ContentStringFormat"/>.
/// With a control template the template's root is the only child, and every <see cref="ContentPresenter"/> inside it
/// shows the content instead; a presenter prefers its own <see cref="ContentPresenter.ContentTemplateSelector"/>, then
/// its own <see cref="ContentPresenter.ContentTemplate"/>, then this control's.
/// </para>
/// <para>
/// The control template comes from <see cref="Template"/> or, when that is <see langword="null"/>, from the applied
/// <see cref="VisualStyle.Template"/>. Named parts resolve through
/// <see cref="VisualElement.GetTemplateChild{T}(string)"/>; the scope belongs to this instance, not to the template
/// description.
/// </para>
/// </remarks>
public class ContentControl : VisualElement
{
    /// <summary>The object shown by this control or by its content presenters.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(ContentControl),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The template that turns non-visual content into a visual, or <see langword="null"/> for the text projection.</summary>
    public static readonly DependencyProperty ContentTemplateProperty = DependencyProperty.Register(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(ContentControl),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Chooses the template for the current content, or <see langword="null"/> to use <see cref="ContentTemplate"/>.</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty = DependencyProperty.Register(
        nameof(ContentTemplateSelector), typeof(DataTemplateSelector), typeof(ContentControl),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The format applied when the content becomes text, or empty to use <see cref="object.ToString"/>.</summary>
    public static readonly DependencyProperty ContentStringFormatProperty = DependencyProperty.Register(
        nameof(ContentStringFormat), typeof(string), typeof(ContentControl),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>A control template applied directly to this control, or <see langword="null"/> to use the style's template.</summary>
    public static readonly DependencyProperty TemplateProperty = DependencyProperty.Register(
        nameof(Template), typeof(VisualControlTemplate), typeof(ContentControl),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    private VisualControlTemplate? _styleTemplate;
    private VisualControlTemplate? _appliedTemplate;
    private VisualElement? _templateRoot;
    private VisualTemplateTriggerApplication? _templateTriggers;
    private VisualElement? _contentVisual;
    private VisualElement? _textVisual;
    private DataTemplate? _dataTemplate;
    private DataTemplateSelector? _dataSelector;
    private object? _dataContent;
    private VisualElement? _dataVisual;
    private readonly List<ContentPresenter> _presenters = [];

    /// <summary>The object shown by this control or by its content presenters.</summary>
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>The template that turns non-visual content into a visual, or <see langword="null"/> for the text projection.</summary>
    /// <remarks>
    /// A <see cref="VisualElement"/> content is shown as-is and ignores this template. When a control template is
    /// applied, the content presenters apply this template on their own.
    /// </remarks>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>Chooses the template for the current content, or <see langword="null"/> to use <see cref="ContentTemplate"/>.</summary>
    /// <remarks>
    /// The selector receives the raw <see cref="Content"/> and this control. A non-null result wins over
    /// <see cref="ContentTemplate"/>; a <see cref="VisualElement"/> content bypasses both.
    /// </remarks>
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>The format applied when the content becomes text, or empty to use <see cref="object.ToString"/>.</summary>
    /// <remarks>The format is ignored when a template or selector supplies a visual.</remarks>
    public string ContentStringFormat
    {
        get => (string)GetValue(ContentStringFormatProperty)!;
        set => SetValue(ContentStringFormatProperty, value ?? string.Empty);
    }

    /// <summary>A control template applied directly to this control, or <see langword="null"/> to use the style's template.</summary>
    public VisualControlTemplate? Template
    {
        get => (VisualControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>The visual currently shown as content, or <see langword="null"/>.</summary>
    public VisualElement? ContentVisual => _contentVisual;

    /// <summary>The template root currently applied, or <see langword="null"/>.</summary>
    public VisualElement? TemplateRoot => _templateRoot;

    /// <summary>The template used when neither <see cref="Template"/> nor a style supplies one.</summary>
    /// <remarks>A control with a built-in chrome (for example a scroll viewer) overrides this instead of setting
    /// <see cref="Template"/> locally, so a style or an explicit template still wins.</remarks>
    protected virtual VisualControlTemplate? DefaultTemplate => null;

    /// <summary>Applies the effective control template again, after <see cref="DefaultTemplate"/> initialized.</summary>
    protected void ReapplyTemplate() => UpdateTemplate();

    internal override void ApplyStyleTemplate(VisualControlTemplate? template)
    {
        _styleTemplate = template;
        UpdateTemplate();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, ContentProperty)
            || ReferenceEquals(e.Property, ContentTemplateProperty)
            || ReferenceEquals(e.Property, ContentTemplateSelectorProperty)
            || ReferenceEquals(e.Property, ContentStringFormatProperty))
        {
            UpdateContent();
        }
        else if (ReferenceEquals(e.Property, TemplateProperty))
        {
            UpdateTemplate();
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var inner = new Size(
            Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal)),
            Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical)));
        var desired = ActiveChild?.Measure(inner) ?? Size.Empty;
        return new Size(
            desired.Width + (int)Math.Ceiling(padding.Horizontal),
            desired.Height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (ActiveChild is not { } child)
        {
            return;
        }

        var padding = Padding;
        child.Arrange(new RectangleF(
            (int)Math.Ceiling(padding.Left),
            (int)Math.Ceiling(padding.Top),
            Math.Max(0f, finalSize.Width - padding.Horizontal),
            Math.Max(0f, finalSize.Height - padding.Vertical)));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _templateTriggers?.Dispose();
            _templateTriggers = null;
            RemoveDataVisual();
            DisposeTextVisual();
            _presenters.Clear();
        }

        base.Dispose(disposing);
    }

    private VisualElement? ActiveChild => _templateRoot ?? _contentVisual;

    private void UpdateTemplate()
    {
        var effective = Template ?? _styleTemplate ?? DefaultTemplate;
        if (ReferenceEquals(effective, _appliedTemplate))
        {
            UpdateContent();
            return;
        }

        RemoveTemplate();
        _appliedTemplate = effective;
        if (effective is null)
        {
            UpdateContent();
            return;
        }

        var instance = effective.CreateContent(this);
        var root = instance.Root;
        _templateRoot = root;
        SetTemplateScope(instance.Scope);
        Add(root);

        foreach (var presenter in EnumeratePresenters(root))
        {
            _presenters.Add(presenter);
        }

        _templateTriggers = new VisualTemplateTriggerApplication(this, instance.Scope, effective.Triggers);
        UpdateContent();
        InvalidateMeasure();
    }

    private void RemoveTemplate()
    {
        _templateTriggers?.Dispose();
        _templateTriggers = null;
        SetTemplateScope(null);

        foreach (var presenter in _presenters)
        {
            presenter.Clear();
        }

        _presenters.Clear();

        if (_templateRoot is not null)
        {
            if (Children.Contains(_templateRoot))
            {
                RemoveChild(_templateRoot);
            }

            _templateRoot.Dispose();
            _templateRoot = null;
        }
    }

    private void UpdateContent()
    {
        if (_templateRoot is not null)
        {
            // 内容交给模板里的 ContentPresenter；它不能再同时挂在控件自己的子项里。
            DetachContentVisual();
            var fallback = ResolveContentVisual(applyContentTemplate: false);
            _contentVisual = fallback;

            foreach (var presenter in _presenters)
            {
                presenter.Present(Content, fallback);
            }

            return;
        }

        var visual = ResolveContentVisual(applyContentTemplate: true);
        if (ReferenceEquals(_contentVisual, visual)
            && (visual is null || Children.Contains(visual)))
        {
            return;
        }

        DetachContentVisual();
        _contentVisual = visual;
        if (visual is not null)
        {
            Add(visual);
        }

        InvalidateMeasure();
    }

    // 控件模板接管内容后，控件自己的投影必须退出子项集合；presenter 会重新添加它或改用模板。
    private void DetachContentVisual()
    {
        var visual = _contentVisual;
        if (visual is not null && Children.Contains(visual))
        {
            RemoveChild(visual);
        }

        _contentVisual = null;
    }

    private VisualElement? ResolveContentVisual(bool applyContentTemplate)
    {
        if (Content is VisualElement element)
        {
            RemoveDataVisual();
            DisposeTextVisual();
            return element;
        }

        if (applyContentTemplate)
        {
            // 选择器先于静态模板；两者都在同一层，选择器的非空结果胜出。
            var selector = ContentTemplateSelector;
            var template = selector?.SelectTemplate(Content, this) ?? ContentTemplate;
            if (template is not null)
            {
                DisposeTextVisual();
                return EnsureDataVisual(template, Content, selector);
            }
        }

        RemoveDataVisual();
        var text = ProjectContentText(Content, ContentStringFormat);
        if (!string.IsNullOrEmpty(text))
        {
            return EnsureTextVisual(text);
        }

        DisposeTextVisual();
        return null;
    }

    // 文本投影的唯一入口：有格式串时用当前区域格式化，否则退回内容的 ToString。
    internal static string? ProjectContentText(object? content, string? format)
    {
        if (content is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(format)
            ? (content as string ?? content.ToString())
            : string.Format(CultureInfo.CurrentCulture, format, content);
    }

    private VisualElement EnsureTextVisual(string text)
    {
        var visual = _textVisual;
        if (visual is not TextBlock block || block.Text != text)
        {
            DisposeTextVisual();
            visual = new TextBlock { Text = text };
            _textVisual = visual;
        }

        return visual;
    }

    private void DisposeTextVisual()
    {
        var visual = _textVisual;
        if (visual is null)
        {
            return;
        }

        visual.ParentElement?.RemoveChild(visual);
        visual.Dispose();
        _textVisual = null;
    }

    private VisualElement EnsureDataVisual(DataTemplate template, object? content, DataTemplateSelector? selector)
    {
        var existing = _dataVisual;
        if (existing is not null
            && ReferenceEquals(_dataTemplate, template)
            && ReferenceEquals(_dataSelector, selector)
            && Equals(_dataContent, content))
        {
            return existing;
        }

        RemoveDataVisual();
        var visual = template.Create();
        visual.DataContext = content;
        _dataTemplate = template;
        _dataSelector = selector;
        _dataContent = content;
        _dataVisual = visual;
        return visual;
    }

    private void RemoveDataVisual()
    {
        var visual = _dataVisual;
        if (visual is null)
        {
            return;
        }

        if (visual.ParentElement is { } parent && parent.Children.Contains(visual))
        {
            parent.RemoveChild(visual);
        }

        visual.Dispose();
        _dataVisual = null;
        _dataTemplate = null;
        _dataSelector = null;
        _dataContent = null;
    }

    private static IEnumerable<ContentPresenter> EnumeratePresenters(VisualElement root)
    {
        if (root is ContentPresenter presenter)
        {
            yield return presenter;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element)
            {
                foreach (var inner in EnumeratePresenters(element))
                {
                    yield return inner;
                }
            }
        }
    }
}

/// <summary>Shows the content of the control whose template contains this presenter.</summary>
/// <remarks>
/// The owning <see cref="ContentControl"/> pushes the raw content and its default projection in. A non-visual content is
/// shown through this presenter's own <see cref="ContentTemplateSelector"/> and <see cref="ContentTemplate"/> first,
/// then through the templated parent's, and otherwise becomes text through <see cref="ContentStringFormat"/>. A
/// <see cref="VisualElement"/> content ignores templates and selectors and is shown as-is.
/// </remarks>
public class ContentPresenter : VisualElement
{
    /// <summary>The template this presenter applies first, or <see langword="null"/> to use the templated parent's.</summary>
    public static readonly DependencyProperty ContentTemplateProperty = DependencyProperty.Register(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(ContentPresenter),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>Chooses this presenter's template, or <see langword="null"/> to fall back to the templated parent.</summary>
    public static readonly DependencyProperty ContentTemplateSelectorProperty = DependencyProperty.Register(
        nameof(ContentTemplateSelector), typeof(DataTemplateSelector), typeof(ContentPresenter),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    /// <summary>The format applied when this presenter projects the content as text, or empty to inherit.</summary>
    public static readonly DependencyProperty ContentStringFormatProperty = DependencyProperty.Register(
        nameof(ContentStringFormat), typeof(string), typeof(ContentPresenter),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = false });

    private object? _content;
    private VisualElement? _fallback;
    private bool _hasContent;
    private DataTemplate? _appliedTemplate;
    private DataTemplateSelector? _appliedSelector;
    private object? _templatedContent;
    private VisualElement? _templatedVisual;
    private VisualElement? _textVisual;
    private VisualElement? _presented;

    /// <summary>The template this presenter applies first, or <see langword="null"/> to use the templated parent's.</summary>
    /// <remarks>A <see cref="VisualElement"/> content is shown as-is and ignores this template.</remarks>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>Chooses this presenter's template, or <see langword="null"/> to fall back to the templated parent.</summary>
    /// <remarks>
    /// The selector receives the raw content and the owning <see cref="ContentControl"/>. A non-null result wins over
    /// <see cref="ContentTemplate"/> at this level; a <see cref="VisualElement"/> content bypasses both.
    /// </remarks>
    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    /// <summary>The format applied when this presenter projects the content as text, or empty to inherit.</summary>
    /// <remarks>
    /// A non-empty value overrides the owning control's format for this presenter. It is ignored when a template or
    /// selector supplies a visual.
    /// </remarks>
    public string ContentStringFormat
    {
        get => (string)GetValue(ContentStringFormatProperty)!;
        set => SetValue(ContentStringFormatProperty, value ?? string.Empty);
    }

    /// <summary>The visual currently presented, or <see langword="null"/>.</summary>
    public VisualElement? PresentedContent => _presented;

    /// <summary>The content shown by this presenter; a <see cref="VisualElement"/> is presented directly.</summary>
    /// <remarks>A controlling <see cref="ContentControl"/> pushes its content in with a default projection; this
    /// property is for a presenter used on its own.</remarks>
    public object? Content
    {
        get => _content;
        set => Present(value, value as VisualElement);
    }

    // 内容控件先把它从自己的子项摘下，再交给这里；否则同一个视觉节点会同时属于两个父集合。
    internal void Present(object? content, VisualElement? fallback)
    {
        _hasContent = true;
        _content = content;
        _fallback = fallback;
        Refresh();
    }

    // 模板卸载时调用：清空而不按“内容为 null”重建模板。
    internal void Clear()
    {
        _hasContent = false;
        _content = null;
        _fallback = null;
        DisposeTemplatedVisual();
        DisposeTextVisual();
        SetPresented(null);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // 模板上级赋值发生在内容推入之前；那时没有内容，重建只会多造一个空模板。
        if (_hasContent
            && (ReferenceEquals(e.Property, ContentTemplateProperty)
                || ReferenceEquals(e.Property, ContentTemplateSelectorProperty)
                || ReferenceEquals(e.Property, ContentStringFormatProperty)
                || ReferenceEquals(e.Property, TemplatedParentProperty)))
        {
            Refresh();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeTemplatedVisual();
            DisposeTextVisual();
        }

        base.Dispose(disposing);
    }

    private void Refresh()
    {
        if (_content is VisualElement element)
        {
            DisposeTemplatedVisual();
            DisposeTextVisual();
            SetPresented(element);
            return;
        }

        var selector = ResolveTemplateSelector();
        var template = ResolveContentTemplate();
        if (template is null)
        {
            DisposeTemplatedVisual();
            SetPresented(ResolveDefaultProjection());
            return;
        }

        DisposeTextVisual();
        SetPresented(EnsureTemplatedVisual(template, selector));
    }

    private DataTemplateSelector? ResolveTemplateSelector()
        => ContentTemplateSelector ?? (TemplatedParent as ContentControl)?.ContentTemplateSelector;

    // 层级顺序：自己的选择器 → 自己的模板 → 模板上级的选择器 → 模板上级的模板。
    private DataTemplate? ResolveContentTemplate()
    {
        var parent = TemplatedParent as ContentControl;
        return ContentTemplateSelector?.SelectTemplate(_content, parent)
            ?? ContentTemplate
            ?? parent?.ContentTemplateSelector?.SelectTemplate(_content, parent)
            ?? parent?.ContentTemplate;
    }

    // 没有模板时：自己的格式优先；未设格式则沿用控件已按上级格式投影好的回退视觉。
    private VisualElement? ResolveDefaultProjection()
    {
        if (string.IsNullOrEmpty(ContentStringFormat) && _fallback is not null)
        {
            DisposeTextVisual();
            return _fallback;
        }

        var text = ContentControl.ProjectContentText(_content, ContentStringFormat);
        if (string.IsNullOrEmpty(text))
        {
            DisposeTextVisual();
            return null;
        }

        return EnsureTextVisual(text);
    }

    private VisualElement EnsureTextVisual(string text)
    {
        var visual = _textVisual;
        if (visual is not TextBlock block || block.Text != text)
        {
            DisposeTextVisual();
            visual = new TextBlock { Text = text };
            _textVisual = visual;
        }

        return visual;
    }

    private void DisposeTextVisual()
    {
        var visual = _textVisual;
        if (visual is null)
        {
            return;
        }

        if (Children.Contains(visual))
        {
            RemoveChild(visual);
        }

        visual.Dispose();
        _textVisual = null;
    }

    private VisualElement EnsureTemplatedVisual(DataTemplate template, DataTemplateSelector? selector)
    {
        var visual = _templatedVisual;
        if (visual is null
            || !ReferenceEquals(_appliedTemplate, template)
            || !ReferenceEquals(_appliedSelector, selector)
            || !Equals(_templatedContent, _content))
        {
            DisposeTemplatedVisual();
            visual = template.Create();
            visual.DataContext = _content;
            _templatedVisual = visual;
            _appliedTemplate = template;
            _appliedSelector = selector;
            _templatedContent = _content;
        }

        return visual;
    }

    private void DisposeTemplatedVisual()
    {
        var visual = _templatedVisual;
        if (visual is null)
        {
            return;
        }

        if (Children.Contains(visual))
        {
            RemoveChild(visual);
        }

        visual.Dispose();
        _templatedVisual = null;
        _appliedTemplate = null;
        _appliedSelector = null;
        _templatedContent = null;
    }

    private void SetPresented(VisualElement? content)
    {
        if (ReferenceEquals(_presented, content))
        {
            return;
        }

        var previous = _presented;
        if (previous is not null && Children.Contains(previous))
        {
            RemoveChild(previous);
        }

        _presented = content;
        if (content is not null)
        {
            Add(content);
        }

        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var inner = new Size(
            Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal)),
            Math.Max(0, availableSize.Height - (int)Math.Ceiling(padding.Vertical)));
        var desired = _presented?.Measure(inner) ?? Size.Empty;
        return new Size(
            desired.Width + (int)Math.Ceiling(padding.Horizontal),
            desired.Height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (_presented is null)
        {
            return;
        }

        var padding = Padding;
        _presented.Arrange(new RectangleF(
            (int)Math.Ceiling(padding.Left),
            (int)Math.Ceiling(padding.Top),
            Math.Max(0f, finalSize.Width - padding.Horizontal),
            Math.Max(0f, finalSize.Height - padding.Vertical)));
    }
}
