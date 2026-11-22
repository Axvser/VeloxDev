using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A navigable page: a <see cref="ContentControl"/> with a title and a theme surface.
/// </summary>
/// <remarks>
/// <para>
/// A page is the unit a <see cref="Frame"/> keeps in its journal. <see cref="Title"/> is metadata for the navigation
/// hosts; the page does not paint a header.
/// </para>
/// <para>
/// With no explicit background the page paints the active theme's <see cref="ThemePalette.Surface"/> colour, so a
/// page separates itself from the window background without an author setting a colour.
/// </para>
/// </remarks>
public class Page : ContentControl
{
    /// <summary>The page title shown by navigation hosts.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(Page),
        new PropertyMetadata(string.Empty) { AffectsRender = false });

    /// <summary>Creates the page and follows theme changes.</summary>
    public Page()
    {
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Gets or sets the page title shown by navigation hosts.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => (string?)GetValue(TitleProperty) ?? string.Empty;
        set => SetValue(TitleProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!HasXamlValue(BackgroundProperty))
        {
            using var brush = new SolidBrush(Theme.Current.Surface);
            e.Graphics.FillRectangle(brush, ClientRectangle);
            return;
        }

        base.OnPaintBackground(e);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            Invalidate();
        }
    }
}
