using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// A trigger whose condition is the value of a binding path on the control's data context rather than a dependency
/// property. It re-evaluates when the bound source raises <see cref="INotifyPropertyChanged"/> and reattaches when the
/// data context or tree position changes.
/// </summary>
/// <remarks>
/// The trigger's <see cref="Trigger.Setters"/> and enter/exit actions still target the control that owns the trigger.
/// A raw string <see cref="Trigger.Value"/> is converted to the observed value's runtime type for comparison, so
/// <c>Value="True"</c> matches a bound <c>bool</c>.
/// </remarks>
public sealed class DataTrigger : Trigger
{
    private readonly ConditionalWeakTable<VeloxDev.WinForms.Xaml.Controls.Control, DataTriggerObserver> _observers = new();
    private Binding? _binding;
    private string? _path;

    /// <summary>The binding whose current value is the condition; the binding is observed, not written.</summary>
    public Binding? Binding
    {
        get => _binding;
        set
        {
            if (ReferenceEquals(_binding, value))
            {
                return;
            }

            _binding = value;
            OnChanged();
        }
    }

    /// <summary>A simple path alternative to <see cref="Binding"/>.</summary>
    public string? Path
    {
        get => _path;
        set
        {
            if (_path == value)
            {
                return;
            }

            _path = value;
            OnChanged();
        }
    }

    internal override bool Matches(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        Attach(control);

        if (!_observers.TryGetValue(control, out var observer))
        {
            return false;
        }

        return Compare(observer.Value);
    }

    internal override void Attach(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (_observers.TryGetValue(control, out var existing))
        {
            existing.Refresh();
            return;
        }

        var observer = new DataTriggerObserver(
            control,
            () => control.RefreshTrigger(this),
            () => Binding?.Path ?? Path ?? string.Empty);

        _observers.Add(control, observer);
        observer.Refresh();
    }

    internal override void Detach(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (_observers.TryGetValue(control, out var observer))
        {
            observer.Dispose();
            _observers.Remove(control);
        }
    }

    internal override void OnDataContextChanged(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
        if (_observers.TryGetValue(control, out var observer))
        {
            observer.Refresh();
        }
    }

    private bool Compare(object? actual)
    {
        var expected = Value;
        if (expected is string text && actual is not null && actual is not string)
        {
            try
            {
                expected = XamlRuntime.ConvertValue(text, actual.GetType());
            }
            catch
            {
                return false;
            }
        }

        return Equals(actual, expected);
    }
}

/// <summary>Observes one dotted binding path on a control's effective data context for a <see cref="DataTrigger"/>.</summary>
internal sealed class DataTriggerObserver : IDisposable
{
    private readonly VeloxDev.WinForms.Xaml.Controls.Control _control;
    private readonly Action _onSourceChanged;
    private readonly Func<string> _pathProvider;
    private readonly List<INotifyPropertyChanged> _observedProperties = [];
    private readonly List<INotifyCollectionChanged> _observedCollections = [];

    private object? _source;
    private bool _disposed;

    public DataTriggerObserver(VeloxDev.WinForms.Xaml.Controls.Control control, Action onSourceChanged, Func<string> pathProvider)
    {
        _control = control;
        _onSourceChanged = onSourceChanged;
        _pathProvider = pathProvider;
    }

    /// <summary>The current value at the observed path.</summary>
    public object? Value { get; private set; }

    /// <summary>Re-resolves the data context and the subscription without raising the source-changed callback.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        DetachSource();
        _source = _control.DataContext;
        if (_source is null)
        {
            Value = null;
            return;
        }

        ResolveAndObserve();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        DetachSource();
        _source = null;
        Value = null;
    }

    private void ResolveAndObserve()
    {
        var nodes = new List<object?>();
        var current = _source;
        nodes.Add(current);

        var path = _pathProvider();
        if (string.IsNullOrEmpty(path) || path == ".")
        {
            Value = current;
            Observe(nodes);
            return;
        }

        Value = null;
        var segments = path.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            if (current is null || !TryParseSegment(segments[index], out var name, out var indexText, out var hasIndex))
            {
                break;
            }

            if (name.Length > 0)
            {
                var descriptor = TypeDescriptor.GetProperties(current).Find(name, ignoreCase: false);
                if (descriptor is null)
                {
                    break;
                }

                current = descriptor.GetValue(current);
                if (current is null)
                {
                    break;
                }
            }

            if (hasIndex)
            {
                // 集合本身也要订阅：索引偏移、Replace、Reset、Move 都要重新解析整条路径。
                nodes.Add(current);

                if (!TryGetIndexed(current, indexText, out var indexed))
                {
                    break;
                }

                current = indexed;
                if (current is null)
                {
                    break;
                }
            }

            if (index == segments.Length - 1)
            {
                Value = current;
                break;
            }

            nodes.Add(current);
        }

        Observe(nodes);
    }

    private static bool TryParseSegment(string segment, out string name, out string indexText, out bool hasIndex)
    {
        name = segment;
        indexText = string.Empty;
        hasIndex = false;

        var open = segment.IndexOf('[');
        if (open < 0)
        {
            return true;
        }

        if (!segment.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        name = segment.Substring(0, open);
        indexText = segment.Substring(open + 1, segment.Length - open - 2).Trim();
        hasIndex = true;
        return indexText.Length > 0 && indexText.IndexOf('[') < 0;
    }

    private static bool TryGetIndexed(object? container, string indexText, out object? value)
    {
        value = null;
        if (container is null)
        {
            return false;
        }

        var keyText = indexText.Trim('"', '\'');

        if (container is IList list)
        {
            if (int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < list.Count)
            {
                value = list[index];
                return true;
            }

            return false;
        }

        if (container is IDictionary dictionary)
        {
            object key = int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
                ? numeric
                : keyText;

            if (dictionary.Contains(key))
            {
                value = dictionary[key];
                return true;
            }
        }

        return false;
    }

    private void Observe(List<object?> nodes)
    {
        var nextProperties = new List<INotifyPropertyChanged>();
        var nextCollections = new List<INotifyCollectionChanged>();

        foreach (var node in nodes)
        {
            if (node is INotifyPropertyChanged observable
                && !nextProperties.Exists(existing => ReferenceEquals(existing, observable)))
            {
                nextProperties.Add(observable);
            }

            if (node is INotifyCollectionChanged collection
                && !nextCollections.Exists(existing => ReferenceEquals(existing, collection)))
            {
                nextCollections.Add(collection);
            }
        }

        foreach (var previous in _observedProperties)
        {
            if (!nextProperties.Exists(candidate => ReferenceEquals(candidate, previous)))
            {
                previous.PropertyChanged -= OnSourcePropertyChanged;
            }
        }

        foreach (var previous in _observedCollections)
        {
            if (!nextCollections.Exists(candidate => ReferenceEquals(candidate, previous)))
            {
                previous.CollectionChanged -= OnSourceCollectionChanged;
            }
        }

        foreach (var candidate in nextProperties)
        {
            if (!_observedProperties.Exists(previous => ReferenceEquals(previous, candidate)))
            {
                candidate.PropertyChanged += OnSourcePropertyChanged;
            }
        }

        foreach (var candidate in nextCollections)
        {
            if (!_observedCollections.Exists(previous => ReferenceEquals(previous, candidate)))
            {
                candidate.CollectionChanged += OnSourceCollectionChanged;
            }
        }

        _observedProperties.Clear();
        _observedProperties.AddRange(nextProperties);
        _observedCollections.Clear();
        _observedCollections.AddRange(nextCollections);
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        ResolveAndObserve();
        _onSourceChanged();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        ResolveAndObserve();
        _onSourceChanged();
    }

    private void DetachSource()
    {
        foreach (var observable in _observedProperties)
        {
            observable.PropertyChanged -= OnSourcePropertyChanged;
        }

        foreach (var collection in _observedCollections)
        {
            collection.CollectionChanged -= OnSourceCollectionChanged;
        }

        _observedProperties.Clear();
        _observedCollections.Clear();
    }
}
