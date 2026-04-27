using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class TableRowProvider
{
    [VeloxProperty] public partial ObservableCollection<string> Cells { get; set; }

    public TableRowProvider()
    {
        Cells = [];
    }

    public static TableRowProvider Create(int columnCount)
    {
        var row = new TableRowProvider();
        for (int i = 0; i < columnCount; i++)
            row.Cells.Add(string.Empty);
        return row;
    }
}
