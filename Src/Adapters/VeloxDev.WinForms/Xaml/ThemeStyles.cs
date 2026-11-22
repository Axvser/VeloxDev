using System.Drawing;
using System.Reflection;
using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Supplies the implicit style a control sees when neither the author nor an ancestor resource dictionary
/// supplied one. The values come from <see cref="Theme.Current"/>, so switching theme changes the defaults.
/// </summary>
/// <remarks>
/// <para>
/// The style is built for the control's exact runtime type. Containers deliberately keep a transparent
/// background; only interactive controls receive a surface, border and foreground. An explicit value or an
/// explicit/implicit style from the control tree always wins because this style is the last fallback.
/// </para>
/// <para>
/// Resting tokens are applied as style setters. State tokens (hover, pressed, focus, selection and disabled)
/// are read by the controls themselves from <see cref="Theme.Current"/> while they paint, so the palette is
/// their only source.
/// </para>
/// </remarks>
public static class ThemeStyles
{
    private static readonly Dictionary<Type, Style?> Cache = [];
    private static readonly HashSet<string> SurfaceTypes = new(StringComparer.Ordinal)
    {
        "Button", "RepeatButton", "ToggleButton",
        "TextBox", "PasswordBox", "RichTextBox", "ListBox", "ComboBox", "Calendar", "DatePicker",
        "TreeView", "ListView", "DataGrid", "TabControl",
    };

    /// <summary>Returns the cached implicit style for <paramref name="type"/>, or <see langword="null"/>.</summary>
    /// <param name="type">The control's exact runtime type.</param>
    public static Style? Find(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (Cache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var style = Build(type);
        Cache[type] = style;
        return style;
    }

    /// <summary>Clears the cached styles so the next lookup reads the current palette.</summary>
    public static void Invalidate() => Cache.Clear();

    /// <summary>Re-applies implicit styles over a control tree after a theme change.</summary>
    /// <param name="root">The view or window root.</param>
    public static void ApplyTo(System.Windows.Forms.Control root)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));

        if (root is XamlControl xaml)
        {
            xaml.RefreshImplicitStyle();
        }

        foreach (System.Windows.Forms.Control child in root.Controls)
        {
            ApplyTo(child);
        }
    }

    private static Style? Build(Type type)
    {
        if (!typeof(XamlControl).IsAssignableFrom(type) || type.IsAbstract)
        {
            return null;
        }

        // 只给库内控件兜底；用户派生控件的 Style 仍应保持 null，除非他们自己放了隐式样式。
        if (type.Namespace is not "VeloxDev.WinForms.Xaml.Controls"
            and not "VeloxDev.WinForms.Xaml.Controls.Shapes")
        {
            return null;
        }

        var palette = Theme.Current;
        var style = new Style { TargetType = type };
        var surface = SurfaceTypes.Contains(type.Name);
        var track = type.Name is "ProgressBar" or "Slider";

        Add(style, type, "Background", track ? palette.SurfaceAlt : palette.ControlBackground, surface || track);
        Add(style, type, "Foreground", track ? palette.Accent : palette.Foreground, true);
        Add(style, type, "BorderBrush", palette.ControlBorder, surface);
        Add(style, type, "BorderThickness", new XamlThickness(1f), surface);
        Add(style, type, "CornerRadius", palette.CornerRadius, surface);

        switch (type.Name)
        {
            case "Slider":
                Add(style, type, "ThumbBrush", palette.Accent);
                break;

            case "ScrollBar":
                // VS Code 的滚动条滑杆是中性灰，强调色只留给按钮与选中态。
                Add(style, type, "TrackBrush", palette.SurfaceAlt);
                Add(style, type, "ThumbBrush", palette.MutedForeground);
                Add(style, type, "ThumbHoverBrush", palette.Foreground);
                Add(style, type, "ThumbBorderBrush", palette.Surface);
                Add(style, type, "ThumbCornerRadius", palette.CornerRadius);
                break;
        }

        return style.Setters.Count == 0 ? null : style;
    }

    private static void Add(Style style, Type type, string propertyName, object? value, bool add = true)
    {
        if (!add || value is null)
        {
            return;
        }

        var property = FindDependencyProperty(type, propertyName);
        if (property is null)
        {
            return;
        }

        if (property.PropertyType == typeof(Brush) && value is Color color)
        {
            value = new SolidColorBrush(color);
        }

        if (!property.PropertyType.IsInstanceOfType(value))
        {
            return;
        }

        // The loop keeps a DP from being added twice when a derived type redeclares one.
        foreach (var existing in style.Setters)
        {
            if (ReferenceEquals(existing.Property, property))
            {
                return;
            }
        }

        style.Setters.Add(new Setter(property, value));
    }

    private static DependencyProperty? FindDependencyProperty(Type type, string propertyName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                propertyName + "Property",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field?.GetValue(null) is DependencyProperty property)
            {
                return property;
            }
        }

        return null;
    }
}
