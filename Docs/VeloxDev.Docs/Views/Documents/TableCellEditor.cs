using Avalonia;
using Avalonia.Controls;

namespace VeloxDev.Docs;

public class TableCellEditor : TextBox
{
    public static readonly StyledProperty<object?> OwnerProperty =
        AvaloniaProperty.Register<TableCellEditor, object?>(nameof(Owner));

    public static readonly StyledProperty<int> IndexProperty =
        AvaloniaProperty.Register<TableCellEditor, int>(nameof(Index));

    public object? Owner
    {
        get => GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    public int Index
    {
        get => GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }
}
