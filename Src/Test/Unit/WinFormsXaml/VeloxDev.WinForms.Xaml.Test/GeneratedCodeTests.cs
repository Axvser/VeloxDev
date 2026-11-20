using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The generated-code path end to end: the fixture XAML and the <c>[XamlProperty]</c>/<c>[XamlAttachedProperty]</c>
/// declarations are compiled by the analyzer in this project, so these tests run the actual emitted members.
/// </summary>
[STATestClass]
public sealed class GeneratedCodeTests
{
    private static readonly Color Accent = Color.FromArgb(0xFF, 0x5B, 0x8C, 0xFF);

    [TestMethod]
    public void GeneratedView_BuildsTheNativeAndDerivedControlTree()
    {
        using var view = new GeneratedView();

        Assert.HasCount(3, view.Controls);
        Assert.IsInstanceOfType<Label>(view.Controls[0]);
        Assert.IsInstanceOfType<GeneratedCard>(view.Controls[1]);
        Assert.IsInstanceOfType<Button>(view.Controls[2]);
        Assert.AreEqual(new Size(420, 320), view.Size);
    }

    [TestMethod]
    public void GeneratedView_BindsNativeAndDependencyPropertiesFromTheDataContext()
    {
        using var view = new GeneratedView();
        var model = new GeneratedModel();
        var label = view.Controls.OfType<Label>().Single();
        var card = view.Controls.OfType<GeneratedCard>().Single();

        // 原生绑定的激活靠 BindingContext；真实宿主由 Form 提供，这里补上同样的环境。
        view.BindingContext = new BindingContext();
        view.DataContext = model;
        XamlTestSurface.EnsureHandle(label);

        Assert.HasCount(1, label.DataBindings);
        Assert.AreEqual("generated caption", label.Text);
        Assert.AreEqual("generated title", card.Title);

        model.Title = "changed title";
        model.Caption = "changed caption";

        Assert.AreEqual("changed title", card.Title);
        Assert.AreEqual("changed caption", label.Text);
    }

    [TestMethod]
    public void GeneratedView_ResolvesStaticResourcesAndBuildsTheDataTemplate()
    {
        using var view = new GeneratedView();
        var card = view.Controls.OfType<GeneratedCard>().Single();

        Assert.AreEqual(Accent, card.AccentColor);
        Assert.AreEqual(Accent, view.Resources["Accent"]);
        Assert.AreEqual("caption from a static resource", view.Resources["Caption"]);

        var model = Assert.IsInstanceOfType<GeneratedModel>(view.Resources["ModelResource"]);
        Assert.AreEqual("resource title", model.Title);

        var template = (DataTemplate)view.Resources["ItemTemplate"]!;
        var content = (GeneratedCard)template.CreateContent();
        using (content)
        {
            Assert.AreEqual(Accent, content.AccentColor);

            content.DataContext = new GeneratedModel { Title = "template title" };
            Assert.AreEqual("template title", content.Title);
        }
    }

    [TestMethod]
    public void GeneratedView_HooksTheXamlEventHandler()
    {
        using var view = new GeneratedView();
        var button = view.Controls.OfType<Button>().Single();

        button.PerformClick();
        button.PerformClick();

        Assert.AreEqual(2, view.ProbeClicks);
    }

    [TestMethod]
    public void GeneratedAttachedProperty_RoundTripsOnAPlainControl()
    {
        using var panel = new VeloxDev.WinForms.Xaml.Controls.Panel();

        GeneratedAttachments.SetNote(panel, "attached note");

        Assert.AreEqual("attached note", GeneratedAttachments.GetNote(panel));
        Assert.AreEqual(GeneratedAttachments.NoteProperty.DefaultValue, GeneratedAttachments.NoteProperty.GetValue(new Button()));
    }
}
