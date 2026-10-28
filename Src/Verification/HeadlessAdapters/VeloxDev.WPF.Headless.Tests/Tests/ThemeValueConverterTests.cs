using System.Windows;
using System.Windows.Media;
using VeloxDev.DynamicTheme;
using PointValueConverter = VeloxDev.DynamicTheme.PointConverter;
using ThicknessValueConverter = VeloxDev.DynamicTheme.ThicknessConverter;
using CornerRadiusValueConverter = VeloxDev.DynamicTheme.CornerRadiusConverter;
using ColorValueConverter = VeloxDev.DynamicTheme.ColorConverter;
using BrushValueConverter = VeloxDev.DynamicTheme.BrushConverter;

namespace VeloxDev.WPF.Headless.Tests.Tests;

// 主题值转换器是无状态的纯函数；不建控件、不需要 Application，覆盖它们最便宜。
[TestClass]
public sealed class ThemeValueConverterTests
{
    [TestMethod]
    public void Double_AcceptsNumbersAndInvariantStrings()
    {
        var converter = new DoubleConverter();

        Assert.AreEqual(1.5d, converter.Convert(typeof(double), "Width", ["1.5"]));
        Assert.AreEqual(2d, converter.Convert(typeof(double), "Width", [2]));
        Assert.AreEqual(3d, converter.Convert(typeof(double), "Width", [3f]));
        Assert.IsNull(converter.Convert(typeof(double), "Width", ["not-a-number"]));
        Assert.IsNull(converter.Convert(typeof(double), "Width", []));
        Assert.IsNull(converter.Convert(typeof(double), "Width", null!));
    }

    [TestMethod]
    public void Point_AcceptsCommaStringAndTwoParameters()
    {
        var converter = new PointValueConverter();

        Assert.AreEqual(new Point(10d, 20d), converter.Convert(typeof(Point), "RenderTransformOrigin", ["10,20"]));
        Assert.AreEqual(new Point(3d, 4d), converter.Convert(typeof(Point), "RenderTransformOrigin", [3d, 4d]));
        Assert.IsNull(converter.Convert(typeof(Point), "RenderTransformOrigin", ["10"]));
        Assert.IsNull(converter.Convert(typeof(Point), "RenderTransformOrigin", []));
    }

    [TestMethod]
    public void Thickness_AcceptsUniformPairAndFour()
    {
        var converter = new ThicknessValueConverter();

        Assert.AreEqual(new Thickness(1d), converter.Convert(typeof(Thickness), "Padding", ["1"]));
        Assert.AreEqual(new Thickness(1d, 2d, 1d, 2d), converter.Convert(typeof(Thickness), "Padding", ["1,2"]));
        Assert.AreEqual(new Thickness(1d, 2d, 3d, 4d), converter.Convert(typeof(Thickness), "Padding", ["1,2,3,4"]));
        Assert.IsNull(converter.Convert(typeof(Thickness), "Padding", ["nope"]));
    }

    [TestMethod]
    public void CornerRadius_AcceptsUniformPairAndFour()
    {
        var converter = new CornerRadiusValueConverter();

        Assert.AreEqual(new CornerRadius(1d), converter.Convert(typeof(CornerRadius), "CornerRadius", ["1"]));
        // 这个转换器把 "1,2" 当单个数字 12（逗号被当千位分隔/移除）；四段才是四角。
        Assert.AreEqual(new CornerRadius(12d), converter.Convert(typeof(CornerRadius), "CornerRadius", ["1,2"]));
        Assert.AreEqual(new CornerRadius(1d, 2d, 3d, 4d), converter.Convert(typeof(CornerRadius), "CornerRadius", ["1,2,3,4"]));
        Assert.IsNull(converter.Convert(typeof(CornerRadius), "CornerRadius", ["nope"]));
    }

    [TestMethod]
    public void ColorAndBrush_ParseArgbHex()
    {
        var expected = Color.FromArgb(0xFF, 0x10, 0x20, 0x30);

        var color = (Color?)new ColorValueConverter().Convert(typeof(Color), "Background", ["#FF102030"]);
        Assert.AreEqual(expected, color);

        var brush = (SolidColorBrush?)new BrushValueConverter().Convert(typeof(Brush), "Background", ["#FF102030"]);
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
        Assert.IsNull(new BrushValueConverter().Convert(typeof(Brush), "P", null!));
    }
}
