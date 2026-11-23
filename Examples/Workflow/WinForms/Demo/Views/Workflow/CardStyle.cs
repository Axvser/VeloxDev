// VeloxDev customization: the typography and converters the visual typed node cards share.
//
// The visual backend's TextBlock / Button / TextBox carry a System.Drawing.Font rather than a FontSize property,
// and its markup has no font literal. The card chrome therefore keeps colours in XAML and sets the font here, from
// the same metric constants the GDI card theme already owns.
using System.Drawing;
using Easel;
using Easel.Visuals;
using VisualButton = Easel.Visuals.Controls.Button;
using VisualItemsControl = Easel.Visuals.Controls.ItemsControl;
using VisualRichTextBox = Easel.Visuals.Controls.RichTextBox;
using VisualTextBox = Easel.Visuals.Controls.TextBox;
using VisualTextBlock = Easel.Visuals.Controls.TextBlock;
using XamlVisibility = Easel.Controls.Visibility;

namespace Demo.Views.Workflow;

/// <summary>Applies the shared card typography to the visual controls of a typed node view.</summary>
internal static class CardStyle
{
    /// <summary>Applies the title face: the card's largest, heaviest line.</summary>
    public static void Title(VisualTextBlock block) => block.Font = CardTheme.Font(CardTheme.TitleSize, FontStyle.Bold);

    /// <summary>Applies the small caps label face.</summary>
    public static void Label(VisualTextBlock block) => block.Font = CardTheme.Font(CardTheme.LabelSize, FontStyle.Bold);

    /// <summary>Applies the body value face.</summary>
    public static void Value(VisualTextBlock block) => block.Font = CardTheme.Font(CardTheme.ValueSize);

    /// <summary>Applies the capsule / badge face.</summary>
    public static void Capsule(VisualTextBlock block) => block.Font = CardTheme.Font(CardTheme.CapsuleSize, FontStyle.Bold);

    /// <summary>Applies the ghost button face.</summary>
    public static void GhostButton(VisualButton button) => button.Font = CardTheme.Font(CardTheme.ButtonSize);

    /// <summary>Applies the field face to a single-line editor.</summary>
    public static void Field(VisualRichTextBox box) => box.Font = CardTheme.Font(CardTheme.ValueSize);

    /// <summary>Applies the field face to a single-line editor.</summary>
    public static void Field(VisualTextBox box) => box.Font = CardTheme.Font(CardTheme.ValueSize);

    /// <summary>Applies the monospace editor face.</summary>
    public static void Mono(VisualRichTextBox box) => box.Font = CardTheme.Mono(12f);

    /// <summary>Applies the monospace editor face.</summary>
    public static void Mono(VisualTextBox box) => box.Font = CardTheme.Mono(12f);

    /// <summary>Applies the port-name face to every TextBlock an items control realizes, now and after each rebuild.</summary>
    public static void PortNames(VisualItemsControl control)
    {
        var font = CardTheme.Font(CardTheme.PortNameSize, FontStyle.Bold);

        void Apply()
        {
            foreach (var item in control.Items)
            {
                ApplyFont(item, font);
            }
        }

        control.ItemsRebuilt += (_, _) => Apply();
        Apply();
    }

    private static void ApplyFont(VisualElement element, Font font)
    {
        if (element is VisualTextBlock block)
        {
            block.Font = font;
        }

        foreach (var child in element.Children)
        {
            if (child is VisualElement nested)
            {
                ApplyFont(nested, font);
            }
        }
    }
}

/// <summary>Maps a boolean to <see cref="XamlVisibility"/> and back.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType)
        => value is true ? XamlVisibility.Visible : XamlVisibility.Collapsed;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type sourceType)
        => value is XamlVisibility.Visible;
}
