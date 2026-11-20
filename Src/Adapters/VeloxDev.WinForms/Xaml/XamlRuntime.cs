using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Runtime helpers used by generated XAML code: value conversion, string parsing for common WinForms value types,
/// and resource lookup.
/// </summary>
/// <remarks>
/// Everything here is public because generated code calls it; it is not intended as an application-facing API.
/// </remarks>
public static class XamlRuntime
{
    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="targetType"/>, supporting the primitive types, enums,
    /// <see cref="System.Drawing.Color"/>, <see cref="Padding"/>, <see cref="Size"/>, <see cref="SizeF"/>,
    /// <see cref="System.Drawing.Point"/>, <see cref="System.Drawing.PointF"/> and <see cref="Controls.Thickness"/>.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The requested type.</param>
    /// <returns>The converted value, or <see langword="null"/> for a null reference or nullable target.</returns>
    /// <exception cref="InvalidCastException">The value cannot be converted.</exception>
    public static object? ConvertValue(object? value, Type targetType)
    {
        if (targetType is null) throw new ArgumentNullException(nameof(targetType));

        var underlying = Nullable.GetUnderlyingType(targetType);
        if (value is null)
        {
            if (underlying is not null || !targetType.IsValueType)
            {
                return null;
            }

            return Activator.CreateInstance(targetType);
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (underlying is not null)
        {
            return ConvertValue(value, underlying);
        }

        if (targetType == typeof(string))
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        if (targetType.IsEnum)
        {
            return value is string text ? ParseEnum(targetType, text) : Enum.ToObject(targetType, value);
        }

        if (targetType == typeof(System.Drawing.Color))
        {
            return value is string color ? ParseColor(color) : value;
        }

        if (targetType == typeof(Padding))
        {
            return value is string padding ? ParsePadding(padding) : value;
        }

        if (targetType == typeof(Controls.Thickness))
        {
            return value switch
            {
                string thickness => ParseThickness(thickness),
                Padding padding => Controls.Thickness.FromPadding(padding),
                IConvertible convertible => new Controls.Thickness(
                    Convert.ToSingle(convertible, CultureInfo.InvariantCulture)),
                _ => value,
            };
        }

        if (targetType == typeof(Size))
        {
            return value is string size ? ParseSize(size) : value;
        }

        if (targetType == typeof(SizeF))
        {
            return value is string sizeF ? ParseSizeF(sizeF) : value;
        }

        if (targetType == typeof(System.Drawing.Point))
        {
            return value is string point ? ParsePoint(point) : value;
        }

        if (targetType == typeof(System.Drawing.PointF))
        {
            return value is string pointF ? ParsePointF(pointF) : value;
        }

        if (targetType == typeof(TimeSpan))
        {
            return value is string span ? TimeSpan.Parse(span, CultureInfo.InvariantCulture) : value;
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
        {
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        var converter = TypeDescriptor.GetConverter(targetType);
        if (converter.CanConvertFrom(value.GetType()))
        {
            return converter.ConvertFrom(null, CultureInfo.InvariantCulture, value);
        }

        throw new InvalidCastException($"Cannot convert '{value}' ({value.GetType()}) to '{targetType}'.");
    }

    /// <summary>Parses a named or hexadecimal color: <c>#RGB</c>, <c>#RRGGBB</c>, <c>#AARRGGBB</c> or a known name.</summary>
    /// <param name="text">The color text.</param>
    public static System.Drawing.Color ParseColor(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("A color value cannot be empty.");
        }

        text = text.Trim();
        if (text[0] == '#')
        {
            var hex = text.Substring(1);
            if (hex.Length == 3)
            {
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            }

            if (hex.Length == 6)
            {
                var rgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return System.Drawing.Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }

            if (hex.Length == 8)
            {
                var argb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return System.Drawing.Color.FromArgb(
                    (int)((argb >> 24) & 0xFF),
                    (int)((argb >> 16) & 0xFF),
                    (int)((argb >> 8) & 0xFF),
                    (int)(argb & 0xFF));
            }

            throw new FormatException($"'{text}' is not a hexadecimal color with 3, 6 or 8 digits.");
        }

        var named = System.Drawing.Color.FromName(text);
        if (named.IsKnownColor || named.IsSystemColor || named.IsNamedColor)
        {
            return named;
        }

        throw new FormatException($"'{text}' is not a known color.");
    }

    /// <summary>Parses an enum value by name, comma-separated flags or underlying number.</summary>
    /// <param name="enumType">The enum type.</param>
    /// <param name="text">The value text.</param>
    public static object ParseEnum(Type enumType, string text)
    {
        if (enumType is null) throw new ArgumentNullException(nameof(enumType));
        text = text.Trim();

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return Enum.ToObject(enumType, number);
        }

        try
        {
            return Enum.Parse(enumType, text, ignoreCase: false);
        }
        catch (ArgumentException)
        {
            return Enum.Parse(enumType, text, ignoreCase: true);
        }
    }

    /// <summary>Parses 1, 2 or 4 comma-separated integers into a <see cref="Padding"/>.</summary>
    /// <param name="text">The padding text.</param>
    public static Padding ParsePadding(string text)
    {
        var parts = SplitNumbers(text);
        return parts.Length switch
        {
            1 => new Padding(parts[0]),
            2 => new Padding(parts[0], parts[1], parts[0], parts[1]),
            4 => new Padding(parts[0], parts[1], parts[2], parts[3]),
            _ => throw new FormatException($"'{text}' is not a padding value (1, 2 or 4 integers)."),
        };
    }

    /// <summary>Parses 1, 2 or 4 comma-separated numbers into a <see cref="Controls.Thickness"/>.</summary>
    /// <param name="text">The thickness text.</param>
    public static Controls.Thickness ParseThickness(string text) => Controls.Thickness.Parse(text);

    /// <summary>Parses two comma-separated integers into a <see cref="Size"/>.</summary>
    /// <param name="text">The size text.</param>
    public static Size ParseSize(string text)
    {
        var parts = SplitNumbers(text);
        return parts.Length == 2
            ? new Size(parts[0], parts[1])
            : throw new FormatException($"'{text}' is not a size value (2 integers).");
    }

    /// <summary>Parses two comma-separated numbers into a <see cref="SizeF"/>.</summary>
    /// <param name="text">The size text.</param>
    public static SizeF ParseSizeF(string text)
    {
        var parts = SplitFloats(text);
        return parts.Length == 2
            ? new SizeF(parts[0], parts[1])
            : throw new FormatException($"'{text}' is not a size value (2 numbers).");
    }

    /// <summary>Parses two comma-separated integers into a <see cref="System.Drawing.Point"/>.</summary>
    /// <param name="text">The point text.</param>
    public static System.Drawing.Point ParsePoint(string text)
    {
        var parts = SplitNumbers(text);
        return parts.Length == 2
            ? new System.Drawing.Point(parts[0], parts[1])
            : throw new FormatException($"'{text}' is not a point value (2 integers).");
    }

    /// <summary>Parses two comma-separated numbers into a <see cref="System.Drawing.PointF"/>.</summary>
    /// <param name="text">The point text.</param>
    public static System.Drawing.PointF ParsePointF(string text)
    {
        var parts = SplitFloats(text);
        return parts.Length == 2
            ? new System.Drawing.PointF(parts[0], parts[1])
            : throw new FormatException($"'{text}' is not a point value (2 numbers).");
    }

    /// <summary>Returns the resource stored under <paramref name="key"/> in <paramref name="scope"/> or an ancestor.</summary>
    /// <typeparam name="T">The type the resource is converted to.</typeparam>
    /// <param name="scope">The control whose resource chain is searched.</param>
    /// <param name="key">The resource key.</param>
    public static T GetResource<T>(VeloxDev.WinForms.Xaml.Controls.Control scope, object key)
    {
        if (scope is null) throw new ArgumentNullException(nameof(scope));

        var value = scope.FindResource(key)
            ?? throw new InvalidOperationException($"No resource named '{key}' was found in the XAML control tree.");

        var converted = ConvertValue(value, typeof(T));
        return converted is null ? default! : (T)converted;
    }

    /// <summary>
    /// Converts <paramref name="value"/> to the attached property's declared type and stores it on
    /// <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The control receiving the value.</param>
    /// <param name="property">The attached dependency property.</param>
    /// <param name="value">The value to convert and store.</param>
    /// <remarks>Generated XAML uses this for attached-property literals and static resources, where the value type is not known to the compiler.</remarks>
    public static void SetAttachedValue(System.Windows.Forms.Control target, DependencyProperty property, object? value)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));

        property.SetValue(target, ConvertValue(value, property.PropertyType));
    }

    /// <summary>Finds a registered attached property, or throws when the owner never registered it.</summary>
    /// <param name="ownerType">The type that registered the attached property.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The registered <see cref="DependencyProperty"/>.</returns>
    /// <exception cref="InvalidOperationException">No such attached property is registered.</exception>
    public static DependencyProperty RequireAttached(Type ownerType, string name)
    {
        if (ownerType is null) throw new ArgumentNullException(nameof(ownerType));

        // typeof(T) 不会触发 T 的静态构造，而 [XamlAttachedProperty] 的注册正发生在生成的静态字段初始化器里。
        // 手动跑一次类型初始化，注册表里才会有这条属性。
        RuntimeHelpers.RunClassConstructor(ownerType.TypeHandle);

        return DependencyProperty.Lookup(ownerType, name)
            ?? throw new InvalidOperationException(
                $"The attached dependency property '{ownerType.Name}.{name}' is not registered. " +
                "Declare a static {name}Property field or a [XamlAttachedProperty] accessor on the owner type.");
    }

    private static readonly ConditionalWeakTable<System.Windows.Forms.Control, NativeStyleState> NativeStyles = new();
    private static readonly ConditionalWeakTable<System.Windows.Forms.Control, NativeStyleResourceState> NativeStyleResources = new();

    /// <summary>
    /// Applies <paramref name="style"/> to a control. A <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> receives it through its explicit
    /// <see cref="VeloxDev.WinForms.Xaml.Controls.Control.Style"/>; a native control receives the style's native property setters only.
    /// </summary>
    /// <param name="target">The control receiving the style.</param>
    /// <param name="style">The style, or <see langword="null"/> to remove the previous native style.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The style target type does not match or a native property is missing.</exception>
    public static void ApplyStyle(System.Windows.Forms.Control target, Style? style)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.Style = style;
            return;
        }

        RemoveNativeStyleResource(target);
        ApplyNativeStyleOnly(target, style);
    }

    /// <summary>
    /// Applies a style stored under <paramref name="key"/> in <paramref name="scope"/> and keeps following that
    /// resource for native and XAML controls: replacing it reapplies the new style, removing it clears the style.
    /// </summary>
    /// <param name="target">The control receiving the style.</param>
    /// <param name="scope">The Control whose resources contain the style key.</param>
    /// <param name="key">The resource key.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void ApplyStyleResource(System.Windows.Forms.Control target, VeloxDev.WinForms.Xaml.Controls.Control scope, object key)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (scope is null) throw new ArgumentNullException(nameof(scope));
        if (key is null) throw new ArgumentNullException(nameof(key));

        if (target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            xaml.ApplyStyleResource(scope, key);
            return;
        }

        RemoveNativeStyleResource(target);
        var state = new NativeStyleResourceState(target, scope, key);
        NativeStyleResources.Add(target, state);
        scope.Resources.Changed += state.Handler;
        state.Apply();
    }

    private static void RemoveNativeStyleResource(System.Windows.Forms.Control target)
    {
        if (NativeStyleResources.TryGetValue(target, out var state))
        {
            state.Scope.Resources.Changed -= state.Handler;
            NativeStyleResources.Remove(target);
        }
    }

    private static void ApplyNativeStyleOnly(System.Windows.Forms.Control target, Style? style)
    {
        if (style is null)
        {
            if (NativeStyles.TryGetValue(target, out var existing))
            {
                UnapplyNativeStyle(target, existing);
                NativeStyles.Remove(target);
            }

            return;
        }

        ValidateNativeStyle(target, style);
        if (NativeStyles.TryGetValue(target, out var current))
        {
            UnapplyNativeStyle(target, current);
            NativeStyles.Remove(target);
        }

        var state = new NativeStyleState(style);
        NativeStyles.Add(target, state);
        try
        {
            ApplyNativeStyle(target, state);
        }
        catch
        {
            UnapplyNativeStyle(target, state);
            NativeStyles.Remove(target);
            throw;
        }
    }

    /// <summary>Re-applies a native control's current style after the style object changed.</summary>
    /// <param name="target">The native control whose style changed.</param>
    internal static void ReapplyNativeStyle(System.Windows.Forms.Control target)
    {
        if (!NativeStyles.TryGetValue(target, out var state))
        {
            return;
        }

        ValidateNativeStyle(target, state.Style);
        UnapplyNativeStyle(target, state);
        ApplyNativeStyle(target, state);
    }

    private static void ValidateNativeStyle(System.Windows.Forms.Control target, Style style)
    {
        if (style.TargetType is null)
        {
            throw new InvalidOperationException("Style.TargetType is required.");
        }

        if (!style.TargetType.IsInstanceOfType(target))
        {
            throw new InvalidOperationException(
                $"Style TargetType '{style.TargetType}' does not apply to '{target.GetType()}'.");
        }

        _ = ResolveStyleChain(style);
    }

    private static List<Style> ResolveStyleChain(Style style)
    {
        var chain = new List<Style>();
        var seen = new HashSet<Style>();
        for (var current = style; current is not null; current = current.BasedOn)
        {
            if (!seen.Add(current))
            {
                throw new InvalidOperationException("A Style BasedOn chain contains a cycle.");
            }

            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }

    private static void ApplyNativeStyle(System.Windows.Forms.Control target, NativeStyleState state)
    {
        var chain = ResolveStyleChain(state.Style);
        state.Chain.AddRange(chain);
        foreach (var style in chain)
        {
            style.RegisterTarget(target);
        }

        var nativeSetters = new Dictionary<string, Setter>(StringComparer.Ordinal);
        var dependencySetters = new Dictionary<DependencyProperty, Setter>();
        foreach (var style in chain)
        {
            foreach (var setter in style.Setters)
            {
                if (setter is null)
                {
                    continue;
                }

                if (setter.Property is { } property)
                {
                    dependencySetters[property] = setter;
                }
                else if (!string.IsNullOrEmpty(setter.PropertyName))
                {
                    nativeSetters[setter.PropertyName!] = setter;
                }
            }
        }

        foreach (var pair in nativeSetters)
        {
            var descriptor = TypeDescriptor.GetProperties(target).Find(pair.Key, ignoreCase: false)
                ?? throw new InvalidOperationException(
                    $"The native property '{pair.Key}' was not found on '{target.GetType()}'.");

            if (descriptor.IsReadOnly)
            {
                throw new InvalidOperationException($"The native property '{pair.Key}' is read-only.");
            }

            var oldValue = descriptor.GetValue(target);
            descriptor.SetValue(target, ConvertValue(pair.Value.Value, descriptor.PropertyType));
            state.Restore.Add(() => descriptor.SetValue(target, oldValue));
        }

        foreach (var pair in dependencySetters)
        {
            var property = pair.Key;
            var oldValue = property.GetValue(target);
            property.SetValue(target, property.CoerceValue(target, pair.Value.Value));
            state.Restore.Add(() => property.SetValue(target, oldValue));
        }
    }

    private static void UnapplyNativeStyle(System.Windows.Forms.Control target, NativeStyleState state)
    {
        for (var index = state.Restore.Count - 1; index >= 0; index--)
        {
            state.Restore[index]();
        }

        state.Restore.Clear();

        foreach (var style in state.Chain)
        {
            style.UnregisterTarget(target);
        }

        state.Chain.Clear();
    }

    private sealed class NativeStyleState
    {
        public NativeStyleState(Style style)
        {
            Style = style;
        }

        public Style Style { get; }

        public List<Style> Chain { get; } = [];

        public List<Action> Restore { get; } = [];
    }

    private sealed class NativeStyleResourceState
    {
        private readonly WeakReference<System.Windows.Forms.Control> _target;

        public NativeStyleResourceState(System.Windows.Forms.Control target, VeloxDev.WinForms.Xaml.Controls.Control scope, object key)
        {
            _target = new WeakReference<System.Windows.Forms.Control>(target);
            Scope = scope;
            Key = key;
            Handler = OnChanged;
        }

        public VeloxDev.WinForms.Xaml.Controls.Control Scope { get; }

        public object Key { get; }

        public Action<object?> Handler { get; }

        public void Apply()
        {
            if (!_target.TryGetTarget(out var target))
            {
                return;
            }

            if (Scope.Resources.TryGetValue(Key, out var value) && value is Style style)
            {
                ApplyNativeStyleOnly(target, style);
            }
            else
            {
                ApplyNativeStyleOnly(target, null);
            }
        }

        private void OnChanged(object? changedKey)
        {
            if (!_target.TryGetTarget(out _))
            {
                Scope.Resources.Changed -= Handler;
                return;
            }

            if (changedKey is null || Equals(changedKey, Key))
            {
                Apply();
            }
        }
    }

    private static int[] SplitNumbers(string text)
    {
        var pieces = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var values = new int[pieces.Length];
        for (var index = 0; index < pieces.Length; index++)
        {
            values[index] = int.Parse(pieces[index], NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static float[] SplitFloats(string text)
    {
        var pieces = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var values = new float[pieces.Length];
        for (var index = 0; index < pieces.Length; index++)
        {
            values[index] = float.Parse(pieces[index], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return values;
    }
}
