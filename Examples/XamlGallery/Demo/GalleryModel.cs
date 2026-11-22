using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VeloxDev.MVVM;
using VeloxDev.WinForms.Xaml;

namespace Demo;

/// <summary>One row shown by the data-bound controls in the gallery.</summary>
public sealed class SampleRow
{
    /// <summary>The display name of the row.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A numeric value the data controls render.</summary>
    public double Value { get; set; }

    /// <summary>Whether the row is enabled; bound by the check-box column.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>One node of the small hierarchical sample shown by the tree view.</summary>
public sealed class GalleryNode
{
    /// <summary>The node label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The child nodes; the tree view discovers them by this name.</summary>
    public ObservableCollection<GalleryNode> Children { get; } = [];
}

/// <summary>
/// The data context behind every gallery section: collections, a command, scalar values and one source-level
/// validation error used by the binding section.
/// </summary>
public sealed class GalleryModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);
    private string? _optionalName = "Ada Lovelace";
    private double _amount = 42.5;
    private DateTime? _date = DateTime.Today;
    private int _saveCount;
    private string _collectionState = "Unsorted · all rows";
    private bool _sortedDescending;
    private bool _filterEnabled;

    /// <summary>Creates the sample collections and the save command.</summary>
    public GalleryModel()
    {
        Rows =
        [
            new SampleRow { Name = "Alpha", Value = 12.5 },
            new SampleRow { Name = "Bravo", Value = 38.0 },
            new SampleRow { Name = "Charlie", Value = 7.25 },
            new SampleRow { Name = "Delta", Value = 91.0, Enabled = false },
            new SampleRow { Name = "Echo", Value = 54.75 },
        ];

        TreeNodes =
        [
            new GalleryNode
            {
                Name = "Surface",
                Children =
                {
                    new GalleryNode { Name = "Canvas" },
                    new GalleryNode { Name = "Minimap" },
                },
            },
            new GalleryNode
            {
                Name = "Controls",
                Children =
                {
                    new GalleryNode { Name = "Buttons" },
                    new GalleryNode { Name = "Selectors" },
                },
            },
        ];

        SaveCommand = new VeloxCommand(_ => SaveCount++);
        RowsView = new CollectionView(Rows);
        for (var index = 0; index < 400; index++)
        {
            VirtualRows.Add(new SampleRow { Name = $"Row {index + 1:000}", Value = index, Enabled = index % 7 != 0 });
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <summary>The rows bound by the list, grid and combo-box examples.</summary>
    public ObservableCollection<SampleRow> Rows { get; }

    /// <summary>The same rows under the name the indexer binding examples use.</summary>
    public ObservableCollection<SampleRow> Items => Rows;

    /// <summary>A sorted and filtered view over <see cref="Rows"/>; the data section binds it directly.</summary>
    public CollectionView RowsView { get; }

    /// <summary>Four hundred rows used by the virtualized list.</summary>
    public ObservableCollection<SampleRow> VirtualRows { get; } = [];

    /// <summary>The current sort and filter state of <see cref="RowsView"/>.</summary>
    public string CollectionState
    {
        get => _collectionState;
        private set => SetField(ref _collectionState, value);
    }

    /// <summary>Sorts the view by name, toggling direction on each call.</summary>
    public void SortRows()
    {
        _sortedDescending = !_sortedDescending;
        RowsView.SortDescriptions.Clear();
        RowsView.SortDescriptions.Add(new SortDescription(
            nameof(SampleRow.Name),
            _sortedDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        UpdateCollectionState();
    }

    /// <summary>Filters the view to enabled rows, or clears the filter.</summary>
    public void FilterRows()
    {
        _filterEnabled = !_filterEnabled;
        RowsView.Filter = _filterEnabled
            ? static item => item is SampleRow { Enabled: true }
            : null;
        UpdateCollectionState();
    }

    private void UpdateCollectionState()
        => CollectionState =
        $"{(_sortedDescending ? "Name ↓" : "Name ↑")} · {(_filterEnabled ? "enabled only" : "all rows")} · {RowsView.Count} shown";

    /// <summary>The hierarchical sample bound by the tree view.</summary>
    public ObservableCollection<GalleryNode> TreeNodes { get; }

    /// <summary>The command invoked by the button and input-binding examples.</summary>
    public ICommand SaveCommand { get; }

    /// <summary>How many times <see cref="SaveCommand"/> has run.</summary>
    public int SaveCount
    {
        get => _saveCount;
        private set => SetField(ref _saveCount, value);
    }

    /// <summary>A nullable date bound by the calendar, date picker and string-format examples.</summary>
    public DateTime? Date
    {
        get => _date;
        set => SetField(ref _date, value);
    }

    /// <summary>A numeric value bound with string formatting.</summary>
    public double Amount
    {
        get => _amount;
        set => SetField(ref _amount, value);
    }

    /// <summary>A name that must not be blank; the binding section shows the source-level error.</summary>
    public string? OptionalName
    {
        get => _optionalName;
        set
        {
            if (!SetField(ref _optionalName, value))
            {
                return;
            }

            ValidateOptionalName();
        }
    }

    /// <inheritdoc />
    public bool HasErrors => _errors.Count > 0;

    /// <inheritdoc />
    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is null || !_errors.TryGetValue(propertyName, out var errors))
        {
            return Array.Empty<string>();
        }

        return errors;
    }

    private void ValidateOptionalName()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(OptionalName))
        {
            errors.Add("A display name is required.");
        }

        if (errors.Count == 0)
        {
            _errors.Remove(nameof(OptionalName));
        }
        else
        {
            _errors[nameof(OptionalName)] = errors;
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(OptionalName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
