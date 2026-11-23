using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using SolidColorBrush = VeloxDev.WinForms.Xaml.SolidColorBrush;
using DrawingSize = System.Drawing.Size;
using Size = VeloxDev.WorkflowSystem.Size;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>The in-tree minimap and info HUD.</summary>
[STATestClass]
public sealed class VisualOverlayTests
{
    [TestMethod]
    public void Minimap_PressRequestsAWorldViewport()
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));
        tree.CreateNodeCommand.Execute(new NodeDefaultComponent
        {
            Anchor = new Anchor(100d, 80d),
            Size = new WorkflowSystem.Size(200d, 120d),
        });

        var minimap = new WorkflowMinimapOverlay
        {
            Width = 200,
            Height = 140,
            WorkflowTree = tree,
            ViewportWidth = 800,
            ViewportHeight = 600,
        };

        using var host = new VisualHost { Size = new DrawingSize(200, 140), DpiScaleOverride = 1f };
        host.Root = minimap;

        double x = double.NaN;
        double y = double.NaN;
        minimap.ViewportScrollRequested += (sx, sy) => { x = sx; y = sy; };

        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        host.RenderTo(graphics, new DrawingSize(200, 140));
        host.ProcessPointerDown(new PointF(100f, 70f), MouseButtons.Left);

        Assert.IsFalse(double.IsNaN(x), "pressing the minimap raises a viewport request");
        Assert.IsFalse(double.IsNaN(y));
        Assert.AreEqual(x, minimap.ScrollOffsetX, 0.01d);
        Assert.AreEqual(y, minimap.ScrollOffsetY, 0.01d);
    }

    [TestMethod]
    public void Minimap_RendersItsThumbnailInsideTheVisualTree()
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));
        tree.CreateNodeCommand.Execute(new NodeDefaultComponent
        {
            Anchor = new Anchor(0d, 0d),
            Size = new WorkflowSystem.Size(200d, 120d),
        });

        var minimap = new WorkflowMinimapOverlay
        {
            Width = 200,
            Height = 140,
            WorkflowTree = tree,
            NodeBrush = Color.Lime,
        };

        using var host = new VisualHost { Size = new DrawingSize(200, 140), DpiScaleOverride = 1f };
        host.Root = minimap;
        using var bitmap = new Bitmap(200, 140);
        using var graphics = Graphics.FromImage(bitmap);
        host.RenderTo(graphics, new DrawingSize(200, 140));

        var green = 0;
        for (var y = 0; y < 140; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.G > 180 && pixel.R < 120) green++;
            }
        }

        Assert.IsGreaterThan(100, green, "the minimap draws a node thumbnail");
    }
}

/// <summary>The visual link menu's command path resolves against the link data context.</summary>
[STATestClass]
public sealed class VisualLinkMenuTests
{
    [TestMethod]
    public void CommandPath_InvokesTheLinksDeleteCommand()
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));
        var senderNode = new NodeDefaultComponent { Anchor = new Anchor(0d, 0d), Size = new WorkflowSystem.Size(100d, 80d) };
        var receiverNode = new NodeDefaultComponent { Anchor = new Anchor(300d, 0d), Size = new WorkflowSystem.Size(100d, 80d) };
        tree.CreateNodeCommand.Execute(senderNode);
        tree.CreateNodeCommand.Execute(receiverNode);
        var sender = new SlotDefaultComponent { Channel = SlotChannel.OneTarget };
        var receiver = new SlotDefaultComponent { Channel = SlotChannel.OneSource };
        senderNode.CreateSlotCommand.Execute(sender);
        receiverNode.CreateSlotCommand.Execute(receiver);
        var link = tree.GetHelper().CreateLink(sender, receiver);

        var menu = new VeloxDev.WinForms.Xaml.Visuals.Controls.VisualContextMenu { DataContext = link };
        var item = new VeloxDev.WinForms.Xaml.Visuals.Controls.VisualMenuItem
        {
            Header = "Delete",
            CommandPath = "DeleteCommand",
        };
        menu.Items.Add(item);

        typeof(VeloxDev.WinForms.Xaml.Visuals.Controls.VisualMenuItem)
            .GetProperty("Owner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(item, menu);
        typeof(VeloxDev.WinForms.Xaml.Visuals.Controls.VisualMenuItem)
            .GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(item, null);

        Assert.AreEqual(0, tree.Links.Count, "the menu command path ran the link's DeleteCommand");
    }
}

/// <summary>The visual menu is hit-testable after it opens in the surface overlay.</summary>
[STATestClass]
public sealed class VisualMenuHitTestTests
{
    [TestMethod]
    public void ClickingTheDeleteRow_InvokesTheLinkDeleteCommand()
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));
        var senderNode = new NodeDefaultComponent { Anchor = new Anchor(0d, 0d), Size = new WorkflowSystem.Size(100d, 80d) };
        var receiverNode = new NodeDefaultComponent { Anchor = new Anchor(300d, 0d), Size = new WorkflowSystem.Size(100d, 80d) };
        tree.CreateNodeCommand.Execute(senderNode);
        tree.CreateNodeCommand.Execute(receiverNode);
        var sender = new SlotDefaultComponent { Channel = SlotChannel.OneTarget };
        var receiver = new SlotDefaultComponent { Channel = SlotChannel.OneSource };
        senderNode.CreateSlotCommand.Execute(sender);
        receiverNode.CreateSlotCommand.Execute(receiver);
        var link = tree.GetHelper().CreateLink(sender, receiver);

        var target = new VeloxDev.WinForms.Xaml.Visuals.Controls.Border
        {
            Width = 400,
            Height = 300,
            Background = new SolidColorBrush(Color.DimGray),
        };
        using var host = new VisualHost { Size = new DrawingSize(400, 300), DpiScaleOverride = 1f };
        host.Root = target;

        var menu = new VeloxDev.WinForms.Xaml.Visuals.Controls.VisualContextMenu { DataContext = link };
        menu.Items.Add(new VeloxDev.WinForms.Xaml.Visuals.Controls.VisualMenuItem
        {
            Header = "Delete",
            CommandPath = "DeleteCommand",
        });
        menu.OpenAt(target, new PointF(100f, 100f));

        var row = menu.AbsoluteBounds;
        host.ProcessPointerDown(new PointF(row.Left + 10f, row.Top + 10f), MouseButtons.Left);

        Assert.AreEqual(0, tree.Links.Count, "the menu row must run the link command");
    }
}
