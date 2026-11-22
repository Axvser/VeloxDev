using System.Drawing;
using VeloxDev.WinForms.Xaml;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A visual control that shows one piece of content, either directly or through a <see cref="VisualControlTemplate"/>.</summary>
/// <remarks>
/// <para>
/// Without a template the content is the only child: a <see cref="VisualElement"/> is shown as-is, a string becomes a
/// <see cref="TextBlock"/>. With a template the template's root is the only child, and every
/// <see cref="ContentPresenter"/> inside it shows the content instead.
/// </para>
/// <para>
/// The template comes from <see cref="Template"/> or, when that is <see langword="null"/>, from the applied
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

    /// <summary>A template applied directly to this control, or <see langword="null"/> to use the style's template.</summary>
    public static readonly DependencyProperty TemplateProperty = DependencyProperty.Register(
        nameof(Template), typeof(VisualControlTemplate), typeof(ContentControl),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    private VisualControlTemplate? _styleTemplate;
    private VisualControlTemplate? _appliedTemplate;
    private VisualElement? _templateRoot;
    private VisualTemplateTriggerApplication? _templateTriggers;
    private VisualElement? _contentVisual;
    private VisualElement? _textVisual;
    private readonly List<ContentPresenter> _presenters = [];

    /// <summary>The object shown by this control or by its content presenters.</summary>
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>A template applied directly to this control, or <see langword="null"/> to use the style's template.</summary>
    public VisualControlTemplate? Template
    {
        get => (VisualControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>The visual currently shown as content, or <see langword="null"/>.</summary>
    public VisualElement? ContentVisual => _contentVisual;

    /// <summary>The template root currently applied, or <see langword="null"/>.</summary>
    public VisualElement? TemplateRoot => _templateRoot;

    internal override void ApplyStyleTemplate(VisualControlTemplate? template)
    {
        _styleTemplate = template;
        UpdateTemplate();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, ContentProperty))
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
            _textVisual?.Dispose();
            _textVisual = null;
            _presenters.Clear();
        }

        base.Dispose(disposing);
    }

    private VisualElement? ActiveChild => _templateRoot ?? _contentVisual;

    private void UpdateTemplate()
    {
        var effective = Template ?? _styleTemplate;
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
            presenter.Present(null);
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
        var visual = ResolveContentVisual();

        if (_templateRoot is not null)
        {
            // 内容交给模板里的 ContentPresenter；它不能再同时挂在控件自己的子项里。
            if (visual is not null && Children.Contains(visual))
            {
                RemoveChild(visual);
            }

            foreach (var presenter in _presenters)
            {
                presenter.Present(visual);
            }

            return;
        }

        if (ReferenceEquals(_contentVisual, visual))
        {
            return;
        }

        if (_contentVisual is not null && Children.Contains(_contentVisual))
        {
            RemoveChild(_contentVisual);
        }

        _contentVisual = visual;
        if (visual is not null)
        {
            Add(visual);
        }

        InvalidateMeasure();
    }

    private VisualElement? ResolveContentVisual()
    {
        switch (Content)
        {
            case VisualElement element:
                return element;
            case string text when !string.IsNullOrEmpty(text):
                if (_textVisual is not TextBlock block || block.Text != text)
                {
                    _textVisual?.Dispose();
                    _textVisual = new TextBlock { Text = text };
                }

                return _textVisual;
            default:
                return null;
        }
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
/// <remarks>The owning <see cref="ContentControl"/> pushes content in; the presenter never reads the templated parent itself.</remarks>
public class ContentPresenter : VisualElement
{
    private VisualElement? _presented;

    /// <summary>The visual currently presented, or <see langword="null"/>.</summary>
    public VisualElement? PresentedContent => _presented;

    // 内容控件先把它从自己的子项摘下，再交给这里；否则同一个视觉节点会同时属于两个父集合。
    internal void Present(VisualElement? content)
    {
        if (ReferenceEquals(_presented, content))
        {
            return;
        }

        if (_presented is not null && Children.Contains(_presented))
        {
            RemoveChild(_presented);
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
