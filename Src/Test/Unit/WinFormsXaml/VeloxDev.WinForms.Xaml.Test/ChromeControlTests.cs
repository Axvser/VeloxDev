using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The WPF-parity chrome controls: content and item presenters, decorators, adorners, headers, toolbars and status
/// bars, plus the fixed-row virtualizing stack panel.
/// </summary>
[STATestClass]
public sealed class ChromeControlTests
{
    [TestMethod]
    public void ContentPresenter_HostsAControlContentAndFillsTheContentBounds()
    {
        using var presenter = new V.ContentPresenter { Size = new Size(120, 60) };
        using var content = new V.Border { Margin = V.Thickness.Empty };

        presenter.Content = content;
        presenter.PerformLayout();

        Assert.AreEqual(1, presenter.Controls.Count);
        Assert.AreSame(content, presenter.ContentVisual);
        Assert.AreEqual(new Rectangle(0, 0, 120, 60), content.Bounds);
    }

    [TestMethod]
    public void ContentPresenter_TemplatesTheContentAndSetsItsDataContext()
    {
        using var presenter = new V.ContentPresenter { Size = new Size(80, 30) };
        V.TextBlock? created = null;
        presenter.ContentTemplate = new DataTemplate(() => created = new V.TextBlock
        {
            Margin = V.Thickness.Empty,
        });

        presenter.Content = "hello";
        presenter.PerformLayout();

        Assert.IsNotNull(created);
        Assert.AreSame(created, presenter.ContentVisual);
        Assert.AreEqual("hello", created!.DataContext);
        Assert.AreEqual(new Rectangle(0, 0, 80, 30), created.Bounds);
    }

    [TestMethod]
    public void ItemsPresenter_WithAnOwnerMirrorsTheOwnersItems()
    {
        using var owner = new V.ItemsControl { ItemsSource = new[] { "a", "b" } };
        using var presenter = new V.ItemsPresenter
        {
            Size = new Size(100, 40),
            Owner = owner,
        };

        presenter.PerformLayout();

        Assert.AreSame(owner, presenter.Owner);
        Assert.AreEqual(2, presenter.RealizedItemCount);
    }

    [TestMethod]
    public void Decorator_FillsItsContentAreaWithTheSingleChild()
    {
        using var decorator = new V.Decorator { Size = new Size(100, 50) };
        using var child = new V.Border { Margin = V.Thickness.Empty };

        decorator.Child = child;
        decorator.PerformLayout();

        Assert.AreSame(child, decorator.Child);
        Assert.AreEqual(1, decorator.Controls.Count);
        Assert.AreEqual(new Rectangle(0, 0, 100, 50), child.Bounds);
    }

    [TestMethod]
    public void AdornerLayer_AddsAndPaintsAdornersInChildCoordinates()
    {
        using var layer = new V.AdornerLayer { Size = new Size(80, 40) };
        using var adorned = new V.Border
        {
            Margin = V.Thickness.Empty,
            Size = new Size(30, 20),
            Location = new Point(10, 5),
        };
        using var adorner = new ProbeAdorner { AdornedElement = adorned };
        layer.Controls.Add(adorned);

        layer.Add(adorner);
        layer.PerformLayout();

        Assert.AreEqual(1, layer.Adorners.Count);
        Assert.AreEqual(new Rectangle(10, 5, 30, 20), adorner.Bounds);

        using var bitmap = new Bitmap(80, 40);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            layer.Render(graphics);
        }

        Assert.AreEqual(1, adorner.RenderCount);
        Assert.AreEqual(new Rectangle(10, 5, 30, 20), adorner.LastBounds);

        Assert.IsTrue(layer.Remove(adorner));
        Assert.AreEqual(0, layer.Adorners.Count);
    }

    [TestMethod]
    public void HeaderedContentControl_LaysContentBelowTheHeaderStrip()
    {
        using var control = new V.HeaderedContentControl
        {
            Size = new Size(120, 80),
            Header = "Title",
        };
        using var child = new V.Border { Margin = V.Thickness.Empty };

        control.Content = child;
        control.PerformLayout();

        Assert.AreEqual("Title", control.Header);
        Assert.AreEqual(
            new Rectangle(0, V.HeaderedContentControl.HeaderHeight, 120, 80 - V.HeaderedContentControl.HeaderHeight),
            child.Bounds);
    }

    [TestMethod]
    public void ToolBar_ArrangesItemsAlongItsOrientation()
    {
        using var toolbar = new V.ToolBar { Size = new Size(200, 40) };
        using var first = new V.Button { Width = 50, Height = 24, Margin = V.Thickness.Empty };
        using var second = new V.Button { Width = 60, Height = 24, Margin = V.Thickness.Empty };

        toolbar.Items.Add(first);
        toolbar.Items.Add(second);
        toolbar.PerformLayout();

        Assert.AreEqual(2, toolbar.Items.Count);
        Assert.AreEqual(2, toolbar.Controls.Count);
        Assert.AreEqual(new Rectangle(0, 8, 50, 24), first.Bounds);
        Assert.AreEqual(new Rectangle(54, 8, 60, 24), second.Bounds);
    }

    [TestMethod]
    public void StatusBar_ArrangesItemsAndGivesSeparatorsAFixedSlot()
    {
        using var bar = new V.StatusBar { Size = new Size(200, 28) };
        using var left = new V.StatusBarItem { Width = 80 };
        using var separator = new V.StatusBarItem { IsSeparator = true };
        using var right = new V.StatusBarItem { Width = 100 };

        bar.Items.Add(left);
        bar.Items.Add(separator);
        bar.Items.Add(right);
        bar.PerformLayout();

        Assert.AreEqual(3, bar.Items.Count);
        Assert.AreEqual(new Rectangle(0, 0, 80, 28), left.Bounds);
        Assert.AreEqual(new Rectangle(80, 0, 8, 28), separator.Bounds);
        Assert.AreEqual(new Rectangle(88, 0, 100, 28), right.Bounds);
    }

    [TestMethod]
    public void VirtualizingStackPanel_RealizesOnlyTheViewportRows()
    {
        using var panel = new V.VirtualizingStackPanel
        {
            Size = new Size(200, 100),
            ItemHeight = 10,
        };

        panel.ItemsSource = Enumerable.Range(0, 1000).ToArray();
        panel.PerformLayout();

        Assert.IsTrue(panel.Virtualizing, "the panel must start in virtualizing mode");
        Assert.AreEqual(13, panel.RealizedItemCount);
        Assert.AreEqual(panel.RealizedItemCount, panel.Controls.Count);
        Assert.IsTrue(panel.RealizedItemCount < 1000);
    }

    private sealed class ProbeAdorner : V.Adorner
    {
        public int RenderCount { get; private set; }

        public Rectangle LastBounds { get; private set; }

        protected override void OnRenderAdorner(Graphics graphics, Rectangle bounds)
        {
            RenderCount++;
            LastBounds = bounds;
            base.OnRenderAdorner(graphics, bounds);
        }
    }
}
