using System.ComponentModel;

namespace VeloxDev.WinForms.Xaml;

/// <summary>The priority of a value written into an <see cref="IWinformsObject"/>.</summary>
/// <remarks>Lower values win. The order mirrors Avalonia's <c>BindingPriority</c>.</remarks>
public enum BindingPriority
{
    /// <summary>A running animation frame.</summary>
    Animation = -1,

    /// <summary>A direct <c>SetValue</c> call or a generated initializer.</summary>
    LocalValue = 0,

    /// <summary>A style trigger.</summary>
    StyleTrigger = 1,

    /// <summary>A control-template setter.</summary>
    Template = 2,

    /// <summary>A style setter.</summary>
    Style = 3,

    /// <summary>A value inherited from an ancestor.</summary>
    Inherited = 4,

    /// <summary>No value has been written.</summary>
    Unset = 5,
}

/// <summary>An object that stores <see cref="DependencyProperty"/> values with a priority stack.</summary>
public interface IWinformsObject
{
    /// <summary>Reads the effective value of <paramref name="property"/>.</summary>
    /// <param name="property">The property to read.</param>
    object? GetValue(DependencyProperty property);

    /// <summary>Writes <paramref name="value"/> at <paramref name="priority"/>.</summary>
    /// <param name="property">The property to write.</param>
    /// <param name="value">The new value.</param>
    /// <param name="priority">The priority layer that owns the value.</param>
    void SetValue(DependencyProperty property, object? value, BindingPriority priority = BindingPriority.LocalValue);

    /// <summary>Removes the value at <paramref name="priority"/>.</summary>
    /// <param name="property">The property to clear.</param>
    /// <param name="priority">The priority layer to remove.</param>
    void ClearValue(DependencyProperty property, BindingPriority priority = BindingPriority.LocalValue);

    /// <summary>True when any priority layer holds a value for <paramref name="property"/>.</summary>
    /// <param name="property">The property to inspect.</param>
    bool IsSet(DependencyProperty property);

    /// <summary>Raised after the effective value of a property changes.</summary>
    event EventHandler<DependencyPropertyChangedEventArgs>? PropertyChanged;

    /// <summary>Pushes an observable into a property layer and restores the previous layer on dispose.</summary>
    IDisposable Bind<T>(DependencyProperty property, IObservable<T?> source, BindingPriority priority = BindingPriority.LocalValue);

    /// <summary>Returns an observable that emits the current value and every later change.</summary>
    IObservable<T?> GetObservable<T>(DependencyProperty property);
}

/// <summary>The WPF-named base implementation of <see cref="IWinformsObject"/> for the visual backend.</summary>
public abstract class WinformsObject : IWinformsObject, INotifyPropertyChanged
{
    private readonly Dictionary<DependencyProperty, SortedDictionary<BindingPriority, object?>> _values = [];
    private PropertyChangedEventHandler? _clrPropertyChanged;

    /// <summary>Raised after the effective value of a property changes.</summary>
    public event EventHandler<DependencyPropertyChangedEventArgs>? PropertyChanged;

    /// <inheritdoc />
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _clrPropertyChanged += value;
        remove => _clrPropertyChanged -= value;
    }

    /// <inheritdoc />
    public object? GetValue(DependencyProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (_values.TryGetValue(property, out var layers) && layers.Count > 0)
        {
            foreach (var layer in layers)
            {
                return layer.Value;
            }
        }

        if (property.Metadata.Inherits && GetInheritedValue(property) is { } inherited)
        {
            return inherited;
        }

        return property.DefaultValue;
    }

    /// <inheritdoc />
    public void SetValue(DependencyProperty property, object? value, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        var oldValue = GetValue(property);
        value = property.CoerceFor(this, value);

        if (!_values.TryGetValue(property, out var layers))
        {
            layers = [];
            _values[property] = layers;
        }

        layers[priority] = value;
        var newValue = GetValue(property);
        if (!Equals(oldValue, newValue))
        {
            RaisePropertyChanged(property, oldValue, newValue, priority);
        }
    }

    /// <inheritdoc />
    public void ClearValue(DependencyProperty property, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (!_values.TryGetValue(property, out var layers) || !layers.ContainsKey(priority))
        {
            return;
        }

        var oldValue = GetValue(property);
        layers.Remove(priority);
        if (layers.Count == 0)
        {
            _values.Remove(property);
        }

        var newValue = GetValue(property);
        if (!Equals(oldValue, newValue))
        {
            RaisePropertyChanged(property, oldValue, newValue, priority);
        }
    }

    /// <inheritdoc />
    public bool IsSet(DependencyProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        return _values.TryGetValue(property, out var layers) && layers.Count > 0;
    }

    /// <inheritdoc />
    public IDisposable Bind<T>(DependencyProperty property, IObservable<T?> source, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (source is null) throw new ArgumentNullException(nameof(source));
        var observer = new BindingObserver<T>(this, property, priority);
        var subscription = source.Subscribe(observer);
        return new BindingSubscription(subscription, this, property, priority);
    }

    /// <inheritdoc />
    public IObservable<T?> GetObservable<T>(DependencyProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        return new PropertyObservable<T>(this, property);
    }

    /// <summary>Resolves a value inherited from an ancestor.</summary>
    /// <param name="property">The property whose value is missing.</param>
    /// <returns>The ancestor value, or <see langword="null"/> when there is none.</returns>
    protected virtual object? GetInheritedValue(DependencyProperty property) => null;

    /// <summary>Called before <see cref="PropertyChanged"/> and <see cref="INotifyPropertyChanged"/> are raised.</summary>
    /// <param name="e">The change data.</param>
    protected virtual void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    /// <summary>Raises the change notifications for a property whose effective value changed.</summary>
    /// <param name="property">The changed property.</param>
    /// <param name="oldValue">The previous effective value.</param>
    /// <param name="newValue">The new effective value.</param>
    /// <param name="priority">The priority layer that changed.</param>
    protected void RaisePropertyChanged(
        DependencyProperty property,
        object? oldValue,
        object? newValue,
        BindingPriority priority = BindingPriority.LocalValue)
    {
        var args = new DependencyPropertyChangedEventArgs(property, oldValue, newValue, priority);
        OnPropertyChanged(args);
        PropertyChanged?.Invoke(this, args);
        _clrPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property.Name));
    }

    private sealed class BindingObserver<T> : IObserver<T?>
    {
        private readonly WinformsObject _owner;
        private readonly DependencyProperty _property;
        private readonly BindingPriority _priority;

        public BindingObserver(WinformsObject owner, DependencyProperty property, BindingPriority priority)
        {
            _owner = owner;
            _property = property;
            _priority = priority;
        }

        public void OnNext(T? value)
        {
            if (value is null && _property.PropertyType.IsValueType && Nullable.GetUnderlyingType(_property.PropertyType) is null)
            {
                _owner.ClearValue(_property, _priority);
                return;
            }

            _owner.SetValue(_property, value, _priority);
        }

        public void OnError(Exception error) => _owner.ClearValue(_property, _priority);

        public void OnCompleted() => _owner.ClearValue(_property, _priority);
    }

    private sealed class BindingSubscription : IDisposable
    {
        private readonly IDisposable _subscription;
        private readonly WinformsObject _owner;
        private readonly DependencyProperty _property;
        private readonly BindingPriority _priority;
        private bool _disposed;

        public BindingSubscription(
            IDisposable subscription,
            WinformsObject owner,
            DependencyProperty property,
            BindingPriority priority)
        {
            _subscription = subscription;
            _owner = owner;
            _property = property;
            _priority = priority;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscription.Dispose();
            _owner.ClearValue(_property, _priority);
        }
    }

    private sealed class PropertyObservable<T> : IObservable<T?>
    {
        private readonly WinformsObject _owner;
        private readonly DependencyProperty _property;

        public PropertyObservable(WinformsObject owner, DependencyProperty property)
        {
            _owner = owner;
            _property = property;
        }

        public IDisposable Subscribe(IObserver<T?> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));
            observer.OnNext((T?)_property.GetValue(_owner));

            void Handler(object? sender, DependencyPropertyChangedEventArgs args)
            {
                if (ReferenceEquals(args.Property, _property))
                {
                    observer.OnNext((T?)_property.GetValue(_owner));
                }
            }

            _owner.PropertyChanged += Handler;
            return new Unsubscriber(() => _owner.PropertyChanged -= Handler);
        }

        private sealed class Unsubscriber(Action unsubscribe) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                unsubscribe();
            }
        }
    }
}
