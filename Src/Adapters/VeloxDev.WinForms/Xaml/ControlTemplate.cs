namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// A content template for a <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>: one factory that creates the visual content inserted under
/// the target control when a <see cref="Style.Template"/> is applied.
/// </summary>
/// <remarks>
/// <para>
/// The created content becomes the template's visual tree. Its root exposes a <see cref="NameScope"/> so
/// <see cref="VeloxDev.WinForms.Xaml.Controls.Control.GetTemplateChild{T}(string)"/> can resolve named parts, and every control in
/// that tree reports the applying control as its <see cref="VeloxDev.WinForms.Xaml.Controls.Control.TemplatedParent"/>.
/// </para>
/// <para>
/// This is still a content template, not WPF's full control-template pipeline: parts are ordinary children, and
/// template triggers apply setters to the templated control or to a named part.
/// </para>
/// </remarks>
public sealed class ControlTemplate
{
    private readonly Func<System.Windows.Forms.Control> _factory;

    /// <summary>Creates a template over <paramref name="factory"/>.</summary>
    /// <param name="factory">Creates one instance of the template's content.</param>
    public ControlTemplate(Func<System.Windows.Forms.Control> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Triggers.Changed += OnTriggersChanged;
    }

    /// <summary>Raised when the trigger collection changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The control type the template expects, or <see langword="null"/> to accept any control.</summary>
    public Type? TargetType { get; set; }

    /// <summary>The triggers evaluated against the templated control while the template is applied.</summary>
    public TriggerCollection Triggers { get; } = [];

    /// <summary>Creates one instance of the template's content.</summary>
    public System.Windows.Forms.Control CreateContent() => _factory();

    /// <summary>Creates one instance of the template's content and points it at <paramref name="templatedParent"/>.</summary>
    /// <param name="templatedParent">The control whose style applied this template.</param>
    public System.Windows.Forms.Control CreateContent(VeloxDev.WinForms.Xaml.Controls.Control templatedParent)
    {
        var content = _factory();
        if (content is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.SetTemplatedParentRecursive(templatedParent);
        }

        return content;
    }

    private void OnTriggersChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
