using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlBinding = VeloxDev.WinForms.Xaml.Binding;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlVerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Full-parity phase 2b: render properties as dependency properties, WPF-named typography with inheritance,
/// effective-enabled focus/input routing with tab order, and a template-owned scroll-bar thumb.
/// </summary>
[STATestClass]
public sealed class VisualRenderTypographyInputParityTests
{
    [TestMethod]
    public void StyleSetter_ChangesOpacityTransformAndClip()
    {
        var border = new VC.Border();
        var style = new VisualStyle { TargetType = typeof(VC.Border) };
        style.Setters.Add(new VisualSetter(VisualElement.OpacityProperty, 0.4d));
        style.Setters.Add(new VisualSetter(VisualElement.TransformProperty, new Matrix(1f, 0f, 0f, 1f, 12f, 0f)));
        style.Setters.Add(new VisualSetter(VisualElement.ClipProperty, new RectangleF(1f, 2f, 3f, 4f)));

        border.Style = style;

        Assert.AreEqual(0.4d, border.Opacity, 0.0001d);
        Assert.AreEqual(12f, border.Transform.OffsetX, 0.0001f);
        Assert.AreEqual(new RectangleF(1f, 2f, 3f, 4f), border.Clip);

        border.Style = null;

        Assert.AreEqual(1d, border.Opacity, 0.0001d, "removing the style restores the default opacity");
        Assert.IsTrue(border.Transform.IsIdentity, "removing the style restores the identity transform");
        Assert.IsNull(border.Clip, "removing the style clears the clip");
    }

    [TestMethod]
    public void Trigger_ChangesOpacityAndClip()
    {
        var button = new VC.Button();
        var style = new VisualStyle { TargetType = typeof(VC.Button) };
        style.Setters.Add(new VisualSetter(VisualElement.OpacityProperty, 1d));

        var trigger = new VisualTrigger(VisualElement.IsEnabledProperty, false);
        trigger.Setters.Add(new VisualSetter(VisualElement.OpacityProperty, 0.25d));
        trigger.Setters.Add(new VisualSetter(VisualElement.ClipProperty, new RectangleF(1f, 2f, 3f, 4f)));
        style.Triggers.Add(trigger);

        button.Style = style;
        Assert.AreEqual(1d, button.Opacity, 0.0001d);

        button.IsEnabled = false;
        Assert.AreEqual(0.25d, button.Opacity, 0.0001d, "the trigger overrides the style setter");
        Assert.AreEqual(new RectangleF(1f, 2f, 3f, 4f), button.Clip);

        button.IsEnabled = true;
        Assert.AreEqual(1d, button.Opacity, 0.0001d, "exiting the trigger reveals the style setter");
        Assert.IsNull(button.Clip, "the trigger's clip is removed when it exits");
    }

    [TestMethod]
    public void RenderProperties_AreBindable()
    {
        var model = new RenderModel();
        var border = new VC.Border();

        border.SetBinding(VisualElement.OpacityProperty, new XamlBinding(nameof(RenderModel.Opacity)) { Source = model });
        border.SetBinding(VisualElement.TransformProperty, new XamlBinding(nameof(RenderModel.Transform)) { Source = model });
        border.SetBinding(VisualElement.ClipProperty, new XamlBinding(nameof(RenderModel.Clip)) { Source = model });

        Assert.AreEqual(0.8d, border.Opacity, 0.0001d);
        Assert.AreEqual(7f, border.Transform.OffsetY, 0.0001f);
        Assert.AreEqual(new RectangleF(0f, 0f, 5f, 6f), border.Clip);

        model.Opacity = 0.3d;
        model.Transform = new Matrix(1f, 0f, 0f, 1f, 0f, 20f);
        model.Clip = new RectangleF(2f, 2f, 8f, 8f);

        Assert.AreEqual(0.3d, border.Opacity, 0.0001d);
        Assert.AreEqual(20f, border.Transform.OffsetY, 0.0001f);
        Assert.AreEqual(new RectangleF(2f, 2f, 8f, 8f), border.Clip);
    }

    [TestMethod]
    public void Opacity_AnimatesThroughTheTransitionLayer()
    {
        using var surface = new VisualHost { Size = new Size(160, 120), DpiScaleOverride = 1f };
        var border = new VC.Border { Opacity = 1d, Background = Color.Red };
        surface.Root = border;
        XamlTestSurface.EnsureHandle(surface);

        VisualTransition.Animate(
            border,
            VisualElement.OpacityProperty,
            0.4d,
            new TransitionEffect { Duration = TimeSpan.FromMilliseconds(80), Ease = Eases.Default });

        Assert.IsTrue(
            XamlTestSurface.PumpUntil(() => Math.Abs(border.Opacity - 0.4d) < 0.01d),
            $"the opacity animation should finish at 0.4; opacity was {border.Opacity}");
    }

    [TestMethod]
    public void LiteralOpacityAndTransform_DriveTheBaseHitTest()
    {
        var border = new VC.Border
        {
            Bounds = new RectangleF(0f, 0f, 20f, 20f),
            Background = Color.Red,
        };

        border.Opacity = 0d;
        Assert.IsFalse(border.HitTest(new PointF(10f, 10f)), "the base hit test reads the DP-backed opacity");

        border.Opacity = 1d;
        border.Transform = new Matrix(1f, 0f, 0f, 1f, 30f, 0f);
        Assert.IsTrue(border.HitTest(new PointF(35f, 10f)), "the base hit test follows the DP-backed transform");
        Assert.IsFalse(border.HitTest(new PointF(5f, 10f)), "the untransformed point no longer hits");
    }

    [TestMethod]
    public void Typography_ComposesFontSizeFamilyAndWeight()
    {
        var block = new VC.TextBlock();
        Assert.AreEqual(VisualElement.DefaultFont.Size, block.Font.Size, 0.1f, "no override keeps the control font");

        block.FontSize = 20d;
        Assert.AreEqual(20f, block.Font.Size, 0.1f, "FontSize rebuilds the control font");

        block.FontWeight = FontWeight.Bold;
        Assert.IsTrue((block.Font.Style & FontStyle.Bold) != 0, "FontWeight.Bold selects a bold face");

        block.FontFamily = "Consolas";
        Assert.IsTrue(
            string.Equals("Consolas", block.Font.FontFamily.Name, StringComparison.OrdinalIgnoreCase),
            $"FontFamily selects the named family; got {block.Font.FontFamily.Name}");
    }

    [TestMethod]
    public void Typography_InheritsFromAncestors()
    {
        var panel = new VC.StackPanel();
        var block = new VC.TextBlock();
        panel.Add(block);

        panel.FontSize = 18d;
        panel.FontFamily = "Consolas";
        panel.FontWeight = FontWeight.Bold;

        Assert.AreEqual(18f, block.Font.Size, 0.1f, "FontSize inherits from the parent");
        Assert.IsTrue(
            string.Equals("Consolas", block.Font.FontFamily.Name, StringComparison.OrdinalIgnoreCase),
            "FontFamily inherits from the parent");
        Assert.IsTrue((block.Font.Style & FontStyle.Bold) != 0, "FontWeight inherits from the parent");

        block.FontSize = 12d;
        Assert.AreEqual(12f, block.Font.Size, 0.1f, "a local value outranks the inherited size");
    }

    [TestMethod]
    public void Foreground_AcceptsColorHexAndBrush()
    {
        var block = new VC.TextBlock();
        Assert.AreEqual(typeof(XamlBrush), VC.TextBlock.ForegroundProperty.PropertyType,
            "the foreground property stores a brush");

        block.Foreground = Color.FromArgb(0xFF, 0x01, 0x02, 0x03);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x01, 0x02, 0x03), block.Foreground, "a Color literal is preserved");

        VC.TextBlock.ForegroundProperty.SetValue(block, "#FF00FF00", BindingPriority.LocalValue);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x00, 0xFF, 0x00), block.Foreground, "a hex string is converted");

        VC.TextBlock.ForegroundProperty.SetValue(block, new SolidColorBrush(Color.Blue), BindingPriority.LocalValue);
        Assert.AreEqual(Color.Blue, block.Foreground, "a brush reads back as its representative color");
    }

    [TestMethod]
    public void IsEnabled_InheritsAndBlocksFocusAndPointerInput()
    {
        using var surface = CreateSurface(140, 100);
        var panel = new VC.StackPanel { Width = 120, Height = 80 };
        var button = new VC.Button { Width = 100, Height = 30, Text = "click" };
        panel.Add(button);
        surface.Root = panel;
        Layout(surface, 140, 100);

        panel.IsEnabled = false;

        Assert.IsFalse(button.IsEnabled, "a child without its own value inherits the disabled state");
        Assert.IsFalse(button.IsEffectivelyEnabled, "the ancestor is part of the effective state");
        Assert.IsFalse(surface.FocusElement(button), "a child of a disabled container cannot take focus");

        button.IsEnabled = true;
        Assert.IsTrue(button.IsEnabled, "the local value is readable");
        Assert.IsFalse(button.IsEffectivelyEnabled, "a local true does not bypass a disabled ancestor");

        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var bounds = button.AbsoluteBounds;
        var center = new PointF(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);
        Assert.AreEqual(0, clicks, "a disabled ancestor blocks the pointer click");

        surface.ProcessKeyDown(Keys.Tab);
        Assert.IsNull(surface.FocusedElement, "a disabled ancestor blocks Tab traversal into the subtree");
    }

    [TestMethod]
    public void TabIndex_OrdersFocusTraversalAndIsTabStopIsHonoured()
    {
        using var surface = CreateSurface(200, 160);
        var root = new VC.StackPanel();
        var first = new VC.Button { Width = 160, Height = 24, TabIndex = 2, Text = "first" };
        var second = new VC.Button { Width = 160, Height = 24, TabIndex = 1, Text = "second" };
        var third = new VC.Button { Width = 160, Height = 24, Text = "third" };
        var skipped = new VC.Button { Width = 160, Height = 24, TabIndex = -1, IsTabStop = false, Text = "skip" };
        root.Add(first);
        root.Add(second);
        root.Add(third);
        root.Add(skipped);
        surface.Root = root;
        Layout(surface, 200, 160);

        surface.ProcessKeyDown(Keys.Tab);
        Assert.AreSame(third, surface.FocusedElement, "the default TabIndex 0 visits first");

        surface.ProcessKeyDown(Keys.Tab);
        Assert.AreSame(second, surface.FocusedElement, "TabIndex 1 follows");

        surface.ProcessKeyDown(Keys.Tab);
        Assert.AreSame(first, surface.FocusedElement, "TabIndex 2 follows");

        surface.ProcessKeyDown(Keys.Tab);
        Assert.AreSame(third, surface.FocusedElement, "IsTabStop=false is skipped and traversal wraps");
    }

    [TestMethod]
    public void ScrollBar_TrackOwnedThumbKeepsTheTemplateLayout()
    {
        using var surface = CreateSurface(16, 100);
        VC.Border? track = null;
        VC.Border? thumb = null;

        var template = new VisualControlTemplate(scope =>
        {
            track = new VC.Border { Padding = XamlThickness.Empty };
            thumb = new VC.Border
            {
                Width = 6f,
                Height = 20f,
                HorizontalAlignment = XamlHorizontalAlignment.Left,
                VerticalAlignment = XamlVerticalAlignment.Top,
                Padding = XamlThickness.Empty,
                Background = Color.Lime,
            };
            track.Child = thumb;
            scope.Register("PART_Track", track);
            scope.Register("PART_Thumb", thumb);
            return track;
        })
        {
            TargetType = typeof(VC.ScrollBar),
        };

        var bar = new VC.ScrollBar { Width = 8, Height = 100, Maximum = 1000, ViewportSize = 300, Template = template };
        surface.Root = bar;
        Layout(surface, 16, 100);

        Assert.IsNotNull(track);
        Assert.IsNotNull(thumb);
        Assert.AreSame(track, bar.GetTemplateChild<VC.Border>("PART_Track"));

        bar.Value = 350;
        Layout(surface, 16, 100);

        Assert.AreEqual(0f, thumb!.Bounds.Y, 0.5f,
            "a PART_Track-owned thumb keeps the template's own position instead of the computed 35 px offset");
        Assert.AreEqual(20f, thumb.Bounds.Height, 0.5f);
    }

    [TestMethod]
    public void ScrollBar_WithoutTrackStillPositionsTheThumb()
    {
        using var surface = CreateSurface(16, 100);
        VC.Border? thumb = null;

        var template = new VisualControlTemplate(scope =>
        {
            thumb = new VC.Border { Padding = XamlThickness.Empty, Background = Color.Lime };
            scope.Register("PART_Thumb", thumb);
            var root = new VC.Canvas();
            root.Add(thumb);
            return root;
        })
        {
            TargetType = typeof(VC.ScrollBar),
        };

        var bar = new VC.ScrollBar { Width = 8, Height = 100, Maximum = 1000, ViewportSize = 300, Template = template };
        surface.Root = bar;
        Layout(surface, 16, 100);

        bar.Value = 350;
        Layout(surface, 16, 100);

        Assert.AreEqual(35f, thumb!.Bounds.Y, 0.5f, "without a PART_Track the bar still moves the thumb itself");
    }

    private static VisualHost CreateSurface(int width, int height)
        => new() { Size = new Size(width, height), DpiScaleOverride = 1f };

    private static void Layout(VisualHost surface, int width, int height)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        surface.RenderTo(graphics, new Size(width, height));
    }

    private sealed class RenderModel : INotifyPropertyChanged
    {
        private double _opacity = 0.8d;
        private Matrix _transform = new(1f, 0f, 0f, 1f, 0f, 7f);
        private RectangleF? _clip = new(0f, 0f, 5f, 6f);

        public event PropertyChangedEventHandler? PropertyChanged;

        public double Opacity
        {
            get => _opacity;
            set
            {
                if (_opacity == value)
                {
                    return;
                }

                _opacity = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
            }
        }

        public Matrix Transform
        {
            get => _transform;
            set
            {
                _transform = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Transform)));
            }
        }

        public RectangleF? Clip
        {
            get => _clip;
            set
            {
                _clip = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Clip)));
            }
        }
    }
}
