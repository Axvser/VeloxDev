namespace Demo.Views.Sections;

/// <summary>The style, template and trigger examples.</summary>
public sealed partial class StylesSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    /// <summary>Creates the section.</summary>
    public StylesSection()
    {
        InitializeComponent();
    }

    private void OnClearClick(object? sender, System.EventArgs e)
    {
        if (DataContext is GalleryModel model)
        {
            model.OptionalName = string.Empty;
        }
    }

    private void OnRestoreClick(object? sender, System.EventArgs e)
    {
        if (DataContext is GalleryModel model)
        {
            model.OptionalName = "Ada Lovelace";
        }
    }
}
