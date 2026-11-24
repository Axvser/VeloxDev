using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public class MinimapDrawContextTests
{
    [TestMethod]
    public void GetMinimapRect_PlacesTopRightInsideAvailableCorner()
    {
        var context = new MinimapDrawContext
        {
            MinimapWidth = 200d,
            MinimapHeight = 140d,
            MinimapMargin = 12d,
            AvailableRight = 800d,
            AvailableTop = 36d,
            AvailableBottom = 560d,
        };

        var rect = context.GetMinimapRect(minimumSize: 20d);

        Assert.AreEqual(588d, rect.Left, 0.001d);
        Assert.AreEqual(48d, rect.Top, 0.001d);
        Assert.AreEqual(200d, rect.Width, 0.001d);
        Assert.AreEqual(140d, rect.Height, 0.001d);
    }

    [TestMethod]
    public void GetMinimapRect_ClampsToAvailableArea()
    {
        var context = new MinimapDrawContext
        {
            MinimapWidth = 200d,
            MinimapHeight = 140d,
            MinimapMargin = 12d,
            AvailableRight = 150d,
            AvailableTop = 0d,
            AvailableBottom = 150d,
        };

        var rect = context.GetMinimapRect(minimumSize: 20d);

        Assert.AreEqual(126d, rect.Width, 0.001d);
        Assert.AreEqual(126d, rect.Height, 0.001d);
        Assert.AreEqual(12d, rect.Left, 0.001d);
        Assert.AreEqual(12d, rect.Top, 0.001d);
    }

    [TestMethod]
    public void GetMinimapRect_FallsBackToOriginWithoutAvailableArea()
    {
        var context = new MinimapDrawContext
        {
            MinimapWidth = 200d,
            MinimapHeight = 140d,
        };

        var rect = context.GetMinimapRect(minimumSize: 20d);

        Assert.AreEqual(0d, rect.Left, 0.001d);
        Assert.AreEqual(0d, rect.Top, 0.001d);
        Assert.AreEqual(200d, rect.Width, 0.001d);
        Assert.AreEqual(140d, rect.Height, 0.001d);
    }
}
