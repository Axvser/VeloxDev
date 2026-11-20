namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// A content template for a <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>: one factory that creates the visual content inserted under
/// the target control when a <see cref="Style.Template"/> is applied.
/// </summary>
/// <remarks>
/// This is a content-template subset only. There is no templated parent, no named parts and no template triggers;
/// the created content is a normal control tree that can use its own dependency properties and triggers.
/// </remarks>
public sealed class ControlTemplate
{
    private readonly Func<System.Windows.Forms.Control> _factory;

    /// <summary>Creates a template over <paramref name="factory"/>.</summary>
    /// <param name="factory">Creates one instance of the template's content.</param>
    public ControlTemplate(Func<System.Windows.Forms.Control> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Creates one instance of the template's content.</summary>
    public System.Windows.Forms.Control CreateContent() => _factory();
}
