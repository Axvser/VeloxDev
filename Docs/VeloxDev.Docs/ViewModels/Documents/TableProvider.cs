using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class TableProvider : IWikiElement
{
    [VeloxProperty] private IWikiElement? parent = null;
    [VeloxProperty] public partial ObservableCollection<string> Headers { get; set; }
    [VeloxProperty] public partial ObservableCollection<string> Alignments { get; set; }
    [VeloxProperty] public partial ObservableCollection<TableRowProvider> Rows { get; set; }

    public TableProvider()
    {
        Headers = [];
        Alignments = [];
        Rows = [];
    }

    public static TableProvider CreateDefault(IWikiElement? parent = null)
    {
        return new TableProvider
        {
            Parent = parent,
            Headers = ["Column 1", "Column 2", "Column 3"],
            Alignments = ["Left", "Left", "Left"],
            Rows =
            [
                TableRowProvider.Create(3),
                TableRowProvider.Create(3)
            ]
        };
    }

    [VeloxCommand]
    private void AddColumn() =>
        InsertColumnAt(Headers.Count);

    [VeloxCommand]
    private void InsertColumn(object? parameter)
    {
        var index = parameter is int i ? i : Headers.Count;
        InsertColumnAt(index);
    }

    [VeloxCommand]
    private void RemoveColumn(object? parameter)
    {
        if (Headers.Count <= 1)
            return;

        var index = parameter is int i ? i : Headers.Count - 1;
        if (index < 0 || index >= Headers.Count)
            return;

        Headers.RemoveAt(index);
        if (Alignments.Count > index)
            Alignments.RemoveAt(index);
        foreach (var row in Rows)
        {
            if (row.Cells.Count > index)
                row.Cells.RemoveAt(index);
        }
    }

    [VeloxCommand]
    private void AddRow() =>
        Rows.Add(TableRowProvider.Create(Headers.Count));

    [VeloxCommand]
    private void InsertRow(object? parameter)
    {
        var index = parameter is int i ? i : Rows.Count;
        if (index < 0) index = 0;
        if (index > Rows.Count) index = Rows.Count;
        Rows.Insert(index, TableRowProvider.Create(Headers.Count));
    }

    [VeloxCommand]
    private void RemoveRow(object? parameter)
    {
        switch (parameter)
        {
            case TableRowProvider row:
                Rows.Remove(row);
                break;
            case int index when index >= 0 && index < Rows.Count:
                Rows.RemoveAt(index);
                break;
        }
    }

    private void InsertColumnAt(int index)
    {
        if (index < 0) index = 0;
        if (index > Headers.Count) index = Headers.Count;

        Headers.Insert(index, $"Column {index + 1}");
        Alignments.Insert(index, "Left");
        foreach (var row in Rows)
            row.Cells.Insert(index, string.Empty);
    }
}
