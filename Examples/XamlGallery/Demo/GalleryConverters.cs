namespace Demo;

/// <summary>Uppercases a bound string in the binding section.</summary>
public sealed class UppercaseConverter : VeloxDev.WinForms.Xaml.IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType) => value?.ToString()?.ToUpperInvariant();

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type sourceType) => value?.ToString();
}

/// <summary>Static values used by the <c>x:Static</c> example.</summary>
public static class GalleryStatics
{
    /// <summary>The sample text read through <c>{x:Static}</c>.</summary>
    public static string SampleText => "From x:Static";
}
