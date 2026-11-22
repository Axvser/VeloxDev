using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The visual styling layer of the HWND-free backend: priority layers, trigger enter/exit, based-on chains,
/// target-type matching and implicit styles from surface and ancestor collections.
/// </summary>
[STATestClass]
public sealed class VisualStylingTests
{
    [TestMethod]
    public void StyleSetter_IsOutrankedByALocalValue()
    {
        var border = new VC.Border();
        var style = CreateWidthStyle(50d);

        border.Style = style;
        Assert.AreEqual(50d, border.Width, "the style layer is visible while no local value exists");

        border.Width = 12d;
        Assert.AreEqual(12d, border.Width, "a local value outranks the style layer");

        border.Style = null;
        Assert.AreEqual(12d, border.Width, "removing the style keeps the local value");
    }

    [TestMethod]
    public void RemovingAStyle_ClearsOnlyItsOwnLayer()
    {
        var border = new VC.Border();
        border.Style = CreateWidthStyle(50d);
        Assert.AreEqual(50d, border.Width);

        border.Style = null;

        Assert.IsTrue(double.IsNaN(border.Width), "without a local value the default is restored");
    }

    [TestMethod]
    public void Trigger_EntersAndExitsBetweenTheStyleValueAndTheDefault()
    {
        var button = new VC.Button();
        var style = new VisualStyle { TargetType = typeof(VC.Button) };
        style.Setters.Add(new VisualSetter(VisualElement.WidthProperty, 50d));
        var trigger = new VisualTrigger(VisualElement.IsEnabledProperty, false);
        trigger.Setters.Add(new VisualSetter(VisualElement.WidthProperty, 80d));
        style.Triggers.Add(trigger);

        button.Style = style;
        Assert.AreEqual(50d, button.Width, "the style setter applies while the trigger is inactive");

        button.IsEnabled = false;
        Assert.AreEqual(80d, button.Width, "the trigger overrides the style setter");

        button.IsEnabled = true;
        Assert.AreEqual(50d, button.Width, "exiting the trigger reveals the style setter, not the default");

        button.Style = null;
        Assert.IsTrue(double.IsNaN(button.Width), "removing the style clears the remaining layer");
    }

    [TestMethod]
    public void BasedOn_AppliesTheBaseFirstAndTheDerivedWins()
    {
        var border = new VC.Border();
        var baseStyle = new VisualStyle { TargetType = typeof(VC.Border) };
        baseStyle.Setters.Add(new VisualSetter(VisualElement.WidthProperty, 50d));
        baseStyle.Setters.Add(new VisualSetter(VisualElement.HeightProperty, 20d));

        var derived = new VisualStyle { TargetType = typeof(VC.Border), BasedOn = baseStyle };
        derived.Setters.Add(new VisualSetter(VisualElement.WidthProperty, 80d));

        border.Style = derived;

        Assert.AreEqual(80d, border.Width, "the derived setter wins over the shared base setter");
        Assert.AreEqual(20d, border.Height, "the base setter that is not overridden still applies");

        border.Style = null;
        Assert.IsTrue(double.IsNaN(border.Width));
        Assert.IsTrue(double.IsNaN(border.Height));
    }

    [TestMethod]
    public void TargetType_MatchesOnlyCompatibleElements()
    {
        var style = new VisualStyle { TargetType = typeof(VC.Button) };

        Assert.IsTrue(style.Matches(new VC.Button()));
        Assert.IsFalse(style.Matches(new VC.Border()));

        var broad = new VisualStyle { TargetType = typeof(VisualElement) };
        Assert.IsTrue(broad.Matches(new VC.Border()), "a base target type accepts derived elements");
        Assert.IsFalse(new VisualStyle().Matches(new VC.Border()), "a style without a target type matches nothing");
    }

    [TestMethod]
    public void SurfaceStyles_ApplyImplicitlyToMatchingElements()
    {
        using var surface = new VisualSurface();
        var root = new VC.StackPanel();
        var first = new VC.Border();
        var second = new VC.Border();
        root.Add(first);
        root.Add(second);

        var style = CreateWidthStyle(33d);
        surface.Styles.Add(style);
        surface.Root = root;

        Assert.AreEqual(33d, first.Width, "the style defined before the root applies on assignment");
        Assert.AreEqual(33d, second.Width);

        surface.Styles.Remove(style);
        Assert.IsTrue(double.IsNaN(first.Width), "removing the implicit style clears its layer");
    }

    [TestMethod]
    public void ExplicitStyle_BeatsTheImplicitStyle()
    {
        using var surface = new VisualSurface();
        var root = new VC.StackPanel();
        var border = new VC.Border();
        root.Add(border);

        var implicitStyle = CreateWidthStyle(33d);
        surface.Styles.Add(implicitStyle);
        surface.Root = root;
        Assert.AreEqual(33d, border.Width);

        border.Style = CreateWidthStyle(44d);
        Assert.AreEqual(44d, border.Width, "the explicit style wins");

        border.Style = null;
        Assert.AreEqual(33d, border.Width, "clearing the explicit style re-applies the implicit one");

        surface.Styles.Clear();
        Assert.IsTrue(double.IsNaN(border.Width));
    }

    [TestMethod]
    public void NearestAncestorCollectionWins()
    {
        using var surface = new VisualSurface();
        var root = new VC.StackPanel();
        var middle = new VC.StackPanel();
        var border = new VC.Border();
        root.Add(middle);
        middle.Add(border);
        surface.Root = root;

        root.Styles.Add(CreateWidthStyle(10d));
        Assert.AreEqual(10d, border.Width, "the outer ancestor collection reaches the descendant");

        var inner = CreateWidthStyle(20d);
        middle.Styles.Add(inner);
        Assert.AreEqual(20d, border.Width, "the nearer collection wins");

        middle.Styles.Remove(inner);
        Assert.AreEqual(10d, border.Width, "removing the nearer style falls back to the outer collection");
    }

    [TestMethod]
    public void SharedStyle_KeepsTriggerStatePerElement()
    {
        var style = new VisualStyle { TargetType = typeof(VC.Button) };
        var trigger = new VisualTrigger(VisualElement.IsEnabledProperty, false);
        trigger.Setters.Add(new VisualSetter(VisualElement.MarginProperty, new XamlThickness(4f)));
        style.Triggers.Add(trigger);

        var first = new VC.Button { Style = style };
        var second = new VC.Button { Style = style };

        first.IsEnabled = false;

        Assert.AreEqual(new XamlThickness(4f), first.Margin);
        Assert.AreEqual(XamlThickness.Empty, second.Margin, "the trigger state belongs to the element, not the style");
    }

    [TestMethod]
    public void RemovingTheElement_DetachesTheTriggerSubscription()
    {
        using var surface = new VisualSurface();
        var root = new VC.StackPanel();
        var button = new VC.Button();
        root.Add(button);
        surface.Root = root;

        var style = new VisualStyle { TargetType = typeof(VC.Button) };
        var trigger = new VisualTrigger(VisualElement.IsEnabledProperty, false);
        trigger.Setters.Add(new VisualSetter(VisualElement.MarginProperty, new XamlThickness(4f)));
        style.Triggers.Add(trigger);
        surface.Styles.Add(style);

        button.IsEnabled = false;
        Assert.AreEqual(new XamlThickness(4f), button.Margin);

        root.RemoveChild(button);
        Assert.AreEqual(XamlThickness.Empty, button.Margin, "leaving the tree removes the implicit style");

        button.IsEnabled = true;
        button.IsEnabled = false;
        Assert.AreEqual(XamlThickness.Empty, button.Margin, "the detached element no longer evaluates the trigger");
    }

    [TestMethod]
    public void ReplacingTheRoot_RemovesTheSurfaceStyleFromTheOldTree()
    {
        using var surface = new VisualSurface();
        var oldRoot = new VC.Border();
        surface.Root = oldRoot;
        surface.Styles.Add(CreateWidthStyle(33d));
        Assert.AreEqual(33d, oldRoot.Width);

        surface.Root = new VC.StackPanel();

        Assert.IsTrue(double.IsNaN(oldRoot.Width), "an element that left the surface loses its implicit style");
    }

    private static VisualStyle CreateWidthStyle(double width)
    {
        var style = new VisualStyle { TargetType = typeof(VC.Border) };
        style.Setters.Add(new VisualSetter(VisualElement.WidthProperty, width));
        return style;
    }
}
