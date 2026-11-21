using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Controls;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The extended analyzer fixture: name lookup, <c>x:Static</c>, format strings, style-setter bindings,
/// template parts and template triggers, z-order, declarative menus/tooltips and popup children.
/// </summary>
[STATestClass]
public sealed class ExtendedXamlTests
{
    [TestMethod]
    public void ElementNameStaticFormattingAndStyleSetterBinding_Resolve()
    {
        using var view = new ExtendedView();
        var model = new ExtendedModel { Caption = "hello", Amount = 1.5d };
        view.DataContext = model;
        XamlTestSurface.EnsureHandle(view);

        Assert.AreEqual("source text", view.ElementText);
        Assert.AreEqual("static value", view.StaticText);
        Assert.AreEqual(1.5d.ToString("N2", CultureInfo.CurrentCulture), view.FormattedText);
        Assert.AreEqual("hello", view.Card.Title);

        model.Caption = "changed";

        Assert.AreEqual("changed", view.Card.Title);
    }

    [TestMethod]
    public void TemplateBinding_PartsAndTemplateTriggers_Work()
    {
        using var view = new ExtendedView();
        view.DataContext = new ExtendedModel { Caption = "caption" };
        XamlTestSurface.EnsureHandle(view);
        view.TemplateCard.Title = "template title";

        var part = view.TemplateCard.GetTemplateChild<TextBlock>("PART_Text");
        Assert.IsNotNull(part);
        Assert.AreEqual("template title", part!.Text);

        view.TemplateCard.IsHot = true;

        Assert.AreEqual("hot", part.Text);

        view.TemplateCard.IsHot = false;

        Assert.AreEqual("template title", part.Text);
    }

    [TestMethod]
    public void PanelZIndex_SortsTheChildren()
    {
        using var view = new ExtendedView();
        XamlTestSurface.EnsureHandle(view);

        var high = view.FindName("highLayer");
        var low = view.FindName("lowLayer");
        Assert.IsNotNull(high);
        Assert.IsNotNull(low);
        Assert.IsTrue(
            view.Controls.GetChildIndex(low!) > view.Controls.GetChildIndex(high!),
            "a higher ZIndex must move to a lower child index (front)");
    }

    [TestMethod]
    public void DeclarativeMenuTooltipAndPopupChild_AreConstructed()
    {
        using var view = new ExtendedView();
        XamlTestSurface.EnsureHandle(view);

        Assert.IsNotNull(view.MenuButton.ContextMenu);
        Assert.HasCount(1, view.MenuButton.ContextMenu!.Items);
        Assert.AreEqual("Open", ((VeloxDev.WinForms.Xaml.Controls.MenuItem)view.MenuButton.ContextMenu.Items[0]).Header);
        Assert.AreEqual("tip text", ToolTipService.GetToolTip(view.MenuButton));
        Assert.IsNotNull(view.Popup.Child);
        Assert.IsFalse(view.Popup.IsOpen);
    }

    [TestMethod]
    public void ItemsPanel_ReceivesTheGeneratedItems()
    {
        using var view = new ExtendedView();
        var model = new ExtendedModel();
        model.Items.Add(new ProbeItem { Name = "one" });
        model.Items.Add(new ProbeItem { Name = "two" });
        view.DataContext = model;
        XamlTestSurface.EnsureHandle(view);

        Assert.IsNotNull(view.Items.ItemsPanel);
        Assert.HasCount(2, view.Items.ItemsPanel!.Controls);
        Assert.AreEqual(2, view.Items.RealizedItemCount);
    }
}
