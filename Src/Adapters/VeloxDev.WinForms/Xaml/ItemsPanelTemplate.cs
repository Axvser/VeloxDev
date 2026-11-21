namespace VeloxDev.WinForms.Xaml;

/// <summary>Creates the panel an <see cref="VeloxDev.WinForms.Xaml.Controls.ItemsControl"/> hosts its items in.</summary>
/// <remarks>
/// Parallel to <see cref="DataTemplate"/>: the generator or the host supplies the factory, and the consuming
/// control owns the panel it creates. A panel template is used only while
/// <see cref="VeloxDev.WinForms.Xaml.Controls.ItemsControl.ItemsPanel"/> is not set.
/// </remarks>
public sealed class ItemsPanelTemplate
{
    private readonly Func<VeloxDev.WinForms.Xaml.Controls.Control> _factory;

    /// <summary>Creates a template over <paramref name="factory"/>.</summary>
    /// <param name="factory">Creates one panel instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    public ItemsPanelTemplate(Func<VeloxDev.WinForms.Xaml.Controls.Control> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Creates one panel instance.</summary>
    public VeloxDev.WinForms.Xaml.Controls.Control CreatePanel() => _factory();
}
