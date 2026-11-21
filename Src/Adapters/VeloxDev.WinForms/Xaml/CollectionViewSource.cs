using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Owns a <see cref="CollectionView"/> and rebuilds it when the source or the view settings change.
/// </summary>
/// <remarks>
/// <para>
/// The source and the settings live on the source object, not on the view: assigning <see cref="Source"/>,
/// changing <see cref="Filter"/>, or adding, removing or replacing an entry of <see cref="SortDescriptions"/> or
/// <see cref="GroupDescriptions"/> disposes the previous view and creates a new one. The new view starts before
/// the first item, so a current position is not preserved across a rebuild.
/// </para>
/// <para>
/// A <see langword="null"/> source produces a <see langword="null"/> view; the view is created lazily and can be
/// used as an <see cref="VeloxDev.WinForms.Xaml.Controls.ItemsControl.ItemsSource"/> because it also raises
/// <see cref="INotifyCollectionChanged"/>. <see cref="View"/> raises both <see cref="ViewChanged"/> and
/// <see cref="INotifyPropertyChanged.PropertyChanged"/>, so a one-way binding to <c>View</c> follows a rebuild.
/// </para>
/// </remarks>
public sealed class CollectionViewSource : INotifyPropertyChanged
{
    private readonly ObservableCollection<SortDescription> _sortDescriptions = [];
    private readonly ObservableCollection<GroupDescription> _groupDescriptions = [];
    private IEnumerable? _source;
    private Predicate<object>? _filter;
    private CollectionView? _view;
    private bool _rebuilding;

    /// <summary>Creates an empty source with no settings.</summary>
    public CollectionViewSource()
    {
        _sortDescriptions.CollectionChanged += OnViewSettingChanged;
        _groupDescriptions.CollectionChanged += OnViewSettingChanged;
    }

    /// <summary>Raised after <see cref="View"/> has been replaced.</summary>
    public event EventHandler? ViewChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The sequence the view reads.</summary>
    public IEnumerable? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value))
            {
                return;
            }

            _source = value;
            RebuildView();
        }
    }

    /// <summary>The filter applied to every item; <see langword="null"/> keeps every item.</summary>
    public Predicate<object>? Filter
    {
        get => _filter;
        set
        {
            if (ReferenceEquals(_filter, value))
            {
                return;
            }

            _filter = value;
            RebuildView();
        }
    }

    /// <summary>The sort keys applied by the view, in priority order.</summary>
    public ObservableCollection<SortDescription> SortDescriptions => _sortDescriptions;

    /// <summary>The group keys applied by the view, from the outermost level inward.</summary>
    public ObservableCollection<GroupDescription> GroupDescriptions => _groupDescriptions;

    /// <summary>The current view, or <see langword="null"/> when <see cref="Source"/> is <see langword="null"/>.</summary>
    public CollectionView? View => _view;

    /// <summary>Rebuilds <see cref="View"/> from the current source and settings.</summary>
    public void Refresh() => RebuildView();

    private void OnViewSettingChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_rebuilding)
        {
            RebuildView();
        }
    }

    private void RebuildView()
    {
        if (_rebuilding)
        {
            return;
        }

        _rebuilding = true;
        try
        {
            _view?.Dispose();
            _view = _source is null ? null : new CollectionView(_source);
            _view?.ApplyViewSettings(_sortDescriptions, _filter, _groupDescriptions);
        }
        finally
        {
            _rebuilding = false;
        }

        ViewChanged?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(View)));
    }
}
