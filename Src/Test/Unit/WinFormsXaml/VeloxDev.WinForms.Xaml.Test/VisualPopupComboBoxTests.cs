using System.Drawing;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using VeloxDev.WinForms.Generator;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The surface overlay, the visual popup and the visual combo box: hit-test priority, outside-click dismissal,
/// pointer selection, keyboard forwarding and the XAML inline-item path.
/// </summary>
[STATestClass]
public sealed class VisualPopupComboBoxTests
{
    [TestMethod]
    public void Popup_RendersAboveTheRootAndHitTestsFirst()
    {
        using var surface = new VisualHost();
        using var root = new VC.Border { Background = Color.Blue };
        var target = new VC.Border { Width = 80f, Height = 30f };
        root.Add(target);
        surface.Root = root;
        Layout(surface, 320, 240);

        using var popup = new VC.VisualPopup
        {
            PlacementTarget = target,
            Background = Color.Red,
            BorderThickness = default,
            Padding = new VeloxDev.WinForms.Xaml.Controls.Thickness(4f),
            Child = new VC.Border { Width = 60f, Height = 30f },
            IsOpen = true,
        };

        var bounds = popup.AbsoluteBounds;
        Assert.IsTrue(bounds.Bottom > 30f, "the popup is placed below the target");

        using var bitmap = new Bitmap(320, 240);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, new Size(320, 240));
        }

        var pixel = bitmap.GetPixel((int)(bounds.X + 20f), (int)(bounds.Y + 10f));
        Assert.AreEqual(Color.Red.ToArgb(), pixel.ToArgb(), "the overlay paints after the root tree");

        var hit = surface.FindElementAt(new PointF(bounds.X + 2f, bounds.Y + 2f));
        Assert.AreSame(popup, hit, "the overlay is hit-tested before the root tree");
    }

    [TestMethod]
    public void Popup_OutsidePressClosesAndInsideOrTargetPressKeepsItOpen()
    {
        using var surface = new VisualHost();
        using var root = new VC.StackPanel();
        var target = new VC.Border { Width = 80f, Height = 30f, Background = Color.Lime };
        root.Add(target);
        surface.Root = root;
        Layout(surface, 320, 240);

        using var popup = new VC.VisualPopup
        {
            PlacementTarget = target,
            Child = new VC.Border { Width = 60f, Height = 30f },
            IsOpen = true,
        };

        surface.ProcessPointerDown(new PointF(300f, 220f), MouseButtons.Left);
        surface.ProcessPointerUp(new PointF(300f, 220f), MouseButtons.Left);
        Assert.IsFalse(popup.IsOpen, "a press outside both the popup and its target closes it");

        popup.Open();
        surface.ProcessPointerDown(
            new PointF(popup.AbsoluteBounds.X + 4f, popup.AbsoluteBounds.Y + 4f),
            MouseButtons.Left);
        surface.ProcessPointerUp(
            new PointF(popup.AbsoluteBounds.X + 4f, popup.AbsoluteBounds.Y + 4f),
            MouseButtons.Left);
        Assert.IsTrue(popup.IsOpen, "a press inside the popup keeps it open");

        var targetCenter = new PointF(target.AbsoluteBounds.X + 2f, target.AbsoluteBounds.Y + 2f);
        surface.ProcessPointerDown(targetCenter, MouseButtons.Left);
        surface.ProcessPointerUp(targetCenter, MouseButtons.Left);
        Assert.IsTrue(popup.IsOpen, "a press on the placement target keeps it open");
    }

    [TestMethod]
    public void Popup_StaysOpenIgnoresOutsidePressAndRootReplacementCloses()
    {
        using var surface = new VisualHost();
        using var root = new VC.StackPanel();
        var target = new VC.Border { Width = 80f, Height = 30f };
        root.Add(target);
        surface.Root = root;
        Layout(surface, 320, 240);

        using var popup = new VC.VisualPopup
        {
            PlacementTarget = target,
            StaysOpen = true,
            Child = new VC.Border { Width = 60f, Height = 30f },
            IsOpen = true,
        };

        surface.ProcessPointerDown(new PointF(300f, 220f), MouseButtons.Left);
        surface.ProcessPointerUp(new PointF(300f, 220f), MouseButtons.Left);
        Assert.IsTrue(popup.IsOpen, "StaysOpen ignores outside presses");

        surface.Root = new VC.StackPanel();
        Assert.IsFalse(popup.IsOpen, "replacing the root closes every popup");
        Assert.IsNull(popup.Child!.Parent, "the child leaves the overlay");
    }

    [TestMethod]
    public void ComboBox_SelectionSetBeforeItemsIsAppliedWhenItemsArrive()
    {
        using var combo = new VC.ComboBox { SelectedIndex = 1 };

        combo.Items.Add("One");
        combo.Items.Add("Two");
        combo.Items.Add("Three");

        Assert.AreEqual("Two", combo.SelectedItem, "the pending index resolves once the items exist");
        Assert.AreEqual(1, combo.DropDownList.SelectedIndex);
    }

    [TestMethod]
    public void ComboBox_PointerSelectionCommitsAndCloses()
    {
        using var surface = new VisualHost();
        using var combo = CreateComboBox();
        var root = new VC.StackPanel();
        root.Add(combo);
        surface.Root = root;
        Layout(surface, 320, 240);

        var comboCenter = Center(combo.AbsoluteBounds);
        surface.ProcessPointerDown(comboCenter, MouseButtons.Left);
        surface.ProcessPointerUp(comboCenter, MouseButtons.Left);

        Assert.IsTrue(combo.IsDropDownOpen, "a pointer press opens the drop-down");
        Assert.HasCount(3, combo.DropDownList.Items);
        Assert.AreSame(combo, surface.FocusedElement, "focus stays on the combo box, not on the list");

        var row = Center(combo.DropDownList.Items[2].AbsoluteBounds);
        surface.ProcessPointerDown(row, MouseButtons.Left);
        surface.ProcessPointerUp(row, MouseButtons.Left);

        Assert.IsFalse(combo.IsDropDownOpen, "choosing a row closes the drop-down");
        Assert.AreEqual(2, combo.SelectedIndex);
        Assert.AreEqual("Three", combo.SelectedItem);
        Assert.AreSame(combo, surface.FocusedElement);
    }

    [TestMethod]
    public void ComboBox_KeyboardMovesOpensCommitsAndEscapeReverts()
    {
        using var surface = new VisualHost();
        using var combo = CreateComboBox();
        var root = new VC.StackPanel();
        root.Add(combo);
        surface.Root = root;
        Layout(surface, 320, 240);
        Assert.IsTrue(combo.Focus());

        surface.ProcessKeyDown(Keys.Down);
        Assert.AreEqual(1, combo.SelectedIndex, "Down on the closed combo moves the selection");

        surface.ProcessKeyDown(Keys.Down, Keys.Alt);
        Assert.IsTrue(combo.IsDropDownOpen, "Alt+Down opens the drop-down");
        Assert.AreSame(combo, surface.FocusedElement);

        surface.ProcessKeyDown(Keys.Down);
        Assert.AreEqual(2, combo.SelectedIndex, "Down is forwarded to the list while open");

        surface.ProcessKeyDown(Keys.Escape);
        Assert.IsFalse(combo.IsDropDownOpen, "Escape closes the drop-down");
        Assert.AreEqual(1, combo.SelectedIndex, "Escape restores the selection captured at open");

        surface.ProcessKeyDown(Keys.Down, Keys.Alt);
        surface.ProcessKeyDown(Keys.Down);
        surface.ProcessKeyDown(Keys.Enter);
        Assert.IsFalse(combo.IsDropDownOpen, "Enter commits and closes");
        Assert.AreEqual(2, combo.SelectedIndex);
    }

    [TestMethod]
    public void ComboBox_OutsidePressClosesAndFocusesTheClickedElement()
    {
        using var surface = new VisualHost();
        using var combo = CreateComboBox();
        var button = new VC.Button { Text = "Elsewhere" };
        var root = new VC.StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        root.Add(combo);
        root.Add(button);
        surface.Root = root;
        Layout(surface, 320, 240);

        var comboCenter = Center(combo.AbsoluteBounds);
        surface.ProcessPointerDown(comboCenter, MouseButtons.Left);
        surface.ProcessPointerUp(comboCenter, MouseButtons.Left);
        Assert.IsTrue(combo.IsDropDownOpen);

        var buttonCenter = Center(button.AbsoluteBounds);
        surface.ProcessPointerDown(buttonCenter, MouseButtons.Left);
        surface.ProcessPointerUp(buttonCenter, MouseButtons.Left);

        Assert.IsFalse(combo.IsDropDownOpen, "an outside press closes the drop-down");
        Assert.AreSame(button, surface.FocusedElement, "focus moves to the clicked element");
    }

    [TestMethod]
    public void ComboBox_CreatesNoAdditionalWindows()
    {
        using var surface = new VisualHost();
        using var combo = CreateComboBox();
        var root = new VC.StackPanel();
        root.Add(combo);
        surface.Root = root;
        Layout(surface, 320, 240);

        combo.IsDropDownOpen = true;
        Layout(surface, 320, 240);

        Assert.IsEmpty(surface.Controls, "the drop-down lives in the overlay, not in a child HWND");
    }

    [TestMethod]
    public void VisualXaml_CompilesInlineComboBoxItems()
    {
        const string source = """
            namespace Probe;

            public partial class ProbeVisualView : VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel
            {
            }
            """;

        const string xaml = """
            <v:StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:v="clr-namespace:VeloxDev.WinForms.Xaml.Visuals.Controls;assembly=VeloxDev.WinForms"
                x:Class="Probe.ProbeVisualView">
              <v:ComboBox x:Name="picker" Width="200" SelectedIndex="0">
                <v:ComboBoxItem>Alpha</v:ComboBoxItem>
                <v:ComboBoxItem>Beta</v:ComboBoxItem>
              </v:ComboBox>
            </v:StackPanel>
            """;

        var (run, output) = GeneratorProbe.RunAndCompile(
            [new XamlFileGenerator()],
            source,
            "VisualComboBoxXamlProbe",
            new InMemoryAdditionalText("ProbeVisualView.xaml", xaml));

        Assert.IsEmpty(run.Diagnostics, GeneratorProbe.Describe(run.Diagnostics));
        var generated = run.GeneratedText();
        StringAssert.Contains(generated, "picker.Items.Add(");
        StringAssert.Contains(generated, "Alpha");
        Assert.IsFalse(generated.Contains("picker.Add(", StringComparison.Ordinal), "items go to Items, not Children");

        var errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.IsEmpty(errors, GeneratorProbe.Describe(errors));
    }

    private static VC.ComboBox CreateComboBox()
    {
        var combo = new VC.ComboBox { Width = 160f, SelectedIndex = 0 };
        combo.Items.Add("One");
        combo.Items.Add("Two");
        combo.Items.Add("Three");
        return combo;
    }

    private static PointF Center(RectangleF bounds)
        => new(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));

    private static void Layout(VisualHost surface, int width, int height)
    {
        surface.Size = new Size(width, height);
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }
}
