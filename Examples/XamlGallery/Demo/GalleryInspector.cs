using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Demo.Views;
using VeloxDev.WinForms.Xaml;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using XamlBorder = VeloxDev.WinForms.Xaml.Controls.Border;
using XamlButton = VeloxDev.WinForms.Xaml.Controls.Button;
using XamlCheckBox = VeloxDev.WinForms.Xaml.Controls.CheckBox;
using XamlColumnDefinition = VeloxDev.WinForms.Xaml.Controls.ColumnDefinition;
using XamlComboBox = VeloxDev.WinForms.Xaml.Controls.ComboBox;
using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;
using XamlGrid = VeloxDev.WinForms.Xaml.Controls.Grid;
using XamlGridLength = VeloxDev.WinForms.Xaml.Controls.GridLength;
using XamlScrollableControl = VeloxDev.WinForms.Xaml.Controls.ScrollableControl;
using XamlStackPanel = VeloxDev.WinForms.Xaml.Controls.StackPanel;
using XamlTextBlock = VeloxDev.WinForms.Xaml.Controls.TextBlock;
using XamlTextBox = VeloxDev.WinForms.Xaml.Controls.TextBox;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace Demo;

internal enum GalleryEditorKind
{
    Text,
    Number,
    Boolean,
    Enum,
    Color,
    Brush,
    Font,
    ReadOnly,
}

/// <summary>One property row the inspector can read, edit and reset.</summary>
internal sealed class GalleryPropertyDescriptor
{
    public GalleryPropertyDescriptor(
        string name,
        string category,
        Type valueType,
        GalleryEditorKind kind,
        DependencyProperty? dependency = null,
        PropertyInfo? clr = null,
        Func<System.Windows.Forms.Control, object?>? attachedGet = null,
        Action<System.Windows.Forms.Control, object?>? attachedSet = null)
    {
        Name = name;
        Category = category;
        ValueType = valueType;
        Kind = kind;
        Dependency = dependency;
        Clr = clr;
        AttachedGet = attachedGet;
        AttachedSet = attachedSet;
    }

    public string Name { get; }

    public string Category { get; }

    public Type ValueType { get; }

    public GalleryEditorKind Kind { get; }

    public DependencyProperty? Dependency { get; }

    public PropertyInfo? Clr { get; }

    public Func<System.Windows.Forms.Control, object?>? AttachedGet { get; }

    public Action<System.Windows.Forms.Control, object?>? AttachedSet { get; }

    public bool IsAttached => AttachedGet is not null;

    public object? Get(System.Windows.Forms.Control target)
    {
        // Prefer the CLR wrapper: Font/Background/Foreground compute an effective value that the DP store
        // does not carry when the author set nothing locally.
        if (Clr is not null)
        {
            try
            {
                return Clr.GetValue(target);
            }
            catch (Exception)
            {
                // Fall through to the DP store when a wrapper rejects the read.
            }
        }

        if (Dependency is not null)
        {
            return Dependency.GetValue(target);
        }

        return AttachedGet?.Invoke(target);
    }

    public void Set(System.Windows.Forms.Control target, object? value)
    {
        var converted = ConvertFor(value);
        if (Dependency is not null)
        {
            if (converted is null)
            {
                Dependency.ClearValue(target);
            }
            else
            {
                Dependency.SetValue(target, converted);
            }

            return;
        }

        if (Clr is not null)
        {
            Clr.SetValue(target, converted);
            return;
        }

        AttachedSet?.Invoke(target, converted);
    }

    public void Reset(System.Windows.Forms.Control target)
    {
        Dependency?.ClearValue(target);
    }

    public bool HasLocalValue(System.Windows.Forms.Control target)
        => Dependency is not null
            && target is XamlControl xaml
            && xaml.HasLocalValue(Dependency);

    public bool CanReset => Dependency is not null;

    private object? ConvertFor(object? value)
    {
        var targetType = ValueType;
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            {
                throw new InvalidOperationException($"{Name} needs a value.");
            }

            return null;
        }

        if (underlying.IsInstanceOfType(value))
        {
            return value;
        }

        if (underlying == typeof(System.Drawing.Color))
        {
            return value is string color ? XamlRuntime.ParseColor(color) : value;
        }

        if (underlying == typeof(XamlBrush))
        {
            return value switch
            {
                string text when text.Length == 0 => null,
                string text => XamlBrush.Parse(text),
                System.Drawing.Color color => new SolidColorBrush(color),
                _ => value,
            };
        }

        if (underlying == typeof(Font))
        {
            return value;
        }

        if (underlying.IsEnum)
        {
            return value is string name ? XamlRuntime.ParseEnum(underlying, name) : Enum.ToObject(underlying, value);
        }

        return XamlRuntime.ConvertValue(value, underlying);
    }
}

/// <summary>
/// Builds the right-hand property list from a selected control's dependency properties and CLR properties.
/// </summary>
/// <remarks>
/// The list is discovered by reflection because a click can land on any descendant of any card. Curated metadata
/// only decides ordering, categories, hiding and the common/advanced split; it does not enumerate values.
/// </remarks>
internal sealed class GalleryInspector
{
    private static readonly string[] CategoryOrder =
        ["Layout", "Appearance", "Content", "Behavior", "Custom", "Other"];

    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal)
    {
        "Name", "DataContext", "ItemsSource", "ItemTemplate", "ContentTemplate", "ContentTemplateSelector",
        "ItemContainerStyle", "ItemTemplateSelector", "Style", "Resources", "Parent", "Controls", "Handle",
        "BackColor", "ForeColor", "IsPointerOver", "IsKeyboardFocused", "Focused", "Visible", "Enabled",
        "Bounds", "Location", "Size", "ClientSize", "Region", "Cursor", "Anchor", "Dock", "Tag", "IsFocused",
        "CanFocus", "Capture", "AutoScroll", "AutoScrollPosition", "BorderStyle", "MinimumSize", "MaximumSize",
        "SelectedItem", "SelectedValue", "SelectedValuePath",
    };

    private static readonly string[] PreferredOrder =
    [
        "Width", "Height", "MinWidth", "MinHeight", "Margin", "Padding", "HorizontalAlignment",
        "VerticalAlignment", "Spacing", "Orientation", "ZIndex", "Row", "Column", "RowSpan", "ColumnSpan",
        "Dock", "Left", "Top", "TextWrapping", "TextAlignment", "TextTrimming", "Text", "Content", "Header",
        "Label", "Note", "PlaceholderText", "IsReadOnly", "AcceptsReturn", "MaxLength", "Background",
        "Foreground", "Font", "Visibility", "BorderBrush", "BorderThickness", "CornerRadius", "Fill", "Stroke",
        "StrokeThickness", "IsEnabled", "IsChecked", "IsExpanded", "IsSelected", "Value", "Minimum", "Maximum",
        "TabIndex", "IsTabStop", "Stretch",
    ];

    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "Width", "Height", "MinWidth", "MinHeight", "Margin", "Padding",
        "HorizontalAlignment", "VerticalAlignment", "Spacing", "Orientation", "TextWrapping", "TextAlignment",
        "TextTrimming", "Background", "Foreground", "Font", "Visibility", "BorderBrush", "BorderThickness",
        "CornerRadius", "Fill", "Stroke", "StrokeThickness", "Text", "Content", "Header", "Label", "Note",
        "PlaceholderText", "IsReadOnly", "AcceptsReturn", "MaxLength", "IsChecked", "IsExpanded", "IsSelected",
        "Value", "Minimum", "Maximum", "IsEnabled", "TabIndex", "IsTabStop", "Stretch",
    };

    private static readonly HashSet<string> LayoutNames = new(StringComparer.Ordinal)
    {
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding",
        "HorizontalAlignment", "VerticalAlignment", "Spacing", "Orientation", "Stretch", "Row", "Column",
        "RowSpan", "ColumnSpan", "ZIndex", "Left", "Top",
    };

    private static readonly HashSet<string> AppearanceNames = new(StringComparer.Ordinal)
    {
        "Background", "Foreground", "Font", "Visibility", "BorderBrush", "BorderThickness", "CornerRadius",
        "Fill", "Stroke", "StrokeThickness", "BarBrush", "TrackBrush", "ThumbBrush", "ThumbHoverBrush",
        "TrackHeight", "ThumbMinLength", "BarThickness",
    };

    private static readonly HashSet<string> ContentNames = new(StringComparer.Ordinal)
    {
        "Text", "Content", "Header", "Label", "Note", "PlaceholderText", "IsReadOnly", "AcceptsReturn",
        "MaxLength", "TextWrapping", "TextAlignment", "TextTrimming", "SelectedIndex", "SelectedItem",
        "SelectedValue", "Document",
    };

    private static readonly HashSet<string> BehaviorNames = new(StringComparer.Ordinal)
    {
        "IsEnabled", "IsChecked", "IsExpanded", "IsSelected", "IsThreeState", "TabIndex", "IsTabStop",
        "Value", "Minimum", "Maximum", "SmallChange", "LargeChange", "IsHitTestVisible", "Focusable",
    };

    private readonly XamlStackPanel _rows;
    private readonly XamlScrollableControl _scroll;
    private readonly List<RowView> _rowViews = [];
    private System.Windows.Forms.Control? _target;
    private string _filter = string.Empty;
    private bool _advanced;
    private bool _refreshing;

    public GalleryInspector(XamlStackPanel rows, XamlScrollableControl scroll)
    {
        _rows = rows;
        _scroll = scroll;
    }

    /// <summary>Raised with a short status line after an edit, reset or failure.</summary>
    public event Action<string>? Status;

    /// <summary>Whether all discovered properties are shown instead of the common set.</summary>
    public bool Advanced
    {
        get => _advanced;
        set => _advanced = value;
    }

    /// <summary>The current property-name filter; empty shows everything.</summary>
    public string Filter
    {
        get => _filter;
        set => _filter = value?.Trim() ?? string.Empty;
    }

    /// <summary>Selects the control to describe and rebuilds the rows.</summary>
    public void Bind(System.Windows.Forms.Control? target, System.Windows.Forms.Control? root)
    {
        _target = target;
        _ = root;
        Rebuild();
    }

    /// <summary>Rebuilds rows from the current target, filter and advanced state.</summary>
    public void Rebuild()
    {
        ClearRows();

        if (_target is null || _target.IsDisposed)
        {
            AddEmptyState();
            Fit();
            return;
        }

        var descriptors = Discover(_target);
        if (descriptors.Count == 0)
        {
            AddEmptyState();
            Fit();
            return;
        }

        var visible = new List<GalleryPropertyDescriptor>();
        foreach (var descriptor in descriptors)
        {
            if (_filter.Length > 0
                && !descriptor.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_advanced && !IsCommon(descriptor))
            {
                continue;
            }

            visible.Add(descriptor);
        }

        if (visible.Count == 0)
        {
            AddMessage("No properties match the filter.");
            Fit();
            return;
        }

        visible.Sort(CompareDescriptors);

        string? category = null;
        foreach (var descriptor in visible)
        {
            if (!string.Equals(category, descriptor.Category, StringComparison.Ordinal))
            {
                category = descriptor.Category;
                AddCaption(category);
            }

            AddRow(descriptor);
        }

        Fit();
    }

    /// <summary>Re-reads every editor from the target without rebuilding the rows.</summary>
    public void RefreshValues()
    {
        _refreshing = true;
        try
        {
            foreach (var row in _rowViews)
            {
                row.Refresh();
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private bool IsCommon(GalleryPropertyDescriptor descriptor)
    {
        if (descriptor.IsAttached)
        {
            return true;
        }

        if (Common.Contains(descriptor.Name))
        {
            return true;
        }

        // Custom dependency properties declared by the control's own type are always worth showing.
        return _target is not null && descriptor.Dependency?.OwnerType == _target.GetType();
    }

    private static int CompareDescriptors(GalleryPropertyDescriptor left, GalleryPropertyDescriptor right)
    {
        var leftCategory = Array.IndexOf(CategoryOrder, left.Category);
        var rightCategory = Array.IndexOf(CategoryOrder, right.Category);
        if (leftCategory != rightCategory)
        {
            return leftCategory.CompareTo(rightCategory);
        }

        var leftCommon = Common.Contains(left.Name) ? 0 : 1;
        var rightCommon = Common.Contains(right.Name) ? 0 : 1;
        if (leftCommon != rightCommon)
        {
            return leftCommon.CompareTo(rightCommon);
        }

        var leftOrder = Array.IndexOf(PreferredOrder, left.Name);
        var rightOrder = Array.IndexOf(PreferredOrder, right.Name);
        if (leftOrder >= 0 || rightOrder >= 0)
        {
            if (leftOrder < 0)
            {
                leftOrder = int.MaxValue;
            }

            if (rightOrder < 0)
            {
                rightOrder = int.MaxValue;
            }

            if (leftOrder != rightOrder)
            {
                return leftOrder.CompareTo(rightOrder);
            }
        }

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private void AddCaption(string category)
    {
        var caption = new XamlTextBlock
        {
            Text = category.ToUpperInvariant(),
            Font = GalleryTypography.Caption,
            Foreground = new SolidColorBrush(Theme.Current.MutedForeground),
            Margin = new XamlThickness(0f, 8f, 0f, 2f),
        };
        _rows.Controls.Add(caption);
    }

    private void AddEmptyState()
    {
        AddMessage("Select an element in the preview.");
        AddMessage("Click any control; Alt+Click selects the exact native HWND.");
    }

    private void AddMessage(string text)
    {
        var block = new XamlTextBlock
        {
            Text = text,
            Font = GalleryTypography.Body,
            Foreground = new SolidColorBrush(Theme.Current.MutedForeground),
            TextWrapping = VeloxDev.WinForms.Xaml.Controls.TextWrapping.Wrap,
            Margin = new XamlThickness(2f, 4f, 2f, 4f),
        };
        _rows.Controls.Add(block);
    }

    private void AddRow(GalleryPropertyDescriptor descriptor)
    {
        var target = _target!;
        var row = new XamlGrid
        {
            Height = 30,
            Margin = new XamlThickness(0f, 1f, 0f, 1f),
        };
        row.ColumnDefinitions.Add(new XamlColumnDefinition { Width = new XamlGridLength(132) });
        row.ColumnDefinitions.Add(new XamlColumnDefinition { Width = XamlGridLength.Star });
        row.ColumnDefinitions.Add(new XamlColumnDefinition { Width = new XamlGridLength(22) });

        var label = new XamlTextBlock
        {
            Font = GalleryTypography.Body,
            Foreground = new SolidColorBrush(Theme.Current.MutedForeground),
            VerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment.Center,
        };
        XamlGrid.SetColumn(label, 0);
        row.Controls.Add(label);

        var (editor, refresh) = CreateEditor(descriptor, target);
        XamlGrid.SetColumn(editor, 1);
        row.Controls.Add(editor);

        var reset = new XamlButton
        {
            Content = "×",
            Width = 20,
            Height = 20,
            Font = GalleryTypography.Caption,
            Visible = false,
            Visibility = VeloxDev.WinForms.Xaml.Controls.Visibility.Collapsed,
        };
        reset.Click += (_, _) =>
        {
            try
            {
                descriptor.Reset(target);
                InvalidateTarget();
                RefreshValues();
                Status?.Invoke($"{descriptor.Name} reset");
            }
            catch (Exception ex)
            {
                Status?.Invoke($"{descriptor.Name}: {ex.Message}");
            }
        };
        XamlGrid.SetColumn(reset, 2);
        row.Controls.Add(reset);

        var view = new RowView(
            target,
            () =>
            {
                var local = descriptor.HasLocalValue(target);
                var canReset = descriptor.CanReset && local;
                label.Text = local ? descriptor.Name + " •" : descriptor.Name;
                reset.Visible = canReset;
                reset.Visibility = canReset
                    ? VeloxDev.WinForms.Xaml.Controls.Visibility.Visible
                    : VeloxDev.WinForms.Xaml.Controls.Visibility.Collapsed;
                refresh();
            });

        _rowViews.Add(view);
        _rows.Controls.Add(row);
        view.Refresh();
    }

    private (System.Windows.Forms.Control Editor, Action Refresh) CreateEditor(
        GalleryPropertyDescriptor descriptor,
        System.Windows.Forms.Control target)
    {
        switch (descriptor.Kind)
        {
            case GalleryEditorKind.Boolean:
            {
                var box = new XamlCheckBox { Width = 20, Height = 20 };
                box.Click += (_, _) =>
                {
                    if (_refreshing)
                    {
                        return;
                    }

                    Commit(descriptor, box.IsChecked == true);
                };

                return (box, () =>
                {
                    var value = descriptor.Get(target);
                    box.IsChecked = value is bool flag && flag;
                });
            }

            case GalleryEditorKind.Enum:
            {
                var box = new XamlComboBox { Height = 26, Font = GalleryTypography.Body };
                var names = Enum.GetNames(descriptor.ValueType);
                box.ItemsSource = names;
                box.SelectionChanged += (_, _) =>
                {
                    if (_refreshing || box.SelectedIndex < 0 || box.SelectedIndex >= names.Length)
                    {
                        return;
                    }

                    Commit(descriptor, names[box.SelectedIndex]);
                };

                return (box, () =>
                {
                    var value = descriptor.Get(target);
                    var name = value?.ToString();
                    var index = Array.FindIndex(names, n => string.Equals(n, name, StringComparison.Ordinal));
                    box.SelectedIndex = index;
                });
            }

            case GalleryEditorKind.Color:
            case GalleryEditorKind.Brush:
            {
                var current = descriptor.Get(target);
                if (descriptor.Kind == GalleryEditorKind.Brush
                    && current is XamlBrush and not SolidColorBrush
                    && current is not null)
                {
                    var readOnly = new XamlTextBlock
                    {
                        Text = FormatValue(descriptor, current),
                        Font = GalleryTypography.Caption,
                        Foreground = new SolidColorBrush(Theme.Current.MutedForeground),
                        VerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment.Center,
                    };
                    return (readOnly, () =>
                    {
                        readOnly.Text = FormatValue(descriptor, descriptor.Get(target));
                    });
                }

                var panel = new XamlStackPanel
                {
                    Orientation = System.Windows.Forms.Orientation.Horizontal,
                    Spacing = 6,
                };
                var swatch = new XamlBorder
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = 4,
                    BorderThickness = new XamlThickness(1f),
                    BorderBrush = new SolidColorBrush(Theme.Current.ControlBorder),
                    Padding = new XamlThickness(0f),
                    VerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment.Center,
                };
                var text = new XamlTextBox
                {
                    Width = 96,
                    Height = 26,
                    Font = GalleryTypography.Body,
                };
                var pick = new XamlButton
                {
                    Content = "…",
                    Width = 24,
                    Height = 24,
                    Font = GalleryTypography.Caption,
                };

                void CommitText()
                {
                    if (_refreshing)
                    {
                        return;
                    }

                    if (text.Text.Length == 0)
                    {
                        if (descriptor.Kind == GalleryEditorKind.Brush)
                        {
                            Commit(descriptor, null);
                        }

                        return;
                    }

                    try
                    {
                        Commit(descriptor, FormatColorText(text.Text));
                    }
                    catch (Exception ex)
                    {
                        Status?.Invoke($"{descriptor.Name}: {ex.Message}");
                    }
                }

                text.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        CommitText();
                        e.SuppressKeyPress = true;
                    }
                };
                text.LostFocus += (_, _) => CommitText();
                pick.Click += (_, _) =>
                {
                    using var dialog = new ColorDialog { FullOpen = true, Color = ReadColor(descriptor.Get(target)) };
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        Commit(descriptor, dialog.Color);
                    }
                };

                panel.Controls.Add(swatch);
                panel.Controls.Add(text);
                panel.Controls.Add(pick);

                return (panel, () =>
                {
                    var value = descriptor.Get(target);
                    text.Text = FormatValue(descriptor, value);
                    var color = ReadColor(value);
                    swatch.Background = new SolidColorBrush(color);
                });
            }

            case GalleryEditorKind.Font:
            {
                var button = new XamlButton
                {
                    Width = 180,
                    Height = 26,
                    Font = GalleryTypography.Body,
                    HorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment.Left,
                };
                button.Click += (_, _) =>
                {
                    var current = descriptor.Get(target) as Font ?? target.Font;
                    using var dialog = new FontDialog { Font = current, ShowEffects = false };
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        Commit(descriptor, dialog.Font);
                    }
                };

                return (button, () =>
                {
                    button.Content = FormatValue(descriptor, descriptor.Get(target));
                });
            }

            case GalleryEditorKind.ReadOnly:
            {
                var block = new XamlTextBlock
                {
                    Font = GalleryTypography.Caption,
                    Foreground = new SolidColorBrush(Theme.Current.MutedForeground),
                    VerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment.Center,
                };
                return (block, () => block.Text = FormatValue(descriptor, descriptor.Get(target)));
            }

            default:
            {
                var text = new XamlTextBox
                {
                    Height = 26,
                    Font = GalleryTypography.Body,
                };
                void CommitText()
                {
                    if (_refreshing)
                    {
                        return;
                    }

                    try
                    {
                        var value = ParseText(descriptor, text.Text);
                        if (value is not null)
                        {
                            Commit(descriptor, value);
                        }
                    }
                    catch (Exception ex)
                    {
                        Status?.Invoke($"{descriptor.Name}: {ex.Message}");
                    }
                }

                text.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        CommitText();
                        e.SuppressKeyPress = true;
                    }
                };
                text.LostFocus += (_, _) => CommitText();

                return (text, () =>
                {
                    var value = descriptor.Get(target);
                    text.Text = FormatValue(descriptor, value);
                });
            }
        }
    }

    private object? ParseText(GalleryPropertyDescriptor descriptor, string text)
    {
        if (descriptor.Kind == GalleryEditorKind.Text)
        {
            return text;
        }

        if (text.Length == 0)
        {
            return null;
        }

        if (descriptor.Kind == GalleryEditorKind.Number)
        {
            var number = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            var type = Nullable.GetUnderlyingType(descriptor.ValueType) ?? descriptor.ValueType;
            return type == typeof(int)
                ? (int)Math.Round(number)
                : Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
        }

        return XamlRuntime.ConvertValue(text, Nullable.GetUnderlyingType(descriptor.ValueType) ?? descriptor.ValueType);
    }

    private void Commit(GalleryPropertyDescriptor descriptor, object? value)
    {
        if (_target is null || _target.IsDisposed)
        {
            return;
        }

        try
        {
            descriptor.Set(_target, value);
            InvalidateTarget();
            RefreshValues();
            Status?.Invoke($"{descriptor.Name} = {FormatValue(descriptor, descriptor.Get(_target))}");
        }
        catch (Exception ex)
        {
            Status?.Invoke($"{descriptor.Name}: {ex.Message}");
        }
    }

    private void InvalidateTarget()
    {
        if (_target is null || _target.IsDisposed)
        {
            return;
        }

        _target.PerformLayout();
        _target.Invalidate(true);
    }

    private void ClearRows()
    {
        _rowViews.Clear();
        foreach (System.Windows.Forms.Control child in _rows.Controls.Cast<System.Windows.Forms.Control>().ToArray())
        {
            _rows.Controls.Remove(child);
            if (!child.IsDisposed)
            {
                child.Dispose();
            }
        }
    }

    private void Fit()
    {
        GalleryLayout.FitNow(_scroll);
    }

    private static GalleryEditorKind KindOf(string name, Type valueType)
    {
        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (type == typeof(bool))
        {
            return GalleryEditorKind.Boolean;
        }

        if (type.IsEnum)
        {
            return GalleryEditorKind.Enum;
        }

        if (type == typeof(System.Drawing.Color))
        {
            return GalleryEditorKind.Color;
        }

        if (typeof(XamlBrush).IsAssignableFrom(type))
        {
            return GalleryEditorKind.Brush;
        }

        if (type == typeof(Font))
        {
            return GalleryEditorKind.Font;
        }

        if (type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return GalleryEditorKind.Number;
        }

        if (type == typeof(string) || type == typeof(object) || type == typeof(XamlThickness)
            || type == typeof(Padding) || type == typeof(Size) || type == typeof(Point)
            || type == typeof(SizeF) || type == typeof(PointF))
        {
            return GalleryEditorKind.Text;
        }

        return GalleryEditorKind.ReadOnly;
    }

    private static string FormatValue(GalleryPropertyDescriptor descriptor, object? value)
    {
        return value switch
        {
            null => string.Empty,
            System.Drawing.Color color => FormatColor(color),
            SolidColorBrush solid => FormatColor(solid.Color),
            XamlBrush brush => $"{brush.GetType().Name}",
            Font font => $"{font.Name}, {font.SizeInPoints:0.#}pt",
            bool flag => flag ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static System.Drawing.Color ReadColor(object? value)
        => value switch
        {
            System.Drawing.Color color => color,
            SolidColorBrush solid => solid.Color,
            _ => System.Drawing.Color.Transparent,
        };

    private static string FormatColor(System.Drawing.Color color)
        => "#" + color.ToArgb().ToString("X8", CultureInfo.InvariantCulture);

    private static object FormatColorText(string text) => XamlRuntime.ParseColor(text);

    private static List<GalleryPropertyDescriptor> Discover(System.Windows.Forms.Control target)
    {
        var result = new List<GalleryPropertyDescriptor>();
        if (target is XamlControl)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in target.GetType().GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.FieldType != typeof(DependencyProperty)
                    || !field.Name.EndsWith("Property", StringComparison.Ordinal))
                {
                    continue;
                }

                if (field.GetValue(null) is not DependencyProperty property || property.IsAttached)
                {
                    continue;
                }

                var name = field.Name[..^"Property".Length];
                if (!seen.Add(name) || Hidden.Contains(name))
                {
                    continue;
                }

                var clr = FindInstanceProperty(target.GetType(), name);
                if (clr is null || !clr.CanRead || !clr.CanWrite)
                {
                    continue;
                }

                var kind = KindOf(name, property.PropertyType);
                if (kind == GalleryEditorKind.ReadOnly)
                {
                    continue;
                }

                result.Add(new GalleryPropertyDescriptor(
                    name,
                    CategoryOf(name, target.GetType(), property),
                    property.PropertyType,
                    kind,
                    dependency: property,
                    clr: clr));
            }
        }
        else
        {
            foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead || !property.CanWrite)
                {
                    continue;
                }

                if (Hidden.Contains(property.Name) || !IsNativeEditable(property.PropertyType))
                {
                    continue;
                }

                var kind = KindOf(property.Name, property.PropertyType);
                if (kind == GalleryEditorKind.ReadOnly)
                {
                    continue;
                }

                result.Add(new GalleryPropertyDescriptor(
                    property.Name,
                    CategoryOf(property.Name, target.GetType(), null),
                    property.PropertyType,
                    kind,
                    clr: property));
            }
        }

        AddAttachedProperties(target, result);
        return result;
    }

    // Margin/Padding/Font/Width are redeclared with `new` on the XAML Control; Type.GetProperty throws
    // AmbiguousMatchException for those, so walk the hierarchy and take the most-derived declaration.
    private static PropertyInfo? FindInstanceProperty(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var candidates = current.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            PropertyInfo? match = null;
            foreach (var candidate in candidates)
            {
                if (!string.Equals(candidate.Name, name, StringComparison.Ordinal)
                    || candidate.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                // A property can only be declared once per type; keep the first non-indexed match.
                match = candidate;
                break;
            }

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool IsNativeEditable(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        return value == typeof(string) || value == typeof(bool) || value.IsEnum
            || value == typeof(int) || value == typeof(float) || value == typeof(double) || value == typeof(decimal)
            || value == typeof(System.Drawing.Color) || value == typeof(Font) || value == typeof(Padding)
            || value == typeof(Size) || value == typeof(Point);
    }

    private static string CategoryOf(string name, Type targetType, DependencyProperty? property)
    {
        if (LayoutNames.Contains(name))
        {
            return "Layout";
        }

        if (AppearanceNames.Contains(name))
        {
            return "Appearance";
        }

        if (ContentNames.Contains(name))
        {
            return "Content";
        }

        if (BehaviorNames.Contains(name))
        {
            return "Behavior";
        }

        if (property is not null && property.OwnerType == targetType)
        {
            return "Custom";
        }

        return "Other";
    }

    private static void AddAttachedProperties(
        System.Windows.Forms.Control target,
        List<GalleryPropertyDescriptor> result)
    {
        var parent = target.Parent;
        if (parent is not null)
        {
            result.Add(new GalleryPropertyDescriptor(
                "ZIndex", "Layout", typeof(int), GalleryEditorKind.Number,
                attachedGet: c => VeloxDev.WinForms.Xaml.Controls.Panel.GetZIndex(c),
                attachedSet: (c, v) => VeloxDev.WinForms.Xaml.Controls.Panel.SetZIndex(c, Convert.ToInt32(v, CultureInfo.InvariantCulture))));
        }

        if (parent is VeloxDev.WinForms.Xaml.Controls.Grid)
        {
            result.Add(new GalleryPropertyDescriptor(
                "Row", "Layout", typeof(int), GalleryEditorKind.Number,
                attachedGet: c => VeloxDev.WinForms.Xaml.Controls.Grid.GetRow(c),
                attachedSet: (c, v) => VeloxDev.WinForms.Xaml.Controls.Grid.SetRow(c, Convert.ToInt32(v, CultureInfo.InvariantCulture))));
            result.Add(new GalleryPropertyDescriptor(
                "Column", "Layout", typeof(int), GalleryEditorKind.Number,
                attachedGet: c => VeloxDev.WinForms.Xaml.Controls.Grid.GetColumn(c),
                attachedSet: (c, v) => VeloxDev.WinForms.Xaml.Controls.Grid.SetColumn(c, Convert.ToInt32(v, CultureInfo.InvariantCulture))));
            result.Add(new GalleryPropertyDescriptor(
                "RowSpan", "Layout", typeof(int), GalleryEditorKind.Number,
                attachedGet: c => VeloxDev.WinForms.Xaml.Controls.Grid.GetRowSpan(c),
                attachedSet: (c, v) => VeloxDev.WinForms.Xaml.Controls.Grid.SetRowSpan(c, Convert.ToInt32(v, CultureInfo.InvariantCulture))));
            result.Add(new GalleryPropertyDescriptor(
                "ColumnSpan", "Layout", typeof(int), GalleryEditorKind.Number,
                attachedGet: c => VeloxDev.WinForms.Xaml.Controls.Grid.GetColumnSpan(c),
                attachedSet: (c, v) => VeloxDev.WinForms.Xaml.Controls.Grid.SetColumnSpan(c, Convert.ToInt32(v, CultureInfo.InvariantCulture))));
        }

        if (parent is VeloxDev.WinForms.Xaml.Controls.DockPanel)
        {
            result.Add(new GalleryPropertyDescriptor(
                "Dock", "Layout", typeof(VeloxDev.WinForms.Xaml.Controls.DockPlacement), GalleryEditorKind.Enum,
                attachedGet: c => VeloxDev.WinForms.Xaml.Controls.DockPanel.GetDock(c),
                attachedSet: (c, v) => VeloxDev.WinForms.Xaml.Controls.DockPanel.SetDock(
                    c,
                    v is string name
                        ? (VeloxDev.WinForms.Xaml.Controls.DockPlacement)XamlRuntime.ParseEnum(
                            typeof(VeloxDev.WinForms.Xaml.Controls.DockPlacement), name)
                        : (VeloxDev.WinForms.Xaml.Controls.DockPlacement)v!)));
        }

        if (parent is VeloxDev.WinForms.Xaml.Controls.Canvas)
        {
            result.Add(new GalleryPropertyDescriptor(
                "Left", "Layout", typeof(int), GalleryEditorKind.Number,
                attachedGet: c => VeloxDev.WinForms.Xaml.Controls.Canvas.GetLeft(c),
                attachedSet: (c, v) => VeloxDev.WinForms.Xaml.Controls.Canvas.SetLeft(c, Convert.ToInt32(v, CultureInfo.InvariantCulture))));
            result.Add(new GalleryPropertyDescriptor(
                "Top", "Layout", typeof(int), GalleryEditorKind.Number,
                attachedGet: c => VeloxDev.WinForms.Xaml.Controls.Canvas.GetTop(c),
                attachedSet: (c, v) => VeloxDev.WinForms.Xaml.Controls.Canvas.SetTop(c, Convert.ToInt32(v, CultureInfo.InvariantCulture))));
        }
    }

    private sealed class RowView(
        System.Windows.Forms.Control target,
        Action refresh)
    {
        public void Refresh()
        {
            if (target.IsDisposed)
            {
                return;
            }

            refresh();
        }
    }
}
