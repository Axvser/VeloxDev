using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>Command and input-binding markup: a bound Button.Command and a bound KeyBinding.Command.</summary>
[STATestClass]
public sealed class CommandXamlTests
{
    [TestMethod]
    public void BoundCommandAndBoundKeyBinding_ExecuteTheModelCommand()
    {
        using var view = new CommandView();
        var model = new ExtendedModel();
        view.DataContext = model;
        XamlTestSurface.EnsureHandle(view);

        view.CommandButton.PerformClick();
        Assert.AreEqual(1, model.SaveCount);

        Assert.AreEqual(Keys.F5, view.FirstBindingKey);
        Assert.IsTrue(view.RunFirstInputBinding());
        Assert.AreEqual(2, model.SaveCount);
    }
}
