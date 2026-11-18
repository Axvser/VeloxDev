using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Demo;

// 画布把缩放存成倍率（1 = 100%），状态栏显示时取倒数 —— 与画布 HUD 的口径一致。
internal sealed class ZoomPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var scale = value switch
        {
            double d => d,
            int i => i,
            _ => 1d,
        };

        return scale > 0
            ? Math.Round(100d / scale).ToString("0", CultureInfo.InvariantCulture) + "%"
            : "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("状态栏是只读的。");
}
