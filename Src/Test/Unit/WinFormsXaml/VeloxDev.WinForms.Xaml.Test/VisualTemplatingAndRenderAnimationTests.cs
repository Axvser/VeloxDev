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
    public void TextBoxTemplate_HostsAContentPartAndReplacesTheSelfDrawnChrome()
    {
        using var surface = new VisualHost { Size = new Size(240, 120), DpiScaleOverride = 1f };
        var box = new VC.TextBox { Width = 200, Height = 40, Text = "Hello", Foreground = Color.White };
        VC.TextBoxContentHost? host = null;
        VC.Border? chrome = null;

        var template = new VisualControlTemplate(scope =>
        {
            chrome = new VC.Border { Background = Color.Lime, Padding = default };
            host = new VC.TextBoxContentHost();
            chrome.Child = host;
            scope.Register("PART_ContentHost", host);
            scope.Register("PART_Chrome", chrome);
            return chrome;
        })
        {
            TargetType = typeof(VC.TextBox),
        };

        box.Template = template;
        surface.Root = box;
        Layout(surface, 240, 120);

        Assert.AreSame(chrome, box.TemplateRoot);
        Assert.AreSame(host, box.GetTemplateChild<VC.TextBoxContentHost>("PART_ContentHost"));
        Assert.IsNull(box.TemplateDiagnostic, "a template with a content host is accepted");

        var bounds = box.AbsoluteBounds;
        Assert.AreEqual(
            Color.Lime.ToArgb(),
            RenderPixel(surface, (int)bounds.Right - 4, (int)bounds.Bottom - 4).ToArgb(),
            "the template chrome replaces the self-drawn border and background");

        var white = CountPixels(
            surface,
            (int)bounds.X,
            (int)bounds.Y,
            (int)bounds.Width,
            (int)bounds.Height,
            static pixel => pixel.R > 200 && pixel.G > 200 && pixel.B > 200);
        Assert.IsGreaterThan(0, white, "the content part paints the field's text");

        box.Template = null;
        Layout(surface, 240, 120);

        var restored = box.AbsoluteBounds;
        Assert.IsNull(box.TemplateRoot, "clearing the template removes the part scope");
        Assert.IsNull(box.GetTemplateChild<VC.TextBoxContentHost>("PART_ContentHost"));
        Assert.IsNull(box.TemplateDiagnostic);
        Assert.AreNotEqual(
            Color.Lime.ToArgb(),
            RenderPixel(surface, (int)restored.Right - 4, (int)restored.Bottom - 4).ToArgb(),
            "clearing the template restores the self-drawn chrome");
    }

    [TestMethod]
    public void TextBoxTemplate_AppliesThroughAStyleAndRestoresWhenTheStyleIsCleared()
    {
        using var surface = new VisualHost { Size = new Size(240, 120), DpiScaleOverride = 1f };
        var box = new VC.TextBox { Width = 200, Height = 40 };
        var style = new VisualStyle { TargetType = typeof(VC.TextBox) };
        style.Template = new VisualControlTemplate(scope =>
        {
            var chrome = new VC.Border { Background = Color.Aqua, Padding = default };
            var host = new VC.TextBoxContentHost();
            chrome.Child = host;
            scope.Register("PART_ContentHost", host);
            return chrome;
        })
        {
            TargetType = typeof(VC.TextBox),
        };

        box.Style = style;
        surface.Root = box;
        Layout(surface, 240, 120);

        var bounds = box.AbsoluteBounds;
        Assert.IsNotNull(box.TemplateRoot, "a style template applies to the text field");
        Assert.AreEqual(
            Color.Aqua.ToArgb(),
            RenderPixel(surface, (int)bounds.Right - 4, (int)bounds.Bottom - 4).ToArgb(),
            "the style template replaces the self-drawn chrome");

        box.Style = null;
        Layout(surface, 240, 120);

        Assert.IsNull(box.TemplateRoot, "clearing the style removes its template");
        Assert.AreNotEqual(
            Color.Aqua.ToArgb(),
            RenderPixel(surface, (int)bounds.Right - 4, (int)bounds.Bottom - 4).ToArgb(),
            "clearing the style restores the self-drawn chrome");
    }

    [TestMethod]
    public void TextBoxTemplate_KeepsFocusAndCaretHitTestingInsideThePart()
    {
        using var surface = new VisualHost { Size = new Size(240, 120) };
        var box = new VC.TextBox { Width = 200, Height = 40, Text = "abcdef" };
        VC.TextBoxContentHost? host = null;

        box.Template = new VisualControlTemplate(scope =>
        {
            var chrome = new VC.Border { Padding = default };
            host = new VC.TextBoxContentHost();
            chrome.Child = host;
            scope.Register("PART_ContentHost", host);
            return chrome;
        })
        {
            TargetType = typeof(VC.TextBox),
        };

        surface.Root = box;
        Layout(surface, 240, 120);

        surface.FocusElement(box);
        Assert.IsTrue(box.IsFocused, "the field takes logical focus");
        Assert.IsTrue((bool)box.GetValue(VC.TextBox.IsFocusedProperty)!, "the trigger-facing focus state follows focus");

        var sample = host!.PointToSurface(new PointF(host.Bounds.Width - 2f, host.Bounds.Height / 2f));
        surface.ProcessPointerDown(sample, System.Windows.Forms.MouseButtons.Left);
        surface.ProcessPointerUp(sample, System.Windows.Forms.MouseButtons.Left);

        Assert.AreEqual(6, box.CaretIndex, "a click at the right edge of the part lands at the end of the text");
    }

    [TestMethod]
    public void TextBoxTemplate_StillHostsTheNativeEditorOverThePart()
    {
        using var surface = new VisualHost { Size = new Size(320, 160) };
        _ = surface.Handle;
        System.Windows.Forms.Application.DoEvents();

        var box = new VC.TextBox { Width = 220, Height = 34 };
        box.Template = new VisualControlTemplate(scope =>
        {
            var chrome = new VC.Border { Padding = default };
            var host = new VC.TextBoxContentHost();
            chrome.Child = host;
            scope.Register("PART_ContentHost", host);
            return chrome;
        })
        {
            TargetType = typeof(VC.TextBox),
        };

        surface.Root = box;
        Layout(surface, 320, 160);
        surface.FocusElement(box);
        System.Windows.Forms.Application.DoEvents();

        Assert.IsTrue(box.IsUsingNativeEditor, "a templated field still opens the native island");
        Assert.IsTrue(surface.Controls.Contains(box.NativeEditor), "the island is parented to the surface");
        Assert.IsTrue((bool)box.GetValue(VC.TextBox.IsFocusedProperty)!, "the focus state stays true while the island edits");
    }

    [TestMethod]
    public void TextBoxTemplate_WithoutAContentHostReportsADiagnostic()
    {
        using var box = new VC.TextBox();
        box.Template = new VisualControlTemplate(_ => new VC.Border())
        {
            TargetType = typeof(VC.TextBox),
        };

        Assert.IsNotNull(box.TemplateDiagnostic, "a template with no content host is reported, not silently accepted");

        box.Template = null;
        Assert.IsNull(box.TemplateDiagnostic, "clearing the template clears the diagnostic");
    }

    [TestMethod]
    public void TextBoxTemplate_TriggersObserveTheFocusState()
    {
        using var surface = new VisualHost { Size = new Size(240, 120) };
        var box = new VC.TextBox { Width = 200, Height = 40 };
        VC.TextBoxContentHost? host = null;

        var template = new VisualControlTemplate(scope =>
        {
            var chrome = new VC.Border { Padding = default };
            host = new VC.TextBoxContentHost();
            chrome.Child = host;
            scope.Register("PART_ContentHost", host);
            return chrome;
        })
        {
            TargetType = typeof(VC.TextBox),
        };

        var focused = new VisualTrigger(VC.TextBox.IsFocusedProperty, true) { TargetName = "PART_ContentHost" };
        focused.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, new SolidColorBrush(Color.Gold)));
        template.Triggers.Add(focused);

        box.Template = template;
        surface.Root = box;
        Layout(surface, 240, 120);

        Assert.IsNull(host!.Background, "the focus trigger is inactive while the field is unfocused");

        surface.FocusElement(box);
        Assert.IsNotNull(host.Background, "the focus trigger wrote the content host");
        Assert.AreEqual(Color.Gold.ToArgb(), host.Background!.ApproximateColor.ToArgb());
    }

    [TestMethod]
    public void RichTextBoxTemplate_RendersTheDocumentAndScrollsTheCaret()
    {
        using var surface = new VisualHost { Size = new Size(320, 180), DpiScaleOverride = 1f };
        var rich = new VC.RichTextBox { Width = 260f, Height = 120f, Foreground = Color.White };
        rich.SetPlainText(string.Join("\n", Enumerable.Range(0, 40).Select(static index => "line " + index)));

        VC.RichTextContentHost? host = null;
        VC.ScrollViewer? viewer = null;
        VC.Border? chrome = null;
        var template = new VisualControlTemplate(scope =>
        {
            chrome = new VC.Border { Background = Color.Navy, Padding = default };
            viewer = new VC.ScrollViewer
            {
                HorizontalScrollBarVisibility = VC.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = VC.ScrollBarVisibility.Auto,
            };
            host = new VC.RichTextContentHost();
            viewer.Content = host;
            chrome.Child = viewer;
            scope.Register("PART_Chrome", chrome);
            scope.Register("PART_ScrollViewer", viewer);
            scope.Register("PART_ContentHost", host);
            return chrome;
        })
        {
            TargetType = typeof(VC.RichTextBox),
        };

        rich.Template = template;
        surface.Root = rich;
        Layout(surface, 320, 180);

        Assert.AreSame(chrome, rich.TemplateRoot);
        Assert.AreSame(host, rich.GetTemplateChild<VC.RichTextContentHost>("PART_ContentHost"));
        Assert.AreSame(viewer, rich.GetTemplateChild<VC.ScrollViewer>("PART_ScrollViewer"));
        Assert.IsNull(rich.TemplateDiagnostic, "a template with a content host is accepted");

        var bounds = rich.AbsoluteBounds;
        Assert.AreEqual(
            Color.Navy.ToArgb(),
            RenderPixel(surface, (int)bounds.Right - 30, (int)bounds.Y + 4).ToArgb(),
            "the template chrome replaces the self-drawn border");

        var white = CountPixels(
            surface,
            (int)bounds.X,
            (int)bounds.Y,
            (int)bounds.Width,
            (int)bounds.Height,
            static pixel => pixel.R > 200 && pixel.G > 200 && pixel.B > 200);
        Assert.IsGreaterThan(0, white, "the content host paints the document runs");

        surface.FocusElement(rich);
        surface.ProcessTextInput("Z");
        Assert.IsTrue(rich.Text.StartsWith("Z", StringComparison.Ordinal), "typing reaches the templated editor");

        var documentPoint = host!.PointToSurface(new PointF(30f, 8f));
        surface.ProcessPointerDown(documentPoint, System.Windows.Forms.MouseButtons.Left);
        surface.ProcessPointerUp(documentPoint, System.Windows.Forms.MouseButtons.Left);
        Assert.IsGreaterThan(0, rich.CaretIndex, "a click in the hosted document maps to a character index");

        surface.ProcessKeyDown(System.Windows.Forms.Keys.End, System.Windows.Forms.Keys.Control);
        Layout(surface, 320, 180);

        Assert.IsGreaterThan(0f, viewer!.VerticalOffset, "moving the caret to the document end scrolls the part's viewport");

        rich.Template = null;
        Layout(surface, 320, 180);

        Assert.IsNull(rich.TemplateRoot, "clearing the template removes the part scope");
        Assert.IsNull(rich.GetTemplateChild<VC.RichTextContentHost>("PART_ContentHost"));
        Assert.IsNull(rich.TemplateDiagnostic);
    }

    [TestMethod]
    public void RichTextBoxTemplate_WithoutAContentHostReportsADiagnostic()
    {
        using var rich = new VC.RichTextBox();
        rich.Template = new VisualControlTemplate(_ => new VC.Border())
        {
            TargetType = typeof(VC.RichTextBox),
        };

        Assert.IsNotNull(rich.TemplateDiagnostic, "a template with no content host is reported, not silently accepted");

        rich.Template = null;
        Assert.IsNull(rich.TemplateDiagnostic);
    }

    [TestMethod]
    public void RichTextBoxTemplate_TriggersObserveReadOnlyState()
    {
        using var surface = new VisualHost { Size = new Size(260, 140) };
        var rich = new VC.RichTextBox { Width = 220f, Height = 90f };
        VC.RichTextContentHost? host = null;

        var template = new VisualControlTemplate(scope =>
        {
            var chrome = new VC.Border { Padding = default };
            host = new VC.RichTextContentHost();
            chrome.Child = host;
            scope.Register("PART_ContentHost", host);
            return chrome;
        })
        {
            TargetType = typeof(VC.RichTextBox),
        };

        var readOnly = new VisualTrigger(VC.RichTextBox.IsReadOnlyProperty, true) { TargetName = "PART_ContentHost" };
        readOnly.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, new SolidColorBrush(Color.Orange)));
        template.Triggers.Add(readOnly);

        rich.Template = template;
        surface.Root = rich;
        Layout(surface, 260, 140);

        Assert.IsNull(host!.Background, "the read-only trigger is inactive while editing is allowed");

        rich.IsReadOnly = true;
        Assert.IsNotNull(host.Background, "the read-only trigger wrote the content host");
        Assert.AreEqual(Color.Orange.ToArgb(), host.Background!.ApproximateColor.ToArgb());
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

    private static int CountPixels(
        VisualHost surface,
        int x,
        int y,
        int width,
        int height,
        Func<Color, bool> predicate)
    {
        using var bitmap = new Bitmap(surface.Size.Width, surface.Size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, surface.Size);
        }

        var count = 0;
        var right = Math.Min(bitmap.Width, x + width);
        var bottom = Math.Min(bitmap.Height, y + height);
        for (var py = Math.Max(0, y); py < bottom; py++)
        {
            for (var px = Math.Max(0, x); px < right; px++)
            {
                if (predicate(bitmap.GetPixel(px, py)))
                {
                    count++;
                }
            }
        }

        return count;
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
