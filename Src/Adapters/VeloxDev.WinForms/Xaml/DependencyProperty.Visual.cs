namespace VeloxDev.WinForms.Xaml;

// 视觉后端沿用 WPF 的属性名，但值存储由 IWinformsObject 自己的优先级表负责；这里只做转发与类型校验。
public sealed partial class DependencyProperty
{
    /// <summary>Reads this property from a visual-backend object.</summary>
    /// <param name="target">The object to read.</param>
    public object? GetValue(IWinformsObject target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return target.GetValue(this);
    }

    /// <summary>Writes this property on a visual-backend object at <paramref name="priority"/>.</summary>
    /// <param name="target">The object to write.</param>
    /// <param name="value">The new value.</param>
    /// <param name="priority">The priority layer.</param>
    public void SetValue(IWinformsObject target, object? value, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        target.SetValue(this, value, priority);
    }

    /// <summary>Removes the value written at <paramref name="priority"/>.</summary>
    /// <param name="target">The object to clear.</param>
    /// <param name="priority">The priority layer to remove.</param>
    public void ClearValue(IWinformsObject target, BindingPriority priority = BindingPriority.LocalValue)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        target.ClearValue(this, priority);
    }

    /// <summary>True when <paramref name="target"/> holds any priority layer for this property.</summary>
    /// <param name="target">The object to inspect.</param>
    public bool IsSet(IWinformsObject target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return target.IsSet(this);
    }

    internal object? CoerceFor(IWinformsObject target, object? value)
    {
        // 视觉目标没有 Control，但回调的 target 是 object：这条路径与附加属性共用同一套元数据。
        if (value is not null && !IsAssignable(value))
        {
            value = XamlRuntime.ConvertValue(value, PropertyType);
        }

        value = ValidateValueType(value);

        if (Metadata.CoerceValueCallback is { } coerce)
        {
            value = ValidateValueType(coerce(target, value));
        }

        return value;
    }
}
