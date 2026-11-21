using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>The chrome/template controls as authored in markup.</summary>
[STATestClass]
public sealed class ChromeXamlTests
{
    [TestMethod]
    public void ChromeView_ConstructsPresenterDecoratorsToolbarStatusAndVirtualizingPanel()
    {
        using var view = new ChromeView();
        var model = new ExtendedModel { Caption = "presented" };
        model.Items.Add(new ProbeItem { Name = "one" });
        model.Items.Add(new ProbeItem { Name = "two" });
        view.DataContext = model;
        XamlTestSurface.EnsureHandle(view);

        Assert.IsNotNull(view.Decorator.Child);
        Assert.IsNotNull(view.AdornerHost.Child);
        Assert.IsNotNull(view.AdornerHost.AdornerLayer);
        Assert.AreEqual("Head", view.Headered.Header);
        Assert.IsNotNull(view.Presenter.ContentVisual);

        Assert.HasCount(1, view.Toolbar.Items);
        Assert.HasCount(1, view.Status.Items);
        Assert.IsTrue(view.Virtualizing.RealizedItemCount <= 10);
    }
}
