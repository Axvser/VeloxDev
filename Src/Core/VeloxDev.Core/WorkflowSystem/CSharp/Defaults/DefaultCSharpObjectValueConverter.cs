using System.ComponentModel;
using System.Globalization;

namespace VeloxDev.WorkflowSystem.CSharp;

public sealed class DefaultCSharpObjectValueConverter : ICSharpObjectValueConverter
{
    public bool TryConvert(
        string value,
        Type targetType,
        object? parameter,
        out object? result)
    {
        result = null;
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var actualType = nullableType ?? targetType;

        try
        {
            if (nullableType is not null && string.IsNullOrEmpty(value))
            {
                result = null;
                return true;
            }

            if (actualType == typeof(string) || actualType == typeof(object))
            {
                result = value;
                return true;
            }

            if (actualType.IsEnum)
            {
                result = Enum.Parse(actualType, value, true);
                return true;
            }

            if (actualType == typeof(Guid))
            {
                result = Guid.Parse(value);
                return true;
            }

            if (actualType == typeof(TimeSpan))
            {
                result = TimeSpan.Parse(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (actualType == typeof(DateTimeOffset))
            {
                result = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (actualType == typeof(Uri))
            {
                result = new Uri(value, UriKind.RelativeOrAbsolute);
                return true;
            }

            if (actualType == typeof(Type))
            {
                result = CSharpObjectTypeTool.ResolveType(value);
                return result is not null;
            }

            var converter = TypeDescriptor.GetConverter(actualType);
            if (converter.CanConvertFrom(typeof(string)))
            {
                result = converter.ConvertFrom(
                    null,
                    CultureInfo.InvariantCulture,
                    value);
                return true;
            }

            result = Convert.ChangeType(
                value,
                actualType,
                CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }
}
