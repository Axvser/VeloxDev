using System.Drawing;
using VeloxDev.WinForms.Xaml;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The WinForms control library that mirrors the WPF/Avalonia basics: layout panels, attached coordinates,
/// content controls and the shared dependency-property pipeline.
/// </summary>
[STATestClass]
public sealed class ControlLibraryTests
{
    [TestMethod]
    public void StackPanel_StacksVerticallyWithSpacingAndStretch()
    {
        using var panel = new V.StackPanel { Size = new Size(200, 200), Spacing = 4 };
        using var first = new V.Border { Height = 20, Margin = V.Thickness.Empty };
        using var second = new V.Border { Height = 30, Margin = V.Thickness.Empty };

        panel.Controls.Add(first);
        panel.Controls.Add(second);
        panel.PerformLayout();

        Assert.AreEqual(new Rectangle(0, 0, 200, 20), first.Bounds);
        Assert.AreEqual(new Rectangle(0, 24, 200, 30), second.Bounds);
    }

    [TestMethod]
    public void StackPanel_MeasuresTheSumOfItsChildren()
    {
        using var panel = new V.StackPanel { Spacing = 2 };
        using var first = new V.Border { Height = 20, Margin = V.Thickness.Empty };
        using var second = new V.Border { Height = 30, Margin = V.Thickness.Empty };
        panel.Controls.Add(first);
        panel.Controls.Add(second);

        Assert.AreEqual(52, panel.Measure(new Size(100, 100)).Height);
    }

    [TestMethod]
    public void StackPanel_LaysOutANativeChild()
    {
        using var panel = new V.StackPanel { Size = new Size(100, 50) };
        using var native = new System.Windows.Forms.Button
        {
            Height = 20,
            Margin = System.Windows.Forms.Padding.Empty,
        };

        panel.Controls.Add(native);
        panel.PerformLayout();

        Assert.AreEqual(new Rectangle(0, 0, 100, 20), native.Bounds);
    }

    [TestMethod]
    public void WrapPanel_WrapsAtTheAvailableWidth()
    {
        using var panel = new V.WrapPanel { Size = new Size(100, 100) };
        using var first = new V.Border { Width = 60, Height = 10, Margin = V.Thickness.Empty };
        using var second = new V.Border { Width = 60, Height = 20, Margin = V.Thickness.Empty };
        panel.Controls.Add(first);
        panel.Controls.Add(second);
        panel.PerformLayout();

        Assert.AreEqual(new Rectangle(0, 0, 60, 10), first.Bounds);
        Assert.AreEqual(new Rectangle(0, 10, 60, 20), second.Bounds);
    }

    [TestMethod]
    public void DockPanel_FillsTheLastChild()
    {
        using var panel = new V.DockPanel { Size = new Size(200, 100) };
        using var top = new V.Border { Height = 20, Margin = V.Thickness.Empty };
        using var fill = new V.Border { Margin = V.Thickness.Empty };
        V.DockPanel.SetDock(top, V.DockPlacement.Top);

        panel.Controls.Add(top);
        panel.Controls.Add(fill);
        panel.PerformLayout();

        Assert.AreEqual(new Rectangle(0, 0, 200, 20), top.Bounds);
        Assert.AreEqual(new Rectangle(0, 20, 200, 80), fill.Bounds);
    }

    [TestMethod]
    public void Grid_UsesDefinitionsAndAttachedCellCoordinates()
    {
        using var grid = new V.Grid { Size = new Size(200, 100) };
        grid.RowDefinitions.Add(new V.RowDefinition { Height = new V.GridLength(20) });
        grid.RowDefinitions.Add(new V.RowDefinition { Height = V.GridLength.Star });
        grid.ColumnDefinitions.Add(new V.ColumnDefinition { Width = new V.GridLength(50) });
        grid.ColumnDefinitions.Add(new V.ColumnDefinition { Width = V.GridLength.Star });

        using var cell = new V.Border { Margin = V.Thickness.Empty };
        V.Grid.SetRow(cell, 1);
        V.Grid.SetColumn(cell, 1);
        grid.Controls.Add(cell);
        grid.PerformLayout();

        Assert.AreEqual(new Rectangle(50, 20, 150, 80), cell.Bounds);
    }

    [TestMethod]
    public void Canvas_UsesAttachedLeftAndTop()
    {
        using var canvas = new V.Canvas { Size = new Size(200, 100) };
        using var child = new V.Border { Width = 30, Height = 40, Margin = V.Thickness.Empty };
        V.Canvas.SetLeft(child, 10);
        V.Canvas.SetTop(child, 20);

        canvas.Controls.Add(child);
        canvas.PerformLayout();

        Assert.AreEqual(new Rectangle(10, 20, 30, 40), child.Bounds);
    }

    [TestMethod]
    public void Button_RaisesClickAndToggleButtonChangesItsCheckedState()
    {
        using var button = new V.Button { Content = "OK" };
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        button.PerformClick();
        Assert.AreEqual(1, clicks);

        using var toggle = new V.ToggleButton();
        toggle.PerformClick();
        Assert.IsTrue(toggle.IsChecked);
        toggle.PerformClick();
        Assert.IsFalse(toggle.IsChecked);
    }

    [TestMethod]
    public void Button_RaisesOneClickPerPress()
    {
        using var button = new ProbeButton { Size = new Size(80, 30) };
        var clicks = 0;
        button.Click += (_, _) => clicks++;

        button.Press();
        button.Release();

        Assert.AreEqual(1, clicks);
    }

    [TestMethod]
    public void RadioButton_UnchecksItsGroupSibling()
    {
        using var host = new V.Panel();
        using var first = new V.RadioButton { GroupName = "mode" };
        using var second = new V.RadioButton { GroupName = "mode" };
        host.Controls.Add(first);
        host.Controls.Add(second);

        first.PerformClick();
        Assert.IsTrue(first.IsChecked);
        second.PerformClick();

        Assert.IsFalse(first.IsChecked);
        Assert.IsTrue(second.IsChecked);
    }

    [TestMethod]
    public void TextBox_TextAndPasswordStayInSync()
    {
        using var textBox = new V.TextBox();
        textBox.Text = "hello";
        Assert.AreEqual("hello", textBox.Text);

        using var passwordBox = new V.PasswordBox();
        passwordBox.Password = "secret";
        Assert.AreEqual("secret", passwordBox.Text);
    }

    [TestMethod]
    public void Selector_ItemsAndSelectionStayInSync()
    {
        using var list = new V.ListBox { ItemsSource = new[] { "a", "b", "c" } };

        Assert.AreEqual(3, list.ItemCount);
        list.SelectedIndex = 1;
        Assert.AreEqual("b", list.SelectedItem);

        list.SelectedItem = "c";
        Assert.AreEqual(2, list.SelectedIndex);
    }

    [TestMethod]
    public void Slider_ClampsItsValueToTheRange()
    {
        using var slider = new V.Slider { Minimum = 10, Maximum = 20, Value = 200 };
        Assert.AreEqual(20d, slider.Value);

        slider.Value = -5;
        Assert.AreEqual(10d, slider.Value);
    }

    [TestMethod]
    public void NewControl_UsesTheStyleAndTriggerPipeline()
    {
        using var button = new V.ToggleButton();
        var style = new Style { TargetType = typeof(V.ToggleButton) };
        style.Setters.Add(new Setter(V.Control.BackgroundProperty, Color.Red));
        style.Setters.Add(new Setter(V.Control.MarginProperty, new V.Thickness(5f)));
        var trigger = new Trigger(V.ToggleButton.IsCheckedProperty, true);
        trigger.Setters.Add(new Setter(V.Button.CornerRadiusProperty, 8f));
        style.Triggers.Add(trigger);
        ((System.Windows.Forms.Control)button).Margin = new System.Windows.Forms.Padding(7);
        button.Style = style;

        Assert.AreEqual(Color.Red, button.Background);
        Assert.AreEqual(new V.Thickness(5f), button.Margin);
        Assert.AreEqual(4f, button.CornerRadius);

        button.IsChecked = true;
        Assert.AreEqual(8f, button.CornerRadius);

        button.IsChecked = false;
        Assert.AreEqual(4f, button.CornerRadius);

        button.Style = null;
        Assert.AreEqual(new V.Thickness(7f), button.Margin);
    }

    [TestMethod]
    public void GridLength_ParsesTheWpfLiterals()
    {
        Assert.IsTrue(V.GridLength.TryParse("Auto", out var auto));
        Assert.IsTrue(auto.IsAuto);
        Assert.IsTrue(V.GridLength.TryParse("2*", out var star));
        Assert.AreEqual(2d, star.Value);
        Assert.IsTrue(star.IsStar);
        Assert.AreEqual(42d, V.GridLength.Parse("42").Value);
    }

    private sealed class ProbeButton : V.Button
    {
        public void Press()
            => OnMouseDown(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 1, 1, 0));

        public void Release()
            => OnMouseUp(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 1, 1, 0));
    }
}
