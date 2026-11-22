using System.Drawing;
using System.Drawing.Drawing2D;
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
    public void StackPanel_KeepsAnExplicitCrossAxisSize()
    {
        using var panel = new V.StackPanel { Size = new Size(200, 100), Orientation = Orientation.Horizontal };
        using var child = new V.Border { Width = 30, Height = 20, Margin = V.Thickness.Empty };
        panel.Controls.Add(child);

        panel.PerformLayout();

        // 横向堆叠的交叉轴是整条带高；显式 Height 不能被 Stretch 覆盖，且应居中。
        Assert.AreEqual(new Rectangle(0, 40, 30, 20), child.Bounds);
    }

    [TestMethod]
    public void AnInheritedBackColor_DoesNotBecomeAPaintedBackground()
    {
        using var parent = new V.Panel { BackColor = Color.FromArgb(0xFF, 0x12, 0x34, 0x56) };
        using var child = new V.TextBlock();
        parent.Controls.Add(child);

        Assert.IsNull(child.Background, "an ambient BackColor is not an authored background");
    }

    [TestMethod]
    public void Border_CentersASingleChildWithCenterAlignment()
    {
        using var border = new V.Border { Size = new Size(100, 60), Padding = V.Thickness.Empty };
        using var child = new V.TextBlock
        {
            Text = "centered",
            Margin = V.Thickness.Empty,
            HorizontalAlignment = V.HorizontalAlignment.Stretch,
            VerticalAlignment = V.VerticalAlignment.Center,
        };
        border.Controls.Add(child);

        border.PerformLayout();

        Assert.AreEqual(100, child.Width);
        Assert.IsLessThan(60, child.Height, "the child keeps its measured height");
        Assert.AreEqual((60 - child.Height) / 2, child.Top, "the child is centered in the content slot");
    }

    [TestMethod]
    public void TextBlock_UsesTheAvailableWidthForWrapping()
    {
        using var text = new V.TextBlock
        {
            Text = "A wrapping line keeps every word inside the width it is measured with.",
            TextWrapping = V.TextWrapping.Wrap,
        };

        var narrow = text.Measure(new Size(120, 0));
        var wide = text.Measure(new Size(2000, 0));

        Assert.IsGreaterThan(wide.Height * 2, narrow.Height, "a narrow measure wraps onto more lines");
        Assert.IsLessThan(120, narrow.Width, "the preferred width stays inside the available width");
    }

    [TestMethod]
    public void StackPanel_KeepsItsOwnExplicitSize()
    {
        using var panel = new V.StackPanel { Width = 250, Height = 100 };
        using var child = new V.Border { Height = 20, Margin = V.Thickness.Empty };
        panel.Controls.Add(child);

        Assert.AreEqual(new Size(250, 100), panel.Measure(new Size(1000, 1000)));
    }

    [TestMethod]
    public void Border_MeasuresItsChildPlusPadding()
    {
        using var border = new V.Border { Padding = new V.Thickness(10), Width = 200 };
        using var text = new V.TextBlock
        {
            Text = "A wrapping line keeps every word inside the width it is measured with.",
            TextWrapping = V.TextWrapping.Wrap,
            Margin = V.Thickness.Empty,
        };
        border.Controls.Add(text);

        var measured = border.Measure(new Size(200, 0));

        Assert.AreEqual(200, measured.Width);
        Assert.IsGreaterThan(40, measured.Height, "10px padding plus at least two wrapped lines");
    }

    [TestMethod]
    public void ExplicitChildSize_InvalidatesTheRootLayout()
    {
        using var panel = new V.StackPanel { Size = new Size(200, 100), Spacing = 0 };
        using var child = new V.Border
        {
            Width = 10,
            Height = 20,
            Margin = V.Thickness.Empty,
            HorizontalAlignment = V.HorizontalAlignment.Left,
        };
        panel.Controls.Add(child);
        panel.PerformLayout();

        child.Width = 40;

        Assert.AreEqual(new Rectangle(0, 0, 40, 20), child.Bounds);
    }

    [TestMethod]
    public void Measure_ReusesTheCacheUntilTheControlIsInvalidated()
    {
        using var text = new V.TextBlock { Text = "a" };
        var first = text.Measure(new Size(300, 0));

        text.Text = "a much longer line that wraps onto several lines when it is measured";

        var second = text.Measure(new Size(300, 0));
        Assert.IsGreaterThan(first.Width, second.Width);
    }

    [TestMethod]
    public void Grid_MeasuresAgainstTheAvailableConstraint()
    {
        using var grid = new V.Grid();

        Assert.AreEqual(new Size(200, 100), grid.Measure(new Size(200, 100)));
    }

    [TestMethod]
    public void Measure_CachesTheResultAndInvalidatesTheCache()
    {
        using var probe = new CountingControl();

        probe.Measure(new Size(100, 100));
        probe.Measure(new Size(100, 100));

        Assert.AreEqual(1, probe.Measures, "the second measure hits the cache");

        probe.Width = 40;
        probe.Measure(new Size(100, 100));

        Assert.AreEqual(2, probe.Measures, "a size change invalidates the path");
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
    public void TextBox_TextAndPasswordAreIndependent()
    {
        using var textBox = new V.TextBox();
        textBox.Text = "hello";
        Assert.AreEqual("hello", textBox.Text);

        using var passwordBox = new V.PasswordBox();
        passwordBox.Password = "secret";
        Assert.AreEqual("secret", passwordBox.Password);
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

        Assert.AreEqual(Color.Red, button.Background?.ApproximateColor);
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

    [TestMethod]
    public void ContentControl_ATemplateFactoryReturningNull_DoesNotThrow()
    {
        using var control = new V.ContentControl();

        control.Content = "text";
        control.ContentTemplate = new DataTemplate(() => null!);
        control.PerformLayout();

        Assert.AreEqual(0, control.Controls.Count);
    }

    [TestMethod]
    public void ItemsControl_ATemplateFactoryReturningNull_FallsBackToATextLine()
    {
        using var control = new V.ItemsControl
        {
            ItemTemplate = new DataTemplate(() => null!),
            ItemsSource = new[] { "a", "b" },
        };

        control.PerformLayout();

        Assert.HasCount(2, control.Controls);
        Assert.IsTrue(control.Controls.Cast<System.Windows.Forms.Control>().All(child => child is V.TextBlock));
    }

    [TestMethod]
    public void Path_UniformStretch_FitsTheGeometryIntoTheContentBounds()
    {
        using var path = new V.Shapes.Path
        {
            Data = "M100,100 L200,100 L200,200 Z",
            Stretch = V.Stretch.Uniform,
        };

        var createPath = typeof(V.Shapes.Path).GetMethod(
            "CreatePath",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Path.CreatePath moved; update this test.");

        using var geometry = (GraphicsPath)createPath.Invoke(path, [new RectangleF(0f, 0f, 20f, 20f)])!;
        var bounds = geometry.GetBounds();

        Assert.IsTrue(bounds.Left >= -0.5f && bounds.Top >= -0.5f, $"geometry starts outside the control: {bounds}");
        Assert.IsTrue(bounds.Right <= 20.5f && bounds.Bottom <= 20.5f, $"geometry ends outside the control: {bounds}");
    }

    [TestMethod]
    public void Path_MultipleMoveCommands_StartSeparateFigures()
    {
        using var path = new V.Shapes.Path { Data = "M0,0 L10,0 M20,20 L30,20" };

        var createPath = typeof(V.Shapes.Path).GetMethod(
            "CreatePath",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Path.CreatePath moved; update this test.");

        using var geometry = (GraphicsPath)createPath.Invoke(path, [new RectangleF(0f, 0f, 100f, 100f)])!;

        // PathPointType.Start 是 0；第二个 M 必须开启新图，不能被当成 L 连过去。
        var figureStarts = geometry.PathTypes.Count(type => (type & 0x07) == 0);
        Assert.AreEqual(2, figureStarts, "the second M must start a new figure");
    }

    [TestMethod]
    public void ATransparentControl_PaintsItsParentBackgroundUnderItself()
    {
        using var parent = new V.Border { Background = Color.Red, Size = new Size(40, 40) };
        using var child = new V.TextBlock { Size = new Size(20, 20), Location = new Point(5, 5) };
        parent.Controls.Add(child);

        using var bitmap = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var paintBackground = typeof(V.Control).GetMethod(
                "OnPaintBackground",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Control.OnPaintBackground moved; update this test.");

            paintBackground.Invoke(
                child,
                [new System.Windows.Forms.PaintEventArgs(graphics, new Rectangle(0, 0, 20, 20))]);
        }

        Assert.AreEqual(Color.Red.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
    }

    private sealed class CountingControl : V.Control
    {
        public int Measures { get; private set; }

        protected override Size MeasureCore(Size availableSize)
        {
            Measures++;
            return base.MeasureCore(availableSize);
        }
    }

    private sealed class ProbeButton : V.Button
    {
        public void Press()
            => OnMouseDown(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 1, 1, 0));

        public void Release()
            => OnMouseUp(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 1, 1, 0));
    }
}
