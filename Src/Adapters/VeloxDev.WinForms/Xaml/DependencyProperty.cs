using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Where a write to a dependency property came from.</summary>
internal enum XamlValueSource
{
    /// <summary>A direct <c>SetValue</c> call, a CLR wrapper or a generated initializer.</summary>
    Local,
    /// <summary>The binding engine pushing a source value to the target.</summary>
    Binding,
    /// <summary>A trigger setter being applied or restored.</summary>
    Trigger,
    /// <summary>A transition frame.</summary>
    Transition,
    /// <summary>A style setter or style-trigger value.</summary>
    Style,
}

/// <summary>
/// A property registered against <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> (or attached to any <see cref="System.Windows.Forms.Control"/>) whose value
/// is stored by the runtime instead of by a backing field.
/// </summary>
/// <remarks>
/// <para>
/// The property object is also its accessor: <see cref="GetValue"/>, <see cref="SetValue"/> and
/// <see cref="ClearValue"/> take the target control. That keeps attached properties usable on arbitrary WinForms
/// controls, which have no dependency-property base class of their own.
/// </para>
/// <para>
/// A value written to a <see cref="System.Windows.Forms.Control"/> lives in that control's local store. A value written to any other
/// <see cref="System.Windows.Forms.Control"/> lives in a side table keyed weakly by the control, so attaching a property never requires
/// the control to be rebuilt or subclassed.
/// </para>
/// </remarks>
public sealed class DependencyProperty
{
    private static readonly object RegistryLock = new();
    private static readonly Dictionary<Type, Dictionary<string, DependencyProperty>> Registry = new();
    private static readonly ConditionalWeakTable<System.Windows.Forms.Control, AttachedValueStore> AttachedStores = new();

    private DependencyProperty(string name, Type propertyType, Type ownerType, PropertyMetadata metadata, bool isAttached)
    {
        Name = name;
        PropertyType = propertyType;
        OwnerType = ownerType;
        Metadata = metadata;
        IsAttached = isAttached;
    }

    /// <summary>The CLR name of the property.</summary>
    public string Name { get; }

    /// <summary>The type of the stored value.</summary>
    public Type PropertyType { get; }

    /// <summary>The type that registered the property.</summary>
    public Type OwnerType { get; }

    /// <summary>The metadata supplied at registration.</summary>
    public PropertyMetadata Metadata { get; }

    /// <summary>The value read when the target has no local value.</summary>
    public object? DefaultValue => Metadata.DefaultValue;

    /// <summary>True when the property was registered with <see cref="RegisterAttached"/>.</summary>
    public bool IsAttached { get; }

    /// <summary>
    /// Registers a property owned by <paramref name="ownerType"/>.
    /// </summary>
    /// <param name="name">The property name; must be unique for the owner type.</param>
    /// <param name="propertyType">The type of the stored value.</param>
    /// <param name="ownerType">The type that owns the property.</param>
    /// <param name="metadata">The default value and callbacks; omitted means a <c>default</c> value.</param>
    /// <returns>The registered property.</returns>
    /// <exception cref="InvalidOperationException">The owner already registered a property with this name.</exception>
    public static DependencyProperty Register(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata = null)
        => RegisterCore(name, propertyType, ownerType, metadata, isAttached: false);

    /// <summary>
    /// Registers an attached property that can be read and written on any <see cref="System.Windows.Forms.Control"/>.
    /// </summary>
    /// <param name="name">The property name; must be unique for the owner type.</param>
    /// <param name="propertyType">The type of the stored value.</param>
    /// <param name="ownerType">The type that declares the attached property.</param>
    /// <param name="metadata">The default value and callbacks; omitted means a <c>default</c> value.</param>
    /// <returns>The registered property.</returns>
    /// <exception cref="InvalidOperationException">The owner already registered a property with this name.</exception>
    public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata = null)
        => RegisterCore(name, propertyType, ownerType, metadata, isAttached: true);

    /// <summary>Finds a property registered by <paramref name="ownerType"/>, or <see langword="null"/>.</summary>
    /// <param name="ownerType">The registering type.</param>
    /// <param name="name">The property name.</param>
    public static DependencyProperty? Lookup(Type ownerType, string name)
    {
        lock (RegistryLock)
        {
            return Registry.TryGetValue(ownerType, out var byName) && byName.TryGetValue(name, out var property)
                ? property
                : null;
        }
    }

    /// <summary>Reads the local value of this property from <paramref name="target"/>, or its default value.</summary>
    /// <param name="target">The control whose value is read.</param>
    public object? GetValue(System.Windows.Forms.Control target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return target is VeloxDev.WinForms.Xaml.Controls.Control xaml
            ? xaml.GetLocalValueOrDefault(this)
            : AttachedStores.TryGetValue(target, out var store) && store.Values.TryGetValue(this, out var value)
                ? value
                : Metadata.DefaultValue;
    }

    /// <summary>Stores <paramref name="value"/> locally on <paramref name="target"/>.</summary>
    /// <param name="target">The control receiving the value.</param>
    /// <param name="value">The new value; <see langword="null"/> is allowed only for reference and nullable types.</param>
    public void SetValue(System.Windows.Forms.Control target, object? value)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        SetValueCore(target, value, XamlValueSource.Local);
    }

    /// <summary>Removes the local value from <paramref name="target"/>, restoring its default or inherited value.</summary>
    /// <param name="target">The control whose local value is removed.</param>
    public void ClearValue(System.Windows.Forms.Control target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        ClearValueCore(target, XamlValueSource.Local);
    }

    /// <summary>Returns the property name for diagnostics.</summary>
    public override string ToString() => $"{OwnerType.Name}.{Name}";

    internal void SetValueCore(System.Windows.Forms.Control target, object? value, XamlValueSource source)
    {
        value = CoerceAndValidate(target, value);

        object? oldValue;
        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            oldValue = xaml.GetLocalValueOrDefault(this);
            if (xaml.HasLocalValue(this) && Equals(oldValue, value))
            {
                return;
            }

            xaml.SetLocalValue(this, value);
        }
        else
        {
            var store = AttachedStores.GetOrCreateValue(target);
            // 第一次写入没有本地值，但回调里的 OldValue 仍应是 GetValue 会返回的默认值，而不是 null。
            oldValue = store.Values.TryGetValue(this, out var existing) ? existing : Metadata.DefaultValue;
            if (store.Values.ContainsKey(this) && Equals(oldValue, value))
            {
                return;
            }

            store.Values[this] = value;
        }

        RaiseChanged(target, oldValue, value, source);
    }

    internal void ClearValueCore(System.Windows.Forms.Control target, XamlValueSource source)
    {
        object? oldValue;
        object? newValue;

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            if (!xaml.HasLocalValue(this))
            {
                return;
            }

            oldValue = xaml.GetLocalValueOrDefault(this);
            xaml.RemoveLocalValue(this);
            newValue = xaml.GetLocalValueOrDefault(this);
        }
        else
        {
            if (!AttachedStores.TryGetValue(target, out var store) || !store.Values.TryGetValue(this, out oldValue))
            {
                return;
            }

            store.Values.Remove(this);
            newValue = Metadata.DefaultValue;
        }

        RaiseChanged(target, oldValue, newValue, source);
    }

    private object? CoerceAndValidate(System.Windows.Forms.Control target, object? value)
    {
        if (value is null && PropertyType.IsValueType && Nullable.GetUnderlyingType(PropertyType) is null)
        {
            throw new ArgumentException(
                $"Property '{Name}' cannot be set to null because its type '{PropertyType}' is a non-nullable value type.",
                nameof(value));
        }

        if (value is not null && !PropertyType.IsInstanceOfType(value))
        {
            value = XamlRuntime.ConvertValue(value, PropertyType);
        }

        if (Metadata.CoerceValueCallback is { } coerce)
        {
            value = coerce(target, value);
        }

        if (value is null && PropertyType.IsValueType && Nullable.GetUnderlyingType(PropertyType) is null)
        {
            throw new ArgumentException(
                $"The coercion of property '{Name}' produced null, which is invalid for '{PropertyType}'.",
                nameof(value));
        }

        return value;
    }

    internal object? CoerceValue(System.Windows.Forms.Control target, object? value) => CoerceAndValidate(target, value);

    internal void RaiseChanged(System.Windows.Forms.Control target, object? oldValue, object? newValue, XamlValueSource source)
    {
        var args = new DependencyPropertyChangedEventArgs(this, oldValue, newValue);
        Metadata.PropertyChangedCallback?.Invoke(target, args);
        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.OnDependencyPropertyChangedCore(this, oldValue, newValue, source);

            // 布局失效由元数据统一扇出：顺序 / 位置类属性只需声明 AffectsMeasure 或 AffectsArrange，
            // 不必在每处回调里手写 Parent?.PerformLayout()。
            if (!xaml.IsDisposed)
            {
                if (Metadata.AffectsMeasure)
                {
                    xaml.InvalidateMeasure();
                }
                else if (Metadata.AffectsArrange)
                {
                    xaml.InvalidateArrange();
                }
            }

            // 现代 GUI 的自动脏标记：依赖属性变了，画它的控件就要重画。以前只有显式写了
            // PropertyChangedCallback 的控件会 Invalidate，自定义视图的文本/颜色因此会停旧值。
            if (Metadata.AffectsRender && !xaml.IsDisposed)
            {
                xaml.Invalidate();
            }
        }
    }

    /// <summary>Raises the change callbacks only when the effective value actually changed.</summary>
    internal void RaiseChangedIfDifferent(System.Windows.Forms.Control target, object? oldValue, object? newValue, XamlValueSource source)
    {
        if (!Equals(oldValue, newValue))
        {
            RaiseChanged(target, oldValue, newValue, source);
        }
    }

    private static DependencyProperty RegisterCore(
        string name,
        Type propertyType,
        Type ownerType,
        PropertyMetadata? metadata,
        bool isAttached)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A dependency property needs a name.", nameof(name));
        if (propertyType is null) throw new ArgumentNullException(nameof(propertyType));
        if (ownerType is null) throw new ArgumentNullException(nameof(ownerType));

        metadata ??= new PropertyMetadata(
            propertyType.IsValueType ? Activator.CreateInstance(propertyType) : null);

        // 可空值类型以盒装基础值给出默认值是合法的（typeof(int?) 的默认 5 就是盒装 int）；
        // IsInstanceOfType 对这条路径返回 false，所以单独放行。
        if (metadata.DefaultValue is not null
            && !propertyType.IsInstanceOfType(metadata.DefaultValue)
            && Nullable.GetUnderlyingType(propertyType)?.IsInstanceOfType(metadata.DefaultValue) != true)
        {
            throw new ArgumentException(
                $"The default value of '{ownerType.Name}.{name}' is not assignable to '{propertyType}'.",
                nameof(metadata));
        }

        // 非可空值类型的默认值不能是 null：写入端由 CoerceAndValidate 拦截，注册端若不拦，
        // 每个 get => (T)GetValue(...)! 都会把 null 拆箱成 NullReferenceException。
        if (metadata.DefaultValue is null
            && propertyType.IsValueType
            && Nullable.GetUnderlyingType(propertyType) is null)
        {
            throw new ArgumentException(
                $"The default value of '{ownerType.Name}.{name}' cannot be null for '{propertyType}'.",
                nameof(metadata));
        }

        var property = new DependencyProperty(name, propertyType, ownerType, metadata, isAttached);

        lock (RegistryLock)
        {
            if (!Registry.TryGetValue(ownerType, out var byName))
            {
                byName = new Dictionary<string, DependencyProperty>(StringComparer.Ordinal);
                Registry.Add(ownerType, byName);
            }

            if (byName.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"'{ownerType.FullName}' already registered a dependency property named '{name}'.");
            }

            byName.Add(name, property);
        }

        return property;
    }

    private sealed class AttachedValueStore
    {
        public Dictionary<DependencyProperty, object?> Values { get; } = [];
    }
}
