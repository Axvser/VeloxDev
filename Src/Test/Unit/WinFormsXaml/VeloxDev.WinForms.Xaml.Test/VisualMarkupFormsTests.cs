using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The visual markup forms the generator must support: unprefixed controls under a <c>local:</c> root, attached
/// properties, named resources, an implicit <c>Style</c>, a <c>ControlTemplate</c> resource, a
/// <c>DataTemplate x:Key</c> resource and a selector resource whose template is shared with an
/// <c>ItemsControl.ItemTemplate</c>.
/// </summary>
[STATestClass]
public sealed class VisualMarkupFormsTests
{
    [TestMethod]
    public void VisualView_BuildsFieldsAttachedValuesAndResources()
    {
        using var view = new VisualFormsView();

        Assert.HasCount(6, view.Children);

        // Unprefixed controls resolved to the visual backend; the root itself used local:.
        Assert.IsInstanceOfType<VC.TextBlock>(view.Children[0]);
        Assert.IsInstanceOfType<VC.Grid>(view.Children[1]);
        Assert.IsInstanceOfType<VC.Canvas>(view.Children[2]);
        Assert.IsInstanceOfType<VC.Button>(view.Children[3]);
        Assert.IsInstanceOfType<VC.ItemsControl>(view.Children[4]);

        Assert.AreSame(view.Caption, view.Children[0]);
        Assert.AreSame(view.Action, view.Children[3]);
        Assert.AreSame(view.List, view.Children[4]);

        // The implicit Style resource (no x:Key) reaches an unstyled child when it is added to the tree.
        Assert.AreEqual(120d, view.ImplicitCaption.Width);

        // A StaticResource on the root's own property resolves through the root's dictionary.
        Assert.IsNotNull(view.Background);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), view.Background!.ApproximateColor);

        // A child attribute is assigned before the child is parented, so resource lookups must be anchored
        // on the view root rather than on the unparented child.
        Assert.IsNotNull(view.Caption.Style);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00), view.Caption.Foreground);
        Assert.IsNotNull(view.Caption.Background);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x33, 0x44, 0x55), view.Caption.Background!.ApproximateColor);

        // Attached properties.
        Assert.AreEqual(1, VC.Grid.GetRow(view.TopCell));
        Assert.AreEqual(7d, VC.Canvas.GetLeft(view.Placed));
        Assert.AreEqual(9d, VC.Canvas.GetTop(view.Placed));

        // Named resources: a control template, a data template and a selector that refers to the data template.
        Assert.IsInstanceOfType<VisualControlTemplate>(view.CardChrome);
        Assert.IsInstanceOfType<DataTemplate>(view.RowTemplate);
        Assert.IsInstanceOfType<Selector>(view.RowSelector);
        Assert.AreSame(view.RowTemplate, view.RowSelector.Template);
    }

    [TestMethod]
    public void SelectorTemplate_CanDriveTheItemsControlItemTemplate()
    {
        using var view = new VisualFormsView();

        // The selector resource carries the keyed data template; assign it to the items control the way a
        // template selector host would.
        view.List.ItemTemplate = view.RowSelector.Template;
        view.List.ItemsSource = new[] { "alpha", "beta" };

        Assert.HasCount(2, view.List.Items);
        Assert.AreEqual("alpha", Assert.IsInstanceOfType<VC.TextBlock>(view.List.Items[0]).Text);
        Assert.AreEqual("beta", Assert.IsInstanceOfType<VC.TextBlock>(view.List.Items[1]).Text);
        Assert.AreEqual(60d, view.List.Items[0].Width);
    }

    [TestMethod]
    public void InlineItemTemplate_RealizesItems()
    {
        using var view = new VisualFormsView();

        view.List.ItemsSource = new[] { "one", "two", "three" };

        Assert.HasCount(3, view.List.Items);
        Assert.AreEqual("one", Assert.IsInstanceOfType<VC.TextBlock>(view.List.Items[0]).Text);
        Assert.AreEqual(50d, view.List.Items[0].Width);
    }

    [TestMethod]
    public void VisualView_WiresTheGeneratedClickHandler()
    {
        using var view = new VisualFormsView();
        using var surface = new VisualHost { Size = new Size(320, 240) };
        surface.Root = view;

        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(320, 240));

        var bounds = view.Action.AbsoluteBounds;
        var center = new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);

        Assert.AreEqual(1, view.ActionClicks);
    }
}
