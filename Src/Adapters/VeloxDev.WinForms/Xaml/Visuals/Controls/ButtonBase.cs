using System.Drawing;
using System.Windows.Input;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>
/// The shared command behavior of the self-drawn button controls.
/// </summary>
/// <remarks>
/// <para>
/// A left pointer release and <c>Space</c>/<c>Enter</c> activate a derived control; the derived control then calls
/// <see cref="ExecuteCommand"/> before changing its own state. While a command is assigned,
/// <see cref="ICommand.CanExecute"/> drives <see cref="VisualElement.IsEnabled"/>: the control is disabled when the
/// command refuses and re-enabled when it allows. An <see cref="VisualElement.IsEnabled"/>=false set by the author is
/// preserved until the command refuses at least once; from then on the command owns the value.
/// </para>
/// <para>
/// Commands are executed only when they can execute; a command that refuses while activated is skipped and the
/// control's own click or checked behavior still runs.
/// </para>
/// <para>
/// <see cref="IsPointerOver"/> and <see cref="IsPressed"/> are dependency properties, so a control template's
/// triggers can react to the interactive state the same way they react to <c>IsEnabled</c>.
/// </para>
/// </remarks>
public abstract class ButtonBase : TemplatedControl
{
    /// <summary>The command executed when the control is activated.</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(ButtonBase),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>The value passed to <see cref="Command"/> when it is evaluated and executed.</summary>
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter),
        typeof(object),
        typeof(ButtonBase),
        new PropertyMetadata(null) { AffectsRender = false });

    /// <summary>Whether the pointer is currently over the control.</summary>
    public static readonly DependencyProperty IsPointerOverProperty = DependencyProperty.Register(
        nameof(IsPointerOver),
        typeof(bool),
        typeof(ButtonBase),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether the left pointer button is currently held down on the control.</summary>
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed),
        typeof(bool),
        typeof(ButtonBase),
        new PropertyMetadata(false) { AffectsRender = true });

    private ICommand? _subscribedCommand;
    private bool _commandDisabledByCommand;

    /// <summary>The command executed when the control is activated, or <see langword="null"/>.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>The value passed to <see cref="Command"/> when it is evaluated and executed.</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Whether the pointer is currently over the control.</summary>
    /// <remarks>This dependency property intentionally shadows <see cref="VisualElement.IsPointerOver"/> so a
    /// control template trigger can observe hover at the template layer.</remarks>
    public new bool IsPointerOver => (bool)GetValue(IsPointerOverProperty)!;

    /// <summary>Whether the left pointer button is currently held down on the control.</summary>
    public bool IsPressed => (bool)GetValue(IsPressedProperty)!;

    /// <summary>Writes <see cref="IsPointerOver"/>; used by the pointer handlers and derived controls.</summary>
    /// <param name="value">The new hover state.</param>
    protected void SetIsPointerOver(bool value) => SetValue(IsPointerOverProperty, value);

    /// <summary>Writes <see cref="IsPressed"/>; used by the pointer handlers and derived controls.</summary>
    /// <param name="value">The new press state.</param>
    protected void SetIsPressed(bool value) => SetValue(IsPressedProperty, value);

    /// <summary>Executes <see cref="Command"/> with <see cref="CommandParameter"/> when it can execute.</summary>
    /// <returns><see langword="true"/> when the command ran.</returns>
    protected bool ExecuteCommand()
    {
        if (Command is not { } command)
        {
            return false;
        }

        var parameter = CommandParameter;
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (ReferenceEquals(e.Property, CommandProperty))
        {
            if (_subscribedCommand is { } previous)
            {
                previous.CanExecuteChanged -= OnCommandCanExecuteChanged;
            }

            _subscribedCommand = Command;
            if (_subscribedCommand is { } current)
            {
                current.CanExecuteChanged += OnCommandCanExecuteChanged;
            }

            UpdateCommandEnabled();
        }
        else if (ReferenceEquals(e.Property, CommandParameterProperty))
        {
            UpdateCommandEnabled();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _subscribedCommand is { } command)
        {
            command.CanExecuteChanged -= OnCommandCanExecuteChanged;
            _subscribedCommand = null;
        }

        base.Dispose(disposing);
    }

    private void OnCommandCanExecuteChanged(object? sender, EventArgs e) => UpdateCommandEnabled();

    // 只有命令自己关掉的状态由命令恢复；作者显式设置的 IsEnabled=false 不被覆盖。
    private void UpdateCommandEnabled()
    {
        var command = Command;
        if (command is not null && !command.CanExecute(CommandParameter))
        {
            _commandDisabledByCommand = true;
            IsEnabled = false;
            return;
        }

        if (_commandDisabledByCommand)
        {
            _commandDisabledByCommand = false;
            IsEnabled = true;
        }
    }
}

/// <summary>
/// A visual element whose appearance can be replaced by a <see cref="VisualControlTemplate"/> while a template is
/// applied.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Template"/> is the direct assignment; a <see cref="VisualStyle.Template"/> is the fallback when it is
/// <see langword="null"/>. The created root becomes a child of the control, every part registers in a fresh
/// <see cref="VisualNameScope"/> (see <see cref="VisualElement.GetTemplateChild{T}(string)"/>), and
/// <see cref="VisualControlTemplate.Triggers"/> run against the control.
/// </para>
/// <para>
/// While a template is applied <see cref="VisualElement"/>'s self-drawn path stays inactive: derived controls test
/// <see cref="IsTemplated"/> before painting, measuring or arranging their own chrome, and measure and arrange the
/// template root instead. Clearing <see cref="Template"/> removes the root, its triggers and its name scope, and the
/// self-drawn path becomes visible again.
/// </para>
/// </remarks>
public abstract class TemplatedControl : Panel
{
    /// <summary>A template applied directly to this control, or <see langword="null"/> to use the style's template.</summary>
    public static readonly DependencyProperty TemplateProperty = DependencyProperty.Register(
        nameof(Template), typeof(VisualControlTemplate), typeof(TemplatedControl),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = false });

    private VisualControlTemplate? _styleTemplate;
    private VisualControlTemplate? _appliedTemplate;
    private VisualElement? _templateRoot;
    private VisualTemplateTriggerApplication? _templateTriggers;
    private bool _templateDisposed;

    /// <summary>Creates the control with no template.</summary>
    protected TemplatedControl()
    {
        IsHitTestVisible = true;
    }

    /// <summary>A control template applied directly to this control, or <see langword="null"/> to use the style's template.</summary>
    public VisualControlTemplate? Template
    {
        get => (VisualControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>The template root currently applied, or <see langword="null"/> while the control draws itself.</summary>
    public VisualElement? TemplateRoot => _templateRoot;

    /// <summary>Whether a control template currently replaces the self-drawn path.</summary>
    protected bool IsTemplated => _templateRoot is not null;

    /// <inheritdoc />
    internal override void ApplyStyleTemplate(VisualControlTemplate? template)
    {
        _styleTemplate = template;
        UpdateTemplate();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, TemplateProperty))
        {
            UpdateTemplate();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 基础 Dispose 会再走一次样式卸载；标记之后 UpdateTemplate 不再重建模板。
            _templateDisposed = true;
            OnTemplateChanging();
            RemoveTemplate();
        }

        base.Dispose(disposing);
    }

    /// <summary>Measures the applied template root inside the control's content area.</summary>
    /// <param name="availableSize">The room the parent can offer.</param>
    /// <returns>The root's desired size, or an empty size without a template.</returns>
    protected Size MeasureTemplate(Size availableSize)
        => _templateRoot?.Measure(availableSize) ?? Size.Empty;

    /// <summary>Arranges the applied template root over the control's whole arranged size.</summary>
    /// <param name="finalSize">The arranged content size.</param>
    protected void ArrangeTemplate(Size finalSize)
        => _templateRoot?.Arrange(new RectangleF(0f, 0f, finalSize.Width, finalSize.Height));

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => IsTemplated ? MeasureTemplate(availableSize) : base.MeasureOverride(availableSize);

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        // 模板根负责整块外观；控件的背景/边框不再自动铺一层。
        if (!IsTemplated)
        {
            base.RenderContent(graphics);
        }
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        if (IsTemplated)
        {
            ArrangeTemplate(finalSize);
            return;
        }

        base.ArrangeOverride(finalSize);
    }

    /// <summary>Called before the current template root is removed, so a control can detach visuals it put inside it.</summary>
    protected virtual void OnTemplateChanging()
    {
    }

    /// <summary>Called after the effective template changed, so a control can re-parent content into the new parts.</summary>
    protected virtual void OnTemplateChanged()
    {
    }

    private void UpdateTemplate()
    {
        if (_templateDisposed)
        {
            return;
        }

        var effective = Template ?? _styleTemplate;
        if (ReferenceEquals(effective, _appliedTemplate))
        {
            return;
        }

        OnTemplateChanging();
        RemoveTemplate();
        _appliedTemplate = effective;
        if (effective is not null)
        {
            var instance = effective.CreateContent(this);
            _templateRoot = instance.Root;
            SetTemplateScope(instance.Scope);
            Add(instance.Root);
            _templateTriggers = new VisualTemplateTriggerApplication(this, instance.Scope, effective.Triggers);
        }

        OnTemplateChanged();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RemoveTemplate()
    {
        _templateTriggers?.Dispose();
        _templateTriggers = null;
        _appliedTemplate = null;
        SetTemplateScope(null);

        if (_templateRoot is null)
        {
            return;
        }

        if (Children.Contains(_templateRoot))
        {
            RemoveChild(_templateRoot);
        }

        _templateRoot.Dispose();
        _templateRoot = null;
    }
}

// 前景刷按颜色比较：触发器写成 "#RRGGBB" 或 Color 时，存储值仍是刷子，但等价判断按代表性颜色进行。
internal sealed class ColorBrush : SolidColorBrush
{
    internal ColorBrush(Color color)
        : base(color)
    {
    }

    public override bool Equals(object? obj)
    {
        switch (obj)
        {
            case ColorBrush other:
                return Color == other.Color;
            case XamlBrush brush:
                return Color == brush.ApproximateColor;
            case Color color:
                return Color == color;
            case string text:
                try
                {
                    return Color == XamlRuntime.ParseColor(text);
                }
                catch (FormatException)
                {
                    return false;
                }
            default:
                return false;
        }
    }

    public override int GetHashCode() => Color.GetHashCode();
}

// 把写入的颜色/刷子统一成 ColorBrush，触发器对任何来源的值都走同一套颜色等价判断。
internal static class ColorBrushCoercion
{
    internal static object? Coerce(object? value)
    {
        return value switch
        {
            null => null,
            ColorBrush => value,
            SolidColorBrush solid => new ColorBrush(solid.Color),
            Color color => new ColorBrush(color),
            _ => value,
        };
    }
}
