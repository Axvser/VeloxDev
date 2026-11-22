using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace Demo;

/// <summary>Builds an editor row for each editable dependency property of the selected visual element.</summary>
internal sealed class GalleryVisualInspector
{
    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal)
    {
        "DataContext", "Style", "Template", "ItemsSource", "ItemFactory", "Content", "Child", "Font", "Name",
    };

    private readonly VC.StackPanel _stack = new() { Spacing = 6 };

    /// <summary>The visual root of the inspector.</summary>
    public VisualElement Root => _stack;

    /// <summary>Rebuilds the rows for <paramref name="target"/>.</summary>
    /// <param name="target">The element to inspect, or <see langword="null"/> to show the empty state.</param>
    public void SetTarget(VisualElement? target)
    {
        foreach (var child in _stack.Children.ToArray())
        {
            if (child is VisualElement element)
            {
                element.Dispose();
            }
        }

        _stack.Children.Clear();

        if (target is null)
        {
            _stack.Add(Caption("Select a card to edit its dependency properties."));
            return;
        }

        _stack.Add(Caption(target.GetType().Name));
        foreach (var property in EnumerateProperties(target.GetType()))
        {
            if (Hidden.Contains(property.Name))
            {
                continue;
            }

            var value = ((IWinformsObject)target).GetValue(property);
            if (!IsEditable(property, value))
            {
                continue;
            }

            _stack.Add(CreateRow(target, property, value));
        }
    }

    private static IEnumerable<DependencyProperty> EnumerateProperties(Type targetType)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var type = targetType; type is not null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(DependencyProperty) || field.GetValue(null) is not DependencyProperty property)
                {
                    continue;
                }

                if (seen.Add(property.Name))
                {
                    yield return property;
                }
            }
        }
    }

    private static bool IsEditable(DependencyProperty property, object? value)
    {
        var type = property.PropertyType;
        if (type == typeof(bool) || type == typeof(string))
        {
            return true;
        }

        if (type.IsEnum)
        {
            return true;
        }

        if (type == typeof(int) || type == typeof(double) || type == typeof(float))
        {
            return true;
        }

        if (type == typeof(Color))
        {
            return true;
        }

        _ = value;
        return false;
    }

    private static VisualElement CreateRow(VisualElement target, DependencyProperty property, object? value)
    {
        var label = new VC.TextBlock
        {
            Text = property.Name,
            Foreground = Theme.Current.MutedForeground,
            Width = 136,
            VerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment.Center,
        };

        VisualElement editor = property.PropertyType == typeof(bool)
            ? CreateBooleanEditor(target, property, value)
            : property.PropertyType.IsEnum
                ? CreateEnumEditor(target, property, value)
                : CreateTextEditor(target, property, value);

        var row = new VC.StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Add(label);
        row.Add(editor);
        return row;
    }

    private static VisualElement CreateBooleanEditor(VisualElement target, DependencyProperty property, object? value)
    {
        var check = new VC.CheckBox { IsChecked = value is true };
        check.PropertyChanged += (_, e) =>
        {
            if (e.Property == VC.CheckBox.IsCheckedProperty)
            {
                target.SetValue(property, check.IsChecked == true);
            }
        };
        return check;
    }

    private static VisualElement CreateEnumEditor(VisualElement target, DependencyProperty property, object? value)
    {
        var names = Enum.GetNames(property.PropertyType);
        var combo = new VC.ComboBox { Width = 150, SelectedIndex = Array.IndexOf(names, value?.ToString()) };
        foreach (var name in names)
        {
            combo.Items.Add(name);
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string text && Enum.TryParse(property.PropertyType, text, out var parsed))
            {
                target.SetValue(property, parsed);
            }
        };
        return combo;
    }

    private static VisualElement CreateTextEditor(VisualElement target, DependencyProperty property, object? value)
    {
        var text = property.PropertyType == typeof(Color)
            ? value is Color color ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}" : string.Empty
            : FormatValue(property.PropertyType, value);
        var editor = new VC.TextBox { Width = 150, Text = text };
        editor.PropertyChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Property, VC.TextBox.TextProperty))
            {
                return;
            }

            try
            {
                var parsed = ParseValue(property.PropertyType, editor.Text);
                if (parsed is not null)
                {
                    target.SetValue(property, parsed);
                }
            }
            catch (FormatException)
            {
                // 输入中途的非法文本不写回；等用户改到可解析的值再提交。
            }
        };
        return editor;
    }

    private static string FormatValue(Type type, object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (type == typeof(double) && value is double number && double.IsNaN(number))
        {
            return "Auto";
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }

    private static object? ParseValue(Type type, string text)
    {
        if (type == typeof(string))
        {
            return text;
        }

        if (type == typeof(Color))
        {
            return XamlRuntime.ParseColor(text);
        }

        var trimmed = text.Trim();
        if (type == typeof(double))
        {
            return trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? double.NaN : double.Parse(trimmed, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type == typeof(float))
        {
            return float.Parse(trimmed, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type == typeof(int))
        {
            return int.Parse(trimmed, System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static VC.TextBlock Caption(string text)
        => new()
        {
            Text = text,
            Foreground = Theme.Current.MutedForeground,
            Wrap = true,
        };
}
