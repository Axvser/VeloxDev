using System.Drawing.Drawing2D;
using System.Reflection;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// SvgPathParser 是 internal：它只服务插槽图形，不值得为它开 InternalsVisibleTo。
// 这里用反射走 BuildPath，验 M/m、A/a、Z 三种命令和未知命令的「停而不抛」。
[TestClass]
public sealed class SvgPathParserTests
{
    private static GraphicsPath Build(string data)
    {
        var type = typeof(WorkflowSurfaceBehavior).Assembly.GetType(
            "VeloxDev.WorkflowSystem.AttachedBehaviors.SvgPathParser")
            ?? throw new InvalidOperationException("SvgPathParser moved; update the headless suite.");
        var method = type.GetMethod("BuildPath", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("SvgPathParser.BuildPath moved; update the headless suite.");
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
