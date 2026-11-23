using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlVerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Full-parity phase 3: WPF-shaped control templates for the self-drawn controls, inherited typography and
/// foreground conversion on the remaining controls, and samplers for the render properties.
/// </summary>
[STATestClass]
public sealed class VisualTemplatingAndRenderAnimationTests
{
    [TestMethod]
    public void ButtonTemplate_HasPartsExposesStateAndReplacesTheSelfDrawnChrome()
    {
        using var surface = new VisualHost { Size = new Size(180, 100), DpiScaleOverride = 1f };
        var button = new VC.Button { Width = 140, Height = 48, Text = "go" };
        VC.Border? indicator = null;

        var template = new VisualControlTemplate(scope =>
        {
            var root = new VC.Border { Background = Color.Lime, Padding = default };
            indicator = new VC.Border
            {
                Width = 8f,
                Height = 8f,
                HorizontalAlignment = XamlHorizontalAlignment.Left,
                VerticalAlignment = XamlVerticalAlignment.Top,
            };
            root.Child = indicator;
            scope.Register("PART_Chrome", root);
            scope.Register("PART_Indicator", indicator);
            return root;
        })
        {
            TargetType = typeof(VC.Button),
        };

        var hover = new VisualTrigger(VC.ButtonBase.IsPointerOverProperty, true) { TargetName = "PART_Indicator" };
        hover.Setters.Add(new VisualSetter(VC.Border.BackgroundProperty, new SolidColorBrush(Color.Red)));
        template.Triggers.Add(hover);

        button.Template = template;
        surface.Root = button;
        Layout(surface, 180, 100);

        Assert.AreSame(button.TemplateRoot, button.GetTemplateChild<VC.Border>("PART_Chrome"));
        Assert.AreSame(indicator, button.GetTemplateChild<VC.Border>("PART_Indicator"),
            "a named template part resolves through the instance scope");

        Assert.AreEqual(Color.Lime.ToArgb(), RenderPixel(surface, 90, 50).ToArgb(),
            "the template root paints the whole control instead of the self-drawn button chrome");

        var bounds = button.AbsoluteBounds;
        surface.ProcessPointerMove(new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f)));

        Assert.IsTrue(button.IsPointerOver, "the pointer entered the templated button");
        Assert.IsNotNull(indicator!.Background, "the hover trigger wrote the named part");
        Assert.AreEqual(Color.Red.ToArgb(), indicator!.Background!.ApproximateColor.ToArgb(),
            "a template trigger on IsPointerOver writes the named part");

        surface.ProcessPointerMove(new PointF(bounds.Right + 40f, bounds.Bottom + 40f));

        Assert.IsFalse(button.IsPointerOver, "the pointer left the button");
        Assert.IsNull(indicator!.Background, "the trigger's value is removed when it exits");

        button.Template = null;
        Layout(surface, 180, 100);

        Assert.IsNull(button.TemplateRoot, "clearing the template removes the root");
        Assert.IsNull(button.GetTemplateChild<VC.Border>("PART_Chrome"), "the part scope is cleared with the template");
        Assert.AreNotEqual(Color.Lime.ToArgb(), RenderPixel(surface, 90, 50).ToArgb(),
            "without a template the self-drawn button chrome is visible again");
    }

    [TestMethod]
    public void ListBoxTemplate_HostsTheRealizedItemsAndExposesSelectionToATrigger()
    {
        using var surface = new VisualHost { Size = new Size(160, 100), DpiScaleOverride = 1f };
        var list = new VC.ListBox { Width = 140, Height = 90 };
        list.ItemsSource = new[] { "alpha", "beta" };
        VC.StackPanel? host = null;

        var template = new VisualControlTemplate(scope =>
        {
            host = new VC.StackPanel();
            VC.ItemsControl.SetIsItemsHost(host, true);
            scope.Register("PART_ItemsHost", host);
            return host;
        })
        {
            TargetType = typeof(VC.ListBox),
        };

        var selected = new VisualTrigger(VC.ListBox.SelectedIndexProperty, 1) { TargetName = "PART_ItemsHost" };
        selected.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, new SolidColorBrush(Color.Gold)));
        template.Triggers.Add(selected);

        list.Template = template;
        surface.Root = list;
        Layout(surface, 160, 100);

        Assert.AreSame(host, list.TemplateRoot);
        Assert.AreSame(host, list.GetTemplateChild<VC.StackPanel>("PART_ItemsHost"));
        Assert.AreEqual(2, host!.Children.Count, "the realized items moved into the template's items host");
        Assert.AreEqual(2, list.Items.Count);

        Assert.IsNull(host!.Background, "the selection trigger is inactive while nothing is selected");

        list.SelectedIndex = 1;
        Assert.IsNotNull(host!.Background, "the selection trigger is active at index 1");
        Assert.AreEqual(Color.Gold.ToArgb(), host!.Background!.ApproximateColor.ToArgb(),
            "a template trigger reads ListBox selection state");

        list.SelectedIndex = -1;
        Assert.IsNull(host!.Background, "clearing the selection removes the trigger value");
    }

    [TestMethod]
    public void RichTextBox_InheritsTypographyAndConvertsAHexForeground()
    {
        var panel = new VC.StackPanel();
        var rich = new VC.RichTextBox { Width = 200f, Height = 80f };
        panel.Add(rich);

        panel.FontSize = 20d;
        panel.FontFamily = "Consolas";
        panel.FontWeight = FontWeight.Bold;

        Assert.AreEqual(20f, rich.Font.Size, 0.1f, "the base font follows the inherited size");
        Assert.IsTrue(
            string.Equals("Consolas", rich.Font.FontFamily.Name, StringComparison.OrdinalIgnoreCase),
            "the base font follows the inherited family");
        Assert.IsTrue((rich.Font.Style & FontStyle.Bold) != 0, "the base font follows the inherited weight");

        VC.RichTextBox.ForegroundProperty.SetValue(rich, "#FF00FF00", BindingPriority.LocalValue);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00), rich.Foreground,
            "a hex foreground converts to a Color on the Color-typed property");
    }

    [TestMethod]
    public void ForegroundTrigger_MatchesHexAndColorValues()
    {
        var button = new VC.Button();
        var style = new VisualStyle { TargetType = typeof(VC.Button) };
        var trigger = new VisualTrigger(VC.Button.ForegroundProperty, "#FF112233");
        trigger.Setters.Add(new VisualSetter(VisualElement.OpacityProperty, 0.5f));
        style.Triggers.Add(trigger);
        button.Style = style;
        Assert.AreEqual(1d, button.Opacity, 0.001d, "the trigger is inactive without a matching foreground");

        button.Foreground = Color.FromArgb(0xFF, 0x11, 0x22, 0x33);
        Assert.AreEqual(0.5d, button.Opacity, 0.001d,
            "a hex trigger value matches the brush stored for a Color assignment");

        button.Foreground = Color.Red;
        Assert.AreEqual(1d, button.Opacity, 0.001d, "a different foreground exits the trigger");
    }

    [TestMethod]
    public void ForegroundTwoWay_OnAColorPropertyWritesBothDirections()
    {
        var model = new ColorModel { Color = Color.Red };
        using var rich = new VC.RichTextBox();
        rich.SetBinding(
            VC.RichTextBox.ForegroundProperty,
            new Binding(nameof(ColorModel.Color)) { Source = model, Mode = BindingMode.TwoWay });

        Assert.AreEqual(Color.Red.ToArgb(), rich.Foreground.ToArgb(), "the source drives the Color-typed property");

        rich.Foreground = Color.Blue;
        Assert.AreEqual(Color.Blue.ToArgb(), model.Color.ToArgb(), "a target write flows back to the Color source");

        model.Color = Color.Green;
        Assert.AreEqual(Color.Green.ToArgb(), rich.Foreground.ToArgb(), "a source change flows to the target again");
    }

    [TestMethod]
    public void RenderProperties_AnimateTransformAndClip()
    {
        using var surface = new VisualHost { Size = new Size(160, 120), DpiScaleOverride = 1f };
        var border = new VC.Border { Width = 100f, Height = 60f, Clip = new RectangleF(0f, 0f, 10f, 10f) };
        surface.Root = border;
        XamlTestSurface.EnsureHandle(surface);

        VisualTransition.Animate(
            border,
            VisualElement.TransformProperty,
            new Matrix(1f, 0f, 0f, 1f, 40f, 0f),
            new TransitionEffect { Duration = TimeSpan.FromMilliseconds(80), Ease = Eases.Default });

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => border.Transform.OffsetX >= 39.5f),
            $"a Matrix transform animates through its sampler; OffsetX was {border.Transform.OffsetX}");

        VisualTransition.Animate(
            border,
            VisualElement.ClipProperty,
            new RectangleF(0f, 0f, 100f, 100f),
            new TransitionEffect { Duration = TimeSpan.FromMilliseconds(80), Ease = Eases.Default });

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => border.Clip is { Width: >= 99f }),
            $"a nullable RectangleF clip animates; clip was {border.Clip}");
    }

    [TestMethod]
    public void EffectProperty_WithNoSampler_IsRejectedExplicitly()
    {
        var border = new VC.Border();

        var thrown = Assert.ThrowsExactly<NotSupportedException>(() =>
            VisualTransition.Animate(border, VisualElement.EffectProperty, new DropShadowEffect()));

        StringAssert.Contains(thrown.Message, "no interpolator",
            "the rejection names the unsupported property instead of silently holding its value");
    }

    [TestMethod]
    public void NativeTextBox_StaysSelfDrawnAndRejectsATemplate()
    {
        using var box = new VC.TextBox { Text = "abc" };
        var style = new VisualStyle { TargetType = typeof(VC.TextBox) };
        style.Template = new VisualControlTemplate(_ => new VC.Border());

        Assert.IsNull(typeof(VC.TextBox).GetProperty("Template"),
            "the native-island editor has no public Template property");
        Assert.ThrowsExactly<NotSupportedException>(() => box.Style = style);

        box.Text = "still editable";
        Assert.AreEqual("still editable", box.Text, "rejecting the template leaves the editor working");
    }

    private static void Layout(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }

    private static Color RenderPixel(VisualHost surface, int x, int y)
    {
        using var bitmap = new Bitmap(surface.Size.Width, surface.Size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, surface.Size);
        }

        return bitmap.GetPixel(x, y);
    }

    private sealed class ColorModel : INotifyPropertyChanged
    {
        private Color _color = Color.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Color Color
        {
            get => _color;
            set
            {
                if (_color == value)
                {
                    return;
                }

                _color = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
            }
        }
    }
}
