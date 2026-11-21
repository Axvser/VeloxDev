using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Layout and chrome controls that mirror the WPF/Avalonia basics: uniform grid, viewbox, thumb, grid splitter,
/// group box, expander and tab control.
/// </summary>
[STATestClass]
public sealed class LayoutControlTests
{
    [TestMethod]
    public void UniformGrid_GivesEveryChildTheSameCell()
    {
        using var grid = new V.UniformGrid
        {
            Size = new Size(120, 80),
            Rows = 2,
            Columns = 3,
        };

        var children = new List<V.Border>();
        for (var index = 0; index < 6; index++)
        {
            var child = new V.Border { Margin = V.Thickness.Empty };
            children.Add(child);
            grid.Controls.Add(child);
        }

        grid.PerformLayout();

        Assert.AreEqual(new Rectangle(0, 0, 40, 40), children[0].Bounds);
        Assert.AreEqual(new Rectangle(40, 0, 40, 40), children[1].Bounds);
        Assert.AreEqual(new Rectangle(80, 0, 40, 40), children[2].Bounds);
        Assert.AreEqual(new Rectangle(0, 40, 40, 40), children[3].Bounds);
        Assert.AreEqual(new Rectangle(80, 40, 40, 40), children[5].Bounds);

        foreach (var child in children)
        {
            child.Dispose();
        }
    }

    [TestMethod]
    public void UniformGrid_FirstColumnSkipsTheLeadingCells()
    {
        using var grid = new V.UniformGrid
        {
            Size = new Size(120, 40),
            Rows = 1,
            Columns = 3,
            FirstColumn = 1,
        };
        using var child = new V.Border { Margin = V.Thickness.Empty };
        grid.Controls.Add(child);

        grid.PerformLayout();

        Assert.AreEqual(new Rectangle(40, 0, 40, 40), child.Bounds);
    }

    [TestMethod]
    public void GroupBox_PlacesContentBelowTheHeader()
    {
        using var box = new V.GroupBox
        {
            Size = new Size(200, 120),
            Header = "Details",
        };
        using var content = new V.Border { Margin = V.Thickness.Empty };
        box.Content = content;

        box.PerformLayout();

        Assert.AreEqual("Details", box.Header);
        Assert.AreEqual(32, content.Bounds.Top, "the header strip is 32 pixels tall");
        Assert.AreEqual(200, content.Bounds.Width);
        Assert.AreEqual(88, content.Bounds.Height);
    }

    [TestMethod]
    public void Expander_TogglesItsContentVisibility()
    {
        using var expander = new V.Expander
        {
            Size = new Size(200, 160),
            Header = "More",
        };
        using var content = new V.Border { Margin = V.Thickness.Empty };
        expander.Content = content;

        expander.PerformLayout();

        Assert.IsFalse(expander.IsExpanded, "collapsed by default");
        Assert.IsFalse(IsShown(content), "collapsed content is hidden");

        expander.IsExpanded = true;
        expander.PerformLayout();

        Assert.IsTrue(expander.IsExpanded);
        Assert.IsTrue(IsShown(content));
        Assert.AreEqual(32, content.Bounds.Top, "the content starts below the 32 pixel header");
        Assert.AreEqual(128, content.Bounds.Height);
    }

    [TestMethod]
    public void Expander_HeaderClickTogglesTheState()
    {
        using var expander = new V.Expander
        {
            Size = new Size(200, 160),
            Header = "More",
        };
        XamlTestSurface.EnsureHandle(expander);

        RaiseMouse(expander, "OnMouseDown", 12, 12);
        Assert.IsTrue(expander.IsExpanded);

        RaiseMouse(expander, "OnMouseDown", 12, 12);
        Assert.IsFalse(expander.IsExpanded);
    }

    [TestMethod]
    public void TabControl_ShowsOnlyTheSelectedTab()
    {
        using var tabs = new V.TabControl { Size = new Size(240, 160) };
        using var first = new V.TabItem { Header = "First" };
        using var second = new V.TabItem { Header = "Second" };
        using var firstContent = new V.Border { Margin = V.Thickness.Empty };
        using var secondContent = new V.Border { Margin = V.Thickness.Empty };
        first.Content = firstContent;
        second.Content = secondContent;

        tabs.Items.Add(first);
        tabs.Items.Add(second);
        tabs.PerformLayout();

        Assert.AreEqual(2, tabs.Items.Count);
        Assert.AreEqual(0, tabs.SelectedIndex, "the first tab is selected on population");
        Assert.AreSame(first, tabs.SelectedItem);
        Assert.IsTrue(first.IsSelected);
        Assert.IsFalse(second.IsSelected);
        Assert.IsTrue(IsShown(first));
        Assert.IsFalse(IsShown(second));
        Assert.AreEqual(new Rectangle(0, 32, 240, 128), first.Bounds);

        var changes = 0;
        tabs.SelectionChanged += (_, _) => changes++;
        tabs.SelectedIndex = 1;

        Assert.AreEqual(1, tabs.SelectedIndex);
        Assert.AreSame(second, tabs.SelectedItem);
        Assert.IsFalse(first.IsSelected);
        Assert.IsTrue(second.IsSelected);
        Assert.IsTrue(IsShown(second));
        Assert.IsFalse(IsShown(first));
        Assert.AreEqual(new Rectangle(0, 32, 240, 128), second.Bounds);
        Assert.AreEqual(1, changes, "one selection change is one event");
    }

    [TestMethod]
    public void TabControl_AcceptsTabsAddedDirectlyToControls()
    {
        using var tabs = new V.TabControl { Size = new Size(240, 160) };
        using var item = new V.TabItem { Header = "Direct" };

        tabs.Controls.Add(item);
        tabs.PerformLayout();

        Assert.AreEqual(1, tabs.Items.Count);
        Assert.AreSame(item, tabs.Items[0]);
        Assert.AreEqual(0, tabs.SelectedIndex);
        Assert.IsTrue(IsShown(item));
    }

    [TestMethod]
    public void Thumb_ReportsDragDeltasAndCompletion()
    {
        using var thumb = new V.Thumb { Size = new Size(12, 40) };
        XamlTestSurface.EnsureHandle(thumb);

        var started = 0;
        var completed = 0;
        var completedHorizontal = 0;
        var completedVertical = 0;
        var canceled = true;
        var deltas = new List<Point>();
        thumb.DragStarted += (_, _) => started++;
        thumb.DragDelta += (_, e) => deltas.Add(new Point(e.HorizontalChange, e.VerticalChange));
        thumb.DragCompleted += (_, e) =>
        {
            completed++;
            completedHorizontal = e.HorizontalChange;
            completedVertical = e.VerticalChange;
            canceled = e.Canceled;
        };

        RaiseMouse(thumb, "OnMouseDown", 5, 5);
        RaiseMouse(thumb, "OnMouseMove", 12, 9);
        RaiseMouse(thumb, "OnMouseMove", 10, 12);
        RaiseMouse(thumb, "OnMouseUp", 10, 12);

        Assert.AreEqual(1, started);
        Assert.AreEqual(1, completed);
        Assert.IsFalse(canceled);
        Assert.AreEqual(5, completedHorizontal, "7 then -2");
        Assert.AreEqual(7, completedVertical, "4 then 3");
        CollectionAssert.AreEqual(
            new[] { new Point(7, 4), new Point(-2, 3) },
            deltas);
    }

    [TestMethod]
    public void Thumb_DisableCancelsTheActiveDrag()
    {
        using var thumb = new V.Thumb { Size = new Size(12, 40) };
        XamlTestSurface.EnsureHandle(thumb);

        var canceled = false;
        thumb.DragCompleted += (_, e) => canceled = e.Canceled;

        RaiseMouse(thumb, "OnMouseDown", 5, 5);
        thumb.Disable();

        Assert.IsFalse(thumb.IsDragging);
        Assert.IsTrue(canceled);
    }

    [TestMethod]
    public void GridSplitter_ResizesItsNeighbours()
    {
        using var host = new V.Panel { Size = new Size(300, 100) };
        using var before = new V.Border { Width = 80, Height = 100, Margin = V.Thickness.Empty };
        using var splitter = new V.GridSplitter
        {
            Orientation = Orientation.Vertical,
            ResizeDirection = V.GridResizeDirection.Columns,
            ResizeBehavior = V.GridResizeBehavior.PreviousAndNext,
        };
        using var after = new V.Border { Width = 80, Height = 100, Margin = V.Thickness.Empty };
        host.Controls.Add(before);
        host.Controls.Add(splitter);
        host.Controls.Add(after);
        XamlTestSurface.EnsureHandle(splitter);

        RaiseMouse(splitter, "OnMouseDown", 3, 10);
        RaiseMouse(splitter, "OnMouseMove", 13, 10);
        RaiseMouse(splitter, "OnMouseUp", 13, 10);

        Assert.AreEqual(90, before.Width, "the previous sibling grows by the drag");
        Assert.AreEqual(70, after.Width, "the next sibling shrinks by the drag");
    }

    [TestMethod]
    public void GridSplitter_TargetReceivesTheDrag()
    {
        using var host = new V.Panel { Size = new Size(300, 200) };
        using var target = new V.Border { Width = 50, Height = 50, Margin = V.Thickness.Empty };
        using var splitter = new V.GridSplitter
        {
            ResizeDirection = V.GridResizeDirection.Rows,
            Target = target,
        };
        host.Controls.Add(target);
        host.Controls.Add(splitter);
        XamlTestSurface.EnsureHandle(splitter);

        RaiseMouse(splitter, "OnMouseDown", 3, 3);
        RaiseMouse(splitter, "OnMouseMove", 3, 13);
        RaiseMouse(splitter, "OnMouseUp", 3, 13);

        Assert.AreEqual(60, target.Height);
    }

    [TestMethod]
    public void Viewbox_ScalesTheChildIntoTheContentArea()
    {
        using var viewbox = new V.Viewbox
        {
            Size = new Size(200, 100),
            Stretch = V.Stretch.Fill,
        };
        using var child = new V.Border { Width = 50, Height = 50, Margin = V.Thickness.Empty };
        viewbox.Child = child;

        viewbox.PerformLayout();

        Assert.AreEqual(new Rectangle(0, 0, 200, 100), child.Bounds);
    }

    private static void RaiseMouse(System.Windows.Forms.Control control, string methodName, int x, int y)
    {
        var method = control.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"{methodName} must exist on {control.GetType().Name}");
        method.Invoke(control, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);
    }

    // WinForms 的 Visible 会叠加父链状态；句柄未挂到可见窗体时只有内部 STATE_VISIBLE 位反映本控件自身的取舍。
    private static bool IsShown(System.Windows.Forms.Control control)
    {
        var method = typeof(System.Windows.Forms.Control).GetMethod(
            "GetState",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "Control.GetState is expected on every WinForms target");
        return (bool)method.Invoke(control, [0x02])!;
    }
}
