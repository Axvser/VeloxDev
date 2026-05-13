using System.Globalization;

namespace VeloxDev.DynamicTheme
{
    public class DoubleConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                return parameters[0] switch
                {
                    double d => d,
                    int i => (double)i,
                    float f => (double)f,
                    string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) => parsed,
                    _ => System.Convert.ToDouble(parameters[0], CultureInfo.InvariantCulture)
                };
            }
            catch { return null; }
        }
    }

    public class StringConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;
            return parameters[0]?.ToString();
        }
    }

    public class IntConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            try
            {
                return parameters[0] switch
                {
                    int i => i,
                    double d => (int)d,
                    float f => (int)f,
                    string s when int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int parsed) => parsed,
                    _ => System.Convert.ToInt32(parameters[0], CultureInfo.InvariantCulture)
                };
            }
            catch { return null; }
        }
    }

    public class BoolConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            if (parameters == null || parameters.Length < 1) return null;

            return parameters[0] switch
            {
                bool b => b,
                string s when bool.TryParse(s, out bool parsed) => parsed,
                int i => i != 0,
                _ => null
            };
        }
    }
}
