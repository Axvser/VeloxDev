using System.Drawing;
using VeloxDev.WinForms.Xaml.Visuals;
using V = VeloxDev.WinForms.Xaml.Controls;
using VisualCtl = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The advanced layout panels of the HWND-free visual backend: grid tracks and spans, docking order, canvas
/// coordinates, wrapping, and the hit-test behavior of a collapsed child.
/// </summary>
[STATestClass]
public sealed class VisualAdvancedPanelTests
{
    [TestMethod]
    public void Grid_PlacesChildrenByPixelTracksAndSpans()
    {
        var grid = new VisualCtl.Grid();
        grid.ColumnDefinitions.Add(new V.ColumnDefinition { Width = new V.GridLength(100) });
        grid.ColumnDefinitions.Add(new V.ColumnDefinition { Width = new V.GridLength(200) });
        grid.RowDefinitions.Add(new V.RowDefinition { Height = new V.GridLength(50) });
        grid.RowDefinitions.Add(new V.RowDefinition { Height = new V.GridLength(150) });

        var lowerRight = new VisualCtl.Border();
        VisualCtl.Grid.SetColumn(lowerRight, 1);
        VisualCtl.Grid.SetRow(lowerRight, 1);
        grid.Add(lowerRight);

        var topSpan = new VisualCtl.Border();
        VisualCtl.Grid.SetColumnSpan(topSpan, 2);
        grid.Add(topSpan);

        ArrangeRoot(grid, 300, 200);

        AssertBounds(lowerRight, 100, 50, 200, 150);
        AssertBounds(topSpan, 0, 0, 300, 50);
    }

    [TestMethod]
    public void Grid_DividesStarTracksByWeight()
    {
        var grid = new VisualCtl.Grid();
        grid.ColumnDefinitions.Add(new V.ColumnDefinition
        {
            Width = new V.GridLength(1, V.GridUnitType.Star),
        });
        grid.ColumnDefinitions.Add(new V.ColumnDefinition
        {
            Width = new V.GridLength(2, V.GridUnitType.Star),
        });

        var left = new VisualCtl.Border();
        var right = new VisualCtl.Border();
        VisualCtl.Grid.SetColumn(right, 1);
        grid.Add(left);
        grid.Add(right);

        ArrangeRoot(grid, 300, 100);

        AssertBounds(left, 0, 0, 100, 100);
        AssertBounds(right, 100, 0, 200, 100);
    }

    [TestMethod]
    public void Grid_AutoTrackTakesTheChildSizeAndTheStarKeepsTheRest()
    {
        var grid = new VisualCtl.Grid();
        grid.ColumnDefinitions.Add(new V.ColumnDefinition { Width = V.GridLength.Auto });
        grid.ColumnDefinitions.Add(new V.ColumnDefinition { Width = V.GridLength.Star });

        var fixedChild = new VisualCtl.Border { Width = 60 };
        var filling = new VisualCtl.Border();
        VisualCtl.Grid.SetColumn(filling, 1);
        grid.Add(fixedChild);
        grid.Add(filling);

        ArrangeRoot(grid, 300, 100);

        AssertBounds(fixedChild, 0, 0, 60, 100);
        AssertBounds(filling, 60, 0, 240, 100);
    }

    [TestMethod]
    public void DockPanel_DocksInChildOrderAndFillsTheLastChild()
    {
        var panel = new VisualCtl.DockPanel();

        var top = new VisualCtl.Border { Height = 40 };
        VisualCtl.DockPanel.SetDock(top, V.DockPlacement.Top);
        var bottom = new VisualCtl.Border { Height = 30 };
        VisualCtl.DockPanel.SetDock(bottom, V.DockPlacement.Bottom);
        var left = new VisualCtl.Border { Width = 50 };
        VisualCtl.DockPanel.SetDock(left, V.DockPlacement.Left);
        var fill = new VisualCtl.Border();

        panel.Add(top);
        panel.Add(bottom);
        panel.Add(left);
        panel.Add(fill);

        ArrangeRoot(panel, 300, 200);

        AssertBounds(top, 0, 0, 300, 40);
        AssertBounds(bottom, 0, 170, 300, 30);
        AssertBounds(left, 0, 40, 50, 130);
        AssertBounds(fill, 50, 40, 250, 130);
    }

    [TestMethod]
    public void DockPanel_WithoutLastChildFillLeavesTheLastChildAtItsCorner()
    {
        var panel = new VisualCtl.DockPanel { LastChildFill = false };
        var top = new VisualCtl.Border { Height = 20 };
        VisualCtl.DockPanel.SetDock(top, V.DockPlacement.Top);
        var last = new VisualCtl.Border { Width = 40 };
        VisualCtl.DockPanel.SetDock(last, V.DockPlacement.Left);

        panel.Add(top);
        panel.Add(last);

        ArrangeRoot(panel, 200, 100);

        AssertBounds(top, 0, 0, 200, 20);
        AssertBounds(last, 0, 20, 40, 80);
    }

    [TestMethod]
    public void Canvas_UsesAttachedLeftTopRightAndBottom()
    {
        var canvas = new VisualCtl.Canvas();

        var topLeft = new VisualCtl.Border { Width = 30, Height = 40 };
        VisualCtl.Canvas.SetLeft(topLeft, 10);
        VisualCtl.Canvas.SetTop(topLeft, 20);

        var bottomRight = new VisualCtl.Border { Width = 50, Height = 10 };
        VisualCtl.Canvas.SetRight(bottomRight, 10);
        VisualCtl.Canvas.SetBottom(bottomRight, 5);

        canvas.Add(topLeft);
        canvas.Add(bottomRight);

        ArrangeRoot(canvas, 200, 100);

        AssertBounds(topLeft, 10, 20, 30, 40);
        AssertBounds(bottomRight, 140, 85, 50, 10);
    }

    [TestMethod]
    public void WrapPanel_WrapsWhenTheLineIsFull()
    {
        var panel = new VisualCtl.WrapPanel { ItemWidth = 40, ItemHeight = 20, Spacing = 5 };
        var first = new VisualCtl.Border();
        var second = new VisualCtl.Border();
        var third = new VisualCtl.Border();
        panel.Add(first);
        panel.Add(second);
        panel.Add(third);

        ArrangeRoot(panel, 100, 100);

        AssertBounds(first, 0, 0, 40, 20);
        AssertBounds(second, 45, 0, 40, 20);
        AssertBounds(third, 0, 25, 40, 20);
    }

    [TestMethod]
    public void CollapsedChild_IsNotHitTested_AndThePanelBackgroundTakesTheHit()
    {
        var grid = new VisualCtl.Grid { Background = Color.FromArgb(255, 20, 30, 40) };
        var button = new VisualCtl.Button();
        grid.Add(button);

        ArrangeRoot(grid, 100, 100);

        Assert.AreSame(button, grid.HitTestVisual(new PointF(10f, 10f)));

        button.Visibility = V.Visibility.Collapsed;
        ArrangeRoot(grid, 100, 100);

        Assert.IsFalse(button.IsVisible);
        Assert.AreSame(grid, grid.HitTestVisual(new PointF(10f, 10f)));
        Assert.IsNull(grid.HitTestVisual(new PointF(150f, 150f)));
    }

    [TestMethod]
    public void Grid_AutoRowMeasuresANestedStarGridAtItsContentHeight()
    {
        var grid = new VisualCtl.Grid();
        grid.RowDefinitions.Add(new V.RowDefinition { Height = V.GridLength.Auto });
        grid.RowDefinitions.Add(new V.RowDefinition { Height = V.GridLength.Star });

        // 一个没有行定义的 Grid 是单星号行；放在 Auto 行里时必须量成内容高度，
        // 否则它会把整段可用高度要走，星号行归零。
        var header = new VisualCtl.Grid();
        header.Add(new VisualCtl.Border { Height = 24 });
        var body = new VisualCtl.Border();
        VisualCtl.Grid.SetRow(body, 1);

        grid.Add(header);
        grid.Add(body);

        ArrangeRoot(grid, 200, 150);

        AssertBounds(header, 0, 0, 200, 24);
        AssertBounds(body, 0, 24, 200, 126);
    }

    [TestMethod]
    public void Grid_MeasuresAChildInsideItsFixedTrack()
    {
        var grid = new VisualCtl.Grid();
        grid.ColumnDefinitions.Add(new V.ColumnDefinition { Width = new V.GridLength(64) });
        grid.ColumnDefinitions.Add(new V.ColumnDefinition { Width = V.GridLength.Star });

        // 星号列的子项按可用宽度撑满；ScrollViewer 的期望尺寸因此取决于父级给了它多宽的轨道。
        var track = new VisualCtl.Grid();
        track.Add(new VisualCtl.Border());
        var view = new VisualCtl.ScrollViewer { Content = track };
        grid.Add(view);

        ArrangeRoot(grid, 280, 100);

        Assert.AreEqual(64f, view.DesiredSize.Width, 0.1f,
            "the viewer is measured against its 64-pixel track, not the whole grid");
        Assert.AreEqual(64f, view.Bounds.Width, 0.1f);
    }

    private static void ArrangeRoot(VisualElement root, int width, int height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new RectangleF(0f, 0f, width, height));
    }

    private static void AssertBounds(VisualElement element, float x, float y, float width, float height)
    {
        Assert.AreEqual(x, element.Bounds.X, 0.01f, "X");
        Assert.AreEqual(y, element.Bounds.Y, 0.01f, "Y");
        Assert.AreEqual(width, element.Bounds.Width, 0.01f, "Width");
        Assert.AreEqual(height, element.Bounds.Height, 0.01f, "Height");
    }
}
