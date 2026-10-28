using Avalonia;
using Avalonia.Media;
using VeloxDev.DynamicTheme;
using PointValueConverter = VeloxDev.DynamicTheme.PointConverter;
using ThicknessValueConverter = VeloxDev.DynamicTheme.ThicknessConverter;
using CornerRadiusValueConverter = VeloxDev.DynamicTheme.CornerRadiusConverter;
using ColorValueConverter = VeloxDev.DynamicTheme.ColorConverter;
using BrushValueConverter = VeloxDev.DynamicTheme.BrushConverter;

namespace VeloxDev.Avalonia.Headless.Tests.Tests;

// Avalonia 的主题值转换器与 WPF 同名但走各家的解析 API；这里把同样的输入再验一遍。
[TestClass]
public sealed class ThemeValueConverterTests
{
    [TestMethod]
    public void Double_AcceptsNumbersAndInvariantStrings()
    {
        var converter = new DoubleConverter();

        Assert.AreEqual(1.5d, converter.Convert(typeof(double), "P", ["1.5"]));
        Assert.AreEqual(2d, converter.Convert(typeof(double), "P", [2]));
        Assert.AreEqual(1d, converter.Convert(typeof(double), "P", [true]));
        Assert.IsNull(converter.Convert(typeof(double), "P", ["nope"]));
    }

    [TestMethod]
    public void Point_AcceptsCommaStringAndTwoParameters()
    {
        var converter = new PointValueConverter();

        Assert.AreEqual(new Point(10d, 20d), converter.Convert(typeof(Point), "P", ["10,20"]));
        Assert.AreEqual(new Point(3d, 4d), converter.Convert(typeof(Point), "P", [3d, 4d]));
        Assert.IsNull(converter.Convert(typeof(Point), "P", []));
    }

    [TestMethod]
    public void Thickness_AcceptsUniformPairAndFour()
    {
        var converter = new ThicknessValueConverter();

        Assert.AreEqual(new Thickness(1d), converter.Convert(typeof(Thickness), "P", ["1"]));
        Assert.AreEqual(new Thickness(1d, 2d, 1d, 2d), converter.Convert(typeof(Thickness), "P", ["1,2"]));
        Assert.AreEqual(new Thickness(1d, 2d, 3d, 4d), converter.Convert(typeof(Thickness), "P", ["1,2,3,4"]));
    }

    [TestMethod]
    public void CornerRadius_AcceptsUniformPairAndFour()
    {
        var converter = new CornerRadiusValueConverter();

        Assert.AreEqual(new CornerRadius(1d), converter.Convert(typeof(CornerRadius), "P", ["1"]));
        // Avalonia 的 CornerRadius 构造顺序是 (topLeft, topRight, bottomRight, bottomLeft)；
        // 两段输入映射成 (1,1,2,2) —— 这是 Avalonia 自己的解析口径，不是对称的四角。
        Assert.AreEqual(new CornerRadius(1d, 1d, 2d, 2d), converter.Convert(typeof(CornerRadius), "P", ["1,2"]));
        Assert.AreEqual(new CornerRadius(1d, 2d, 3d, 4d), converter.Convert(typeof(CornerRadius), "P", ["1,2,3,4"]));
    }

    [TestMethod]
    public void ColorAndBrush_ParseArgbHex()
    {
        var expected = Color.Parse("#FF102030");

        var color = (Color?)new ColorValueConverter().Convert(typeof(Color), "P", ["#FF102030"]);
        Assert.AreEqual(expected, color);

        var brush = (SolidColorBrush?)new BrushValueConverter().Convert(typeof(IBrush), "P", ["#FF102030"]);
        Assert.IsNotNull(brush);
        Assert.AreEqual(expected, brush!.Color);
    }

    [TestMethod]
    public void Converters_ReturnNullForMissingParameters()
    {
        Assert.IsNull(new PointValueConverter().Convert(typeof(Point), "P", null!));
        Assert.IsNull(new ThicknessValueConverter().Convert(typeof(Thickness), "P", null!));
        Assert.IsNull(new CornerRadiusValueConverter().Convert(typeof(CornerRadius), "P", null!));
        Assert.IsNull(new ColorValueConverter().Convert(typeof(Color), "P", null!));
        Assert.IsNull(new BrushValueConverter().Convert(typeof(IBrush), "P", null!));
    }
}
