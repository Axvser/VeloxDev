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

/// <summary>An object that stores <see cref="WinformsProperty"/> values with a priority stack.</summary>
public interface IWinformsObject
{
    /// <summary>Reads the effective value of <paramref name="property"/>.</summary>
    /// <param name="property">The property to read.</param>
    object? GetValue(WinformsProperty property);

    /// <summary>Writes <paramref name="value"/> at <paramref name="priority"/>.</summary>
    /// <param name="property">The property to write.</param>
    /// <param name="value">The new value.</param>
    /// <param name="priority">The priority layer that owns the value.</param>
    void SetValue(WinformsProperty property, object? value, BindingPriority priority = BindingPriority.LocalValue);

    /// <summary>Removes the value at <paramref name="priority"/>.</summary>
    /// <param name="property">The property to clear.</param>
    /// <param name="priority">The priority layer to remove.</param>
    void ClearValue(WinformsProperty property, BindingPriority priority = BindingPriority.LocalValue);

    /// <summary>True when any priority layer holds a value for <paramref name="property"/>.</summary>
    /// <param name="property">The property to inspect.</param>
    bool IsSet(WinformsProperty property);

    /// <summary>Raised after the effective value of a property changes.</summary>
    event EventHandler<WinformsPropertyChangedEventArgs>? PropertyChanged;

    /// <summary>Pushes an observable into a property layer and restores the previous layer on dispose.</summary>
    IDisposable Bind<T>(WinformsProperty<T> property, IObservable<T?> source, BindingPriority priority = BindingPriority.LocalValue);

    /// <summary>Returns an observable that emits the current value and every later change.</summary>
    IObservable<T?> GetObservable<T>(WinformsProperty<T> property);
}

/// <summary>Data for a change raised by <see cref="IWinformsObject.PropertyChanged"/>.</summary>
public sealed class WinformsPropertyChangedEventArgs : EventArgs
{
    internal WinformsPropertyChangedEventArgs(
        WinformsProperty property,
        object? oldValue,
        object? newValue,
        BindingPriority priority)
    {
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
        Priority = priority;
    }

    /// <summary>The property whose effective value changed.</summary>
    public WinformsProperty Property { get; }

    /// <summary>The effective value before the change.</summary>
    public object? OldValue { get; }

    /// <summary>The effective value after the change.</summary>
    public object? NewValue { get; }

    /// <summary>The priority layer that triggered the change.</summary>
    public BindingPriority Priority { get; }
}

/// <summary>A property registered against <see cref="IWinformsObject"/>.</summary>
public abstract class WinformsProperty
{
    private static readonly object RegistryLock = new();
    private static readonly Dictionary<string, WinformsProperty> Registry = [];

    private protected WinformsProperty(
        string name,
        Type propertyType,
        Type ownerType,
        object? defaultValue,
        bool isAttached,
        bool inherits,
        bool affectsMeasure,
        bool affectsArrange,
        bool affectsRender)
    {
        Name = name;
        PropertyType = propertyType;
        OwnerType = ownerType;
        DefaultValue = defaultValue;
        IsAttached = isAttached;
        Inherits = inherits;
        AffectsMeasure = affectsMeasure;
        AffectsArrange = affectsArrange;
        AffectsRender = affectsRender;
    }

    /// <summary>The CLR name of the property.</summary>
    public string Name { get; }

    /// <summary>The type of the stored value.</summary>
    public Type PropertyType { get; }

    /// <summary>The type that registered the property.</summary>
    public Type OwnerType { get; }

    /// <summary>The value read when no layer and no ancestor supplies one.</summary>
    public object? DefaultValue { get; }

    /// <summary>True when the property is read through static accessors on any <see cref="IWinformsObject"/>.</summary>
    public bool IsAttached { get; }

    /// <summary>True when an ancestor's value is used when this object has no layer.</summary>
    public bool Inherits { get; }

    /// <summary>True when a change invalidates measure.</summary>
    public bool AffectsMeasure { get; }

    /// <summary>True when a change invalidates arrange.</summary>
    public bool AffectsArrange { get; }

    /// <summary>True when a change invalidates the visual.</summary>
    public bool AffectsRender { get; }

    internal void RegisterCore()
    {
        var key = $"{OwnerType.FullName}.{Name}";
        lock (RegistryLock)
        {
            if (Registry.ContainsKey(key))
            {
                throw new InvalidOperationException($"'{OwnerType.FullName}' already registered a property named '{Name}'.");
            }

            Registry.Add(key, this);
        }
    }

    internal object? CoerceValue(IWinformsObject owner, object? value)
    {
        if (value is null)
        {
            if (PropertyType.IsValueType && Nullable.GetUnderlyingType(PropertyType) is null)
            {
                throw new ArgumentException(
                    $"Property '{Name}' cannot be set to null because its type '{PropertyType}' is a non-nullable value type.",
                    nameof(value));
            }

            return null;
        }

        if (PropertyType.IsInstanceOfType(value)
            || Nullable.GetUnderlyingType(PropertyType)?.IsInstanceOfType(value) == true)
        {
            return value;
        }

        return XamlRuntime.ConvertValue(value, PropertyType);
    }

    /// <inheritdoc />
    public override string ToString() => $"{OwnerType.Name}.{Name}";

    /// <summary>Registers a typed property owned by <typeparamref name="TOwner"/>.</summary>
    /// <typeparam name="TOwner">The registering type.</typeparam>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="name">The property name; unique for the owner.</param>
    /// <param name="defaultValue">The value used when no layer is set.</param>
    /// <param name="inherits">Whether a missing value falls back to an ancestor.</param>
    /// <param name="affectsMeasure">Whether a change invalidates measure.</param>
    /// <param name="affectsArrange">Whether a change invalidates arrange.</param>
    /// <param name="affectsRender">Whether a change invalidates the visual.</param>
    /// <returns>The registered property.</returns>
    public static WinformsProperty<T> Register<TOwner, T>(
        string name,
        T defaultValue = default!,
        bool inherits = false,
        bool affectsMeasure = false,
        bool affectsArrange = false,
        bool affectsRender = false)
        => RegisterCore<TOwner, T>(name, defaultValue, isAttached: false, inherits, affectsMeasure, affectsArrange, affectsRender);

    /// <summary>Registers a typed attached property owned by <typeparamref name="TOwner"/>.</summary>
    /// <typeparam name="TOwner">The registering type.</typeparam>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="name">The property name; unique for the owner.</param>
    /// <param name="defaultValue">The value used when no layer is set.</param>
    /// <param name="affectsMeasure">Whether a change invalidates measure.</param>
    /// <param name="affectsArrange">Whether a change invalidates arrange.</param>
    /// <param name="affectsRender">Whether a change invalidates the visual.</param>
    /// <returns>The registered property.</returns>
    public static WinformsProperty<T> RegisterAttached<TOwner, T>(
        string name,
        T defaultValue = default!,
        bool affectsMeasure = false,
        bool affectsArrange = false,
        bool affectsRender = false)
        => RegisterCore<TOwner, T>(name, defaultValue, isAttached: true, inherits: false, affectsMeasure, affectsArrange, affectsRender);

    private static WinformsProperty<T> RegisterCore<TOwner, T>(
        string name,
        T defaultValue,
        bool isAttached,
        bool inherits,
        bool affectsMeasure,
        bool affectsArrange,
        bool affectsRender)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A property needs a name.", nameof(name));
        }

        var property = new WinformsProperty<T>(
            name,
            typeof(TOwner),
            defaultValue,
            isAttached,
            inherits,
            affectsMeasure,
            affectsArrange,
            affectsRender);
        property.RegisterCore();
        return property;
    }
}

/// <summary>A typed <see cref="WinformsProperty"/>.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class WinformsProperty<T> : WinformsProperty
{
    internal WinformsProperty(
        string name,
        Type ownerType,
        T defaultValue,
        bool isAttached,
        bool inherits,
        bool affectsMeasure,
        bool affectsArrange,
        bool affectsRender)
        : base(
            name,
            typeof(T),
            ownerType,
            defaultValue,
            isAttached,
            inherits,
            affectsMeasure,
            affectsArrange,
            affectsRender)
    {
    }

    /// <summary>Reads this property from <paramref name="owner"/>.</summary>
    /// <param name="owner">The object to read.</param>
    public T GetValue(IWinformsObject owner)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        return (T)owner.GetValue(this)!;
    }

    /// <summary>Writes this property on <paramref name="owner"/> at the local priority.</summary>
    /// <param name="owner">The object to write.</param>
    /// <param name="value">The new value.</param>
    /// <param name="priority">The priority layer.</param>
    public void SetValue(IWinformsObject owner, T value, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        owner.SetValue(this, value, priority);
    }

    /// <summary>Removes the value written at <paramref name="priority"/>.</summary>
    /// <param name="owner">The object to clear.</param>
    /// <param name="priority">The priority layer to remove.</param>
    public void ClearValue(IWinformsObject owner, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        owner.ClearValue(this, priority);
    }

    /// <summary>Binds an observable to this property on <paramref name="owner"/>.</summary>
    /// <param name="owner">The object to write.</param>
    /// <param name="source">The value stream.</param>
    /// <param name="priority">The priority layer the subscription owns.</param>
    /// <returns>A subscription that clears the layer when disposed.</returns>
    public IDisposable Bind(IWinformsObject owner, IObservable<T?> source, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        return owner.Bind(this, source, priority);
    }

    /// <summary>Returns an observable for this property on <paramref name="owner"/>.</summary>
    /// <param name="owner">The object to observe.</param>
    public IObservable<T?> GetObservable(IWinformsObject owner)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        return owner.GetObservable(this);
    }
}

/// <summary>The base implementation of <see cref="IWinformsObject"/>.</summary>
public class WinformsObject : IWinformsObject, INotifyPropertyChanged
{
    private readonly Dictionary<WinformsProperty, SortedDictionary<BindingPriority, object?>> _values = [];
    private PropertyChangedEventHandler? _clrPropertyChanged;

    /// <summary>Raised after the effective value of a property changes.</summary>
    public event EventHandler<WinformsPropertyChangedEventArgs>? PropertyChanged;

    /// <inheritdoc />
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _clrPropertyChanged += value;
        remove => _clrPropertyChanged -= value;
    }

    /// <inheritdoc />
    public object? GetValue(WinformsProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (_values.TryGetValue(property, out var layers) && layers.Count > 0)
        {
            foreach (var layer in layers)
            {
                return layer.Value;
            }
        }

        if (property.Inherits && GetInheritedValue(property) is { } inherited)
        {
            return inherited;
        }

        return property.DefaultValue;
    }

    /// <summary>Reads a typed property from this object.</summary>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="property">The property to read.</param>
    public T GetValue<T>(WinformsProperty<T> property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        return (T)GetValue((WinformsProperty)property)!;
    }

    /// <inheritdoc />
    public void SetValue(WinformsProperty property, object? value, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        var oldValue = GetValue(property);
        value = property.CoerceValue(this, value);

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

    /// <summary>Writes a typed property on this object.</summary>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="property">The property to write.</param>
    /// <param name="value">The new value.</param>
    /// <param name="priority">The priority layer.</param>
    public void SetValue<T>(WinformsProperty<T> property, T value, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        SetValue((WinformsProperty)property, value, priority);
    }

    /// <inheritdoc />
    public void ClearValue(WinformsProperty property, BindingPriority priority = BindingPriority.LocalValue)
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
    public bool IsSet(WinformsProperty property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        return _values.TryGetValue(property, out var layers) && layers.Count > 0;
    }

    /// <inheritdoc />
    public IDisposable Bind<T>(WinformsProperty<T> property, IObservable<T?> source, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (source is null) throw new ArgumentNullException(nameof(source));
        var observer = new BindingObserver<T>(this, property, priority);
        var subscription = source.Subscribe(observer);
        return new BindingSubscription(subscription, this, property, priority);
    }

    /// <inheritdoc />
    public IObservable<T?> GetObservable<T>(WinformsProperty<T> property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        return new PropertyObservable<T>(this, property);
    }

    /// <summary>Resolves a value inherited from an ancestor.</summary>
    /// <param name="property">The property whose value is missing.</param>
    /// <returns>The ancestor value, or <see langword="null"/> when there is none.</returns>
    protected virtual object? GetInheritedValue(WinformsProperty property) => null;

    /// <summary>Called before <see cref="PropertyChanged"/> and <see cref="INotifyPropertyChanged"/> are raised.</summary>
    /// <param name="e">The change data.</param>
    protected virtual void OnPropertyChanged(WinformsPropertyChangedEventArgs e)
    {
    }

    /// <summary>Raises the change notifications for a property whose effective value changed.</summary>
    /// <param name="property">The changed property.</param>
    /// <param name="oldValue">The previous effective value.</param>
    /// <param name="newValue">The new effective value.</param>
    /// <param name="priority">The priority layer that changed.</param>
    protected void RaisePropertyChanged(
        WinformsProperty property,
        object? oldValue,
        object? newValue,
        BindingPriority priority = BindingPriority.LocalValue)
    {
        var args = new WinformsPropertyChangedEventArgs(property, oldValue, newValue, priority);
        OnPropertyChanged(args);
        PropertyChanged?.Invoke(this, args);
        _clrPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property.Name));
    }

    private sealed class BindingObserver<T> : IObserver<T?>
    {
        private readonly WinformsObject _owner;
        private readonly WinformsProperty<T> _property;
        private readonly BindingPriority _priority;

        public BindingObserver(WinformsObject owner, WinformsProperty<T> property, BindingPriority priority)
        {
            _owner = owner;
            _property = property;
            _priority = priority;
        }

        public void OnNext(T? value)
        {
            if (value is null && typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null)
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
        private readonly WinformsProperty _property;
        private readonly BindingPriority _priority;
        private bool _disposed;

        public BindingSubscription(
            IDisposable subscription,
            WinformsObject owner,
            WinformsProperty property,
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
        private readonly WinformsProperty<T> _property;

        public PropertyObservable(WinformsObject owner, WinformsProperty<T> property)
        {
            _owner = owner;
            _property = property;
        }

        public IDisposable Subscribe(IObserver<T?> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));
            observer.OnNext(_property.GetValue(_owner));

            void Handler(object? sender, WinformsPropertyChangedEventArgs args)
            {
                if (ReferenceEquals(args.Property, _property))
                {
                    observer.OnNext(_property.GetValue(_owner));
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
