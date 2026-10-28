using System.Drawing;
using VeloxDev.DynamicTheme;
using PointValueConverter = VeloxDev.DynamicTheme.PointConverter;
using SizeValueConverter = VeloxDev.DynamicTheme.SizeConverter;
using SizeFValueConverter = VeloxDev.DynamicTheme.SizeFConverter;
using RectangleValueConverter = VeloxDev.DynamicTheme.RectangleConverter;
using PaddingValueConverter = VeloxDev.DynamicTheme.PaddingConverter;
using ColorValueConverter = VeloxDev.DynamicTheme.ColorConverter;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// WinForms 的主题值转换器全是纯函数；这里一次覆盖 12 个类型。
[TestClass]
public sealed class ThemeValueConverterTests
{
    [TestMethod]
    public void ScalarConverters_AcceptTheirNaturalInputs()
    {
        Assert.AreEqual(1.5d, new DoubleConverter().Convert(typeof(double), "P", ["1.5"]));
        Assert.AreEqual(3, new IntConverter().Convert(typeof(int), "P", ["3"]));
        Assert.AreEqual(4.25f, new FloatConverter().Convert(typeof(float), "P", ["4.25"]));
        Assert.IsNull(new DoubleConverter().Convert(typeof(double), "P", ["nope"]));
        Assert.IsNull(new IntConverter().Convert(typeof(int), "P", ["nope"]));
    }

    [TestMethod]
    public void GeometryConverters_ParsePairs()
    {
        Assert.AreEqual(new Point(10, 20), new PointValueConverter().Convert(typeof(Point), "P", ["10,20"]));
        Assert.AreEqual(new PointF(10f, 20f), new PointFConverter().Convert(typeof(PointF), "P", ["10,20"]));
        Assert.AreEqual(new Size(10, 20), new SizeValueConverter().Convert(typeof(Size), "P", ["10,20"]));
        Assert.AreEqual(new SizeF(10f, 20f), new SizeFValueConverter().Convert(typeof(SizeF), "P", ["10,20"]));
        Assert.AreEqual(new Rectangle(1, 2, 3, 4), new RectangleValueConverter().Convert(typeof(Rectangle), "P", ["1,2,3,4"]));
        Assert.AreEqual(new RectangleF(1f, 2f, 3f, 4f), new RectangleFConverter().Convert(typeof(RectangleF), "P", ["1,2,3,4"]));
    }

    [TestMethod]
    public void PaddingAndColor_ParseTheirTextForms()
    {
        Assert.AreEqual(new Padding(1), new PaddingValueConverter().Convert(typeof(Padding), "P", ["1"]));
        Assert.AreEqual(new Padding(1, 2, 3, 4), new PaddingValueConverter().Convert(typeof(Padding), "P", ["1,2,3,4"]));
        Assert.AreEqual(Color.Red, new ColorValueConverter().Convert(typeof(Color), "P", ["Red"]));
    }

    [TestMethod]
    public void Converters_ReturnNullForMissingInput()
    {
        Assert.IsNull(new PointValueConverter().Convert(typeof(Point), "P", null!));
        Assert.IsNull(new SizeValueConverter().Convert(typeof(Size), "P", null!));
        Assert.IsNull(new RectangleValueConverter().Convert(typeof(Rectangle), "P", null!));
        Assert.IsNull(new PaddingValueConverter().Convert(typeof(Padding), "P", null!));
    }
}
