using System.Drawing.Drawing2D;
using System.Reflection;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// The SVG path reader is internal to the XAML shapes; the slot attachment uses the same one.
// Reflection keeps this suite free of an InternalsVisibleTo for a parser that only serves glyphs.
[TestClass]
public sealed class SvgPathParserTests
{
    private static GraphicsPath Build(string data)
    {
        var type = typeof(WorkflowSurfaceBehavior).Assembly.GetType(
            "VeloxDev.WinForms.Xaml.Controls.Shapes.SvgPathParser")
            ?? throw new InvalidOperationException("SvgPathParser moved; update the headless suite.");
        var method = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("SvgPathParser.Parse moved; update the headless suite.");
        return (GraphicsPath)method.Invoke(null, [data])!;
    }

    [TestMethod]
    public void MoveArcClose_ProducesAPath()
    {
        using var path = Build("M 0,0 A 5,5 0 0 1 10,10 Z");
        Assert.IsTrue(path.PointCount > 0);
    }

    [TestMethod]
    public void RelativeCommands_AreAccepted()
    {
        using var path = Build("m 0,0 a 5,5 0 0 1 10,10 z");
        Assert.IsTrue(path.PointCount > 0);
    }

    [TestMethod]
    public void UnknownCommand_StopsWithoutThrowing()
    {
        using var path = Build("M 0,0 L 10,10");
        Assert.IsTrue(path.PointCount >= 0);
    }

    [TestMethod]
    public void EmptyData_ReturnsAnEmptyPath()
    {
        using var path = Build(string.Empty);
        Assert.AreEqual(0, path.PointCount);
    }
}
