using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using XamlHorizontalAlignment = VeloxDev.WinForms.Xaml.Controls.HorizontalAlignment;
using XamlVerticalAlignment = VeloxDev.WinForms.Xaml.Controls.VerticalAlignment;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Full-parity phase 3, cluster B: control templates for the combo box, the menu family and the calendar/date picker.
/// Each control replaces its self-drawn chrome with named parts, exposes state to template triggers and realizes
/// item containers into the template's presenter.
/// </summary>
[STATestClass]
public sealed class VisualClusterBTemplateTests
{
    [TestMethod]
    public void ComboBoxTemplate_PartsReplaceChromeAndDriveThePopup()
    {
        using var surface = new VisualHost { Size = new Size(320, 240), DpiScaleOverride = 1f };
        using var combo = new VC.ComboBox { Width = 160, SelectedIndex = 0 };
        combo.Items.Add("One");
        combo.Items.Add("Two");
        combo.Items.Add("Three");

        VC.Border? root = null;
        VC.Border? chrome = null;
        VC.ItemsPresenter? host = null;
        VC.VisualPopup? popup = null;
        var template = new VisualControlTemplate(scope =>
        {
            root = new VC.Border { Background = Color.Lime, Padding = default };
            chrome = new VC.Border
            {
                Width = 10f,
                Height = 10f,
                HorizontalAlignment = XamlHorizontalAlignment.Left,
                VerticalAlignment = XamlVerticalAlignment.Top,
            };
            root.Child = chrome;
            scope.Register("PART_Chrome", chrome);

            host = new VC.ItemsPresenter();
            scope.Register("PART_ItemsPresenter", host);
            var scroll = new VC.ScrollViewer { Content = host };
            popup = new VC.VisualPopup { Child = scroll };
            scope.Register("PART_Popup", popup);
            return root;
        })
        {
            TargetType = typeof(VC.ComboBox),
        };

        var openTrigger = new VisualTrigger(VC.ComboBox.IsDropDownOpenProperty, true) { TargetName = "PART_Chrome" };
        openTrigger.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, new SolidColorBrush(Color.Red)));
        template.Triggers.Add(openTrigger);

        combo.Template = template;
        surface.Root = combo;
        Layout(surface, 320, 240);

        Assert.AreSame(root, combo.TemplateRoot);
        Assert.AreSame(popup, combo.TemplatePopup, "the named PART_Popup becomes the drop-down popup");
        Assert.AreSame(host, combo.GetTemplateChild<VC.ItemsPresenter>("PART_ItemsPresenter"));
        Assert.AreSame(host, combo.DropDownList.ParentElement, "the realized list moved into the template host");

        Assert.AreEqual(Color.Lime.ToArgb(), RenderPixel(surface, 80, 20).ToArgb(),
            "the template root paints the closed chrome instead of the self-drawn selector");
        Assert.IsNull(chrome!.Background, "the drop-down trigger is inactive while closed");

        combo.IsDropDownOpen = true;
        Layout(surface, 320, 240);

        Assert.IsTrue(popup!.IsOpen, "the template popup is the drop-down");
        Assert.IsNotNull(chrome!.Background, "the IsDropDownOpen trigger wrote the named part");
        Assert.AreEqual(Color.Red.ToArgb(), chrome!.Background!.ApproximateColor.ToArgb());

        var row = Center(combo.DropDownList.Items[2].AbsoluteBounds);
        surface.ProcessPointerDown(row, MouseButtons.Left);
        surface.ProcessPointerUp(row, MouseButtons.Left);

        Assert.IsFalse(combo.IsDropDownOpen, "choosing a row closes the template popup");
        Assert.AreEqual(2, combo.SelectedIndex);
        Assert.AreEqual("Three", combo.SelectedItem);

        combo.Template = null;
        Layout(surface, 320, 240);

        Assert.IsNull(combo.TemplateRoot, "clearing the template removes the root");
        Assert.IsNull(combo.GetTemplateChild<VC.Border>("PART_Chrome"), "the part scope is cleared");
        Assert.AreNotEqual(Color.Lime.ToArgb(), RenderPixel(surface, 80, 20).ToArgb(),
            "without a template the self-drawn selector is visible again");
    }

    [TestMethod]
    public void ComboBoxTemplate_EditableTextAndTogglePartsAreWired()
    {
        using var surface = new VisualHost { Size = new Size(320, 240), DpiScaleOverride = 1f };
        using var combo = new VC.ComboBox { Width = 180, IsEditable = true, SelectedIndex = 1 };
        combo.Items.Add("One");
        combo.Items.Add("Two");

        VC.TextBox? editor = null;
        VC.Button? toggle = null;
        var template = new VisualControlTemplate(scope =>
        {
            var root = new VC.StackPanel { Background = Color.Lime };
            editor = new VC.TextBox();
            toggle = new VC.Button { Text = "v" };
            root.Add(editor);
            root.Add(toggle);
            scope.Register("PART_EditableTextBox", editor);
            scope.Register("PART_ToggleButton", toggle);

            var host = new VC.ItemsPresenter();
            scope.Register("PART_ItemsPresenter", host);
            var popup = new VC.VisualPopup { Child = host };
            scope.Register("PART_Popup", popup);
            return root;
        })
        {
            TargetType = typeof(VC.ComboBox),
        };

        combo.Template = template;
        surface.Root = combo;
        Layout(surface, 320, 240);

        Assert.AreSame(editor, combo.EditableTextBox);
        Assert.AreEqual(XamlVisibility.Visible, editor!.Visibility, "an editable template shows its editor");
        Assert.AreEqual("Two", editor.Text, "the selected item drives the editable text");

        combo.EditableTextBox!.Text = "One";
        Assert.AreEqual("One", combo.Text, "typing in the editor flows back to the Text property");

        var toggleCenter = Center(toggle!.AbsoluteBounds);
        surface.ProcessPointerDown(toggleCenter, MouseButtons.Left);
        surface.ProcessPointerUp(toggleCenter, MouseButtons.Left);
        Assert.IsTrue(combo.IsDropDownOpen, "the named toggle part opens the drop-down");

        combo.IsDropDownOpen = false;
        Assert.IsFalse(combo.IsDropDownOpen);
    }

    [TestMethod]
    public void ComboBoxTemplate_MissingPartsThrowDiagnostics()
    {
        using var noPopup = new VC.ComboBox { Width = 140 };
        var noPopupTemplate = new VisualControlTemplate(_ => new VC.Border());
        var popupError = Assert.ThrowsExactly<InvalidOperationException>(() => noPopup.Template = noPopupTemplate);
        StringAssert.Contains(popupError.Message, "PART_Popup",
            "the diagnostic names the missing required part");

        using var noEditor = new VC.ComboBox { Width = 140, IsEditable = true };
        var noEditorTemplate = new VisualControlTemplate(scope =>
        {
            var root = new VC.StackPanel();
            var host = new VC.ItemsPresenter();
            scope.Register("PART_ItemsPresenter", host);
            var popup = new VC.VisualPopup { Child = host };
            scope.Register("PART_Popup", popup);
            return root;
        });
        var editorError = Assert.ThrowsExactly<InvalidOperationException>(() => noEditor.Template = noEditorTemplate);
        StringAssert.Contains(editorError.Message, "PART_EditableTextBox");
    }

    [TestMethod]
    public void ComboBoxWithoutTemplate_StillDrawsAndSelects()
    {
        using var surface = new VisualHost { Size = new Size(320, 240) };
        using var combo = new VC.ComboBox { Width = 160, SelectedIndex = 0 };
        combo.Items.Add("One");
        combo.Items.Add("Two");
        surface.Root = combo;
        Layout(surface, 320, 240);

        Assert.IsNull(combo.TemplateRoot);
        Assert.IsNull(combo.EditableTextBox);

        var center = Center(combo.AbsoluteBounds);
        surface.ProcessPointerDown(center, MouseButtons.Left);
        surface.ProcessPointerUp(center, MouseButtons.Left);
        Assert.IsTrue(combo.IsDropDownOpen, "the no-template popup path is unchanged");

        var row = Center(combo.DropDownList.Items[1].AbsoluteBounds);
        surface.ProcessPointerDown(row, MouseButtons.Left);
        surface.ProcessPointerUp(row, MouseButtons.Left);
        Assert.AreEqual(1, combo.SelectedIndex);
        Assert.IsFalse(combo.IsDropDownOpen);
    }

    [TestMethod]
    public void ContextMenuTemplate_RealizesContainersAndActivatesRows()
    {
        using var surface = new VisualHost { Size = new Size(360, 260), DpiScaleOverride = 1f };
        using var rootPanel = new VC.StackPanel();
        var target = new VC.Border { Width = 80f, Height = 30f, Background = Color.Lime };
        rootPanel.Add(target);
        surface.Root = rootPanel;

        var invoked = 0;
        using var menu = new VC.VisualContextMenu();
        menu.Items.Add(new VC.VisualMenuItem { Header = "Delete", Command = () => invoked++ });
        var parent = new VC.VisualMenuItem { Header = "More" };
        parent.Items.Add(new VC.VisualMenuItem { Header = "Nested" });
        menu.Items.Add(parent);

        VC.StackPanel? templateRoot = null;
        VC.Border? chrome = null;
        VC.ItemsPresenter? host = null;
        var template = new VisualControlTemplate(scope =>
        {
            templateRoot = new VC.StackPanel { Background = Color.Lime };
            chrome = new VC.Border
            {
                Width = 8f,
                Height = 8f,
                HorizontalAlignment = XamlHorizontalAlignment.Left,
                VerticalAlignment = XamlVerticalAlignment.Top,
            };
            host = new VC.ItemsPresenter();
            templateRoot.Add(chrome);
            templateRoot.Add(host);
            scope.Register("PART_Chrome", chrome);
            scope.Register("PART_ItemsPresenter", host);
            return templateRoot;
        })
        {
            TargetType = typeof(VC.VisualContextMenu),
        };

        var hover = new VisualTrigger(VC.VisualContextMenu.IsPointerOverProperty, true) { TargetName = "PART_Chrome" };
        hover.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, new SolidColorBrush(Color.Red)));
        template.Triggers.Add(hover);

        menu.Template = template;
        menu.OpenAt(target, new PointF(target.AbsoluteBounds.X + 2f, target.AbsoluteBounds.Y + 2f));
        Layout(surface, 360, 260);

        Assert.AreSame(templateRoot, menu.TemplateRoot);
        Assert.AreEqual(2, menu.Containers.Count, "one template container is realized per row");
        Assert.AreEqual(2, host!.Children.Count);

        Assert.IsNull(chrome!.Background);
        var firstRow = Center(menu.Containers[0].AbsoluteBounds);
        surface.ProcessPointerMove(firstRow);
        Assert.IsTrue(menu.IsPointerOver, "the pointer entered the templated menu");
        Assert.IsTrue(menu.Containers[0].IsHighlighted, "the hovered row exposes its state");
        Assert.IsNotNull(chrome!.Background, "the IsPointerOver trigger wrote the named part");
        Assert.AreEqual(Color.Red.ToArgb(), chrome!.Background!.ApproximateColor.ToArgb());

        surface.ProcessPointerDown(firstRow, MouseButtons.Left);
        surface.ProcessPointerUp(firstRow, MouseButtons.Left);
        Assert.AreEqual(1, invoked, "clicking a templated row invokes its command");
        Assert.IsFalse(menu.IsOpen, "invoking a row closes the menu");

        menu.OpenAt(target, new PointF(target.AbsoluteBounds.X + 2f, target.AbsoluteBounds.Y + 2f));
        Layout(surface, 360, 260);
        var parentRow = Center(menu.Containers[1].AbsoluteBounds);
        surface.ProcessPointerDown(parentRow, MouseButtons.Left);
        surface.ProcessPointerUp(parentRow, MouseButtons.Left);

        Assert.IsTrue(menu.IsOpen, "a row with children keeps the parent menu open");
        Assert.IsNotNull(menu.Submenu, "the submenu is a nested templated menu");
        Assert.AreEqual(1, menu.Submenu!.Containers.Count, "the submenu realized its child row");

        menu.Template = null;
        Assert.IsNull(menu.TemplateRoot, "clearing the template removes the row containers");
        Assert.AreEqual(0, menu.Containers.Count);
    }

    [TestMethod]
    public void VisualMenuTemplate_RealizesHeadersAndOpensAMenu()
    {
        using var surface = new VisualHost { Size = new Size(360, 220), DpiScaleOverride = 1f };
        using var bar = new VC.VisualMenu();
        var file = new VC.VisualMenuItem { Header = "File" };
        file.Items.Add(new VC.VisualMenuItem { Header = "Open" });
        var edit = new VC.VisualMenuItem { Header = "Edit" };
        edit.Items.Add(new VC.VisualMenuItem { Header = "Copy" });
        bar.Items.Add(file);
        bar.Items.Add(edit);

        VC.StackPanel? host = null;
        var template = new VisualControlTemplate(scope =>
        {
            var root = new VC.StackPanel { Orientation = Orientation.Horizontal, Background = Color.Lime };
            host = new VC.StackPanel { Orientation = Orientation.Horizontal };
            root.Add(host);
            scope.Register("PART_ItemsPresenter", host);
            return root;
        })
        {
            TargetType = typeof(VC.VisualMenu),
        };

        bar.Template = template;
        surface.Root = bar;
        Layout(surface, 360, 220);

        Assert.IsNotNull(bar.TemplateRoot);
        Assert.AreEqual(2, bar.Containers.Count, "one header container is realized per top-level item");
        Assert.AreEqual(2, host!.Children.Count);

        var firstHeader = Center(bar.Containers[0].AbsoluteBounds);
        surface.ProcessPointerDown(firstHeader, MouseButtons.Left);
        surface.ProcessPointerUp(firstHeader, MouseButtons.Left);

        Assert.IsNotNull(bar.OpenedMenu, "pressing a templated header opens its menu");
        Assert.IsTrue(bar.Containers[0].IsHighlighted, "the open header exposes its state");

        bar.Template = null;
        Assert.IsNull(bar.TemplateRoot);
        Assert.AreEqual(0, bar.Containers.Count);
    }

    [TestMethod]
    public void MenuTemplates_MissingItemsHostThrowDiagnostics()
    {
        using var menu = new VC.VisualContextMenu();
        menu.Items.Add(new VC.VisualMenuItem { Header = "A" });
        var menuError = Assert.ThrowsExactly<InvalidOperationException>(
            () => menu.Template = new VisualControlTemplate(_ => new VC.Border()));
        StringAssert.Contains(menuError.Message, "PART_ItemsPresenter");

        using var bar = new VC.VisualMenu();
        bar.Items.Add(new VC.VisualMenuItem { Header = "A" });
        var barError = Assert.ThrowsExactly<InvalidOperationException>(
            () => bar.Template = new VisualControlTemplate(_ => new VC.Border()));
        StringAssert.Contains(barError.Message, "PART_ItemsPresenter");
    }

    [TestMethod]
    public void CalendarTemplate_HostsCalendarItemAndDrivesSelection()
    {
        using var surface = new VisualHost { Size = new Size(320, 300), DpiScaleOverride = 1f };
        using var calendar = new VC.Calendar { Width = 250f, Height = 226f };

        VC.CalendarItem? item = null;
        VC.Border? indicator = null;
        var template = new VisualControlTemplate(scope =>
        {
            var root = new VC.StackPanel { Background = Color.Lime };
            indicator = new VC.Border
            {
                Width = 8f,
                Height = 8f,
                HorizontalAlignment = XamlHorizontalAlignment.Right,
                VerticalAlignment = XamlVerticalAlignment.Top,
            };
            item = new VC.CalendarItem();
            root.Add(indicator);
            root.Add(item);
            scope.Register("PART_Indicator", indicator);
            scope.Register("PART_CalendarItem", item);
            return root;
        })
        {
            TargetType = typeof(VC.Calendar),
        };

        var date = new DateTime(2024, 5, 6);
        var selected = new VisualTrigger(VC.Calendar.SelectedDateProperty, date) { TargetName = "PART_Indicator" };
        selected.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, new SolidColorBrush(Color.Red)));
        template.Triggers.Add(selected);

        calendar.Template = template;
        surface.Root = calendar;
        Layout(surface, 320, 300);

        Assert.AreSame(item, calendar.MonthGrid, "PART_CalendarItem presents the month grid");
        Assert.AreSame(calendar, item!.Owner, "the grid part reads the owning calendar");
        Assert.AreEqual(
            Color.Lime.ToArgb(),
            RenderPixel(surface, (int)calendar.AbsoluteBounds.Left + 3, (int)calendar.AbsoluteBounds.Top + 3).ToArgb(),
            "the template root paints the calendar chrome");

        calendar.SelectedDate = date;
        Assert.IsNotNull(indicator!.Background, "the SelectedDate trigger wrote the named part");
        Assert.AreEqual(Color.Red.ToArgb(), indicator!.Background!.ApproximateColor.ToArgb());

        Assert.IsTrue(calendar.Focus());
        surface.ProcessKeyDown(Keys.Right);
        Assert.AreEqual(date.AddDays(1).Date, calendar.SelectedDate!.Value.Date,
            "keyboard navigation still moves the selection through the templated grid");
        Assert.IsNull(indicator!.Background, "a different date exits the trigger");

        calendar.Template = null;
        Layout(surface, 320, 300);
        Assert.IsNull(calendar.TemplateRoot);
        Assert.IsNull(calendar.MonthGrid, "clearing the template drops the grid part");
        Assert.AreNotEqual(Color.Lime.ToArgb(), RenderPixel(surface, 3, 3).ToArgb(),
            "the self-drawn month grid is visible again");
    }

    [TestMethod]
    public void CalendarTemplate_MissingCalendarItemThrows()
    {
        using var calendar = new VC.Calendar();
        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => calendar.Template = new VisualControlTemplate(_ => new VC.Border()));
        StringAssert.Contains(error.Message, "PART_CalendarItem");
    }

    [TestMethod]
    public void DatePickerTemplate_WiresTextBoxButtonAndPopup()
    {
        using var surface = new VisualHost { Size = new Size(320, 300), DpiScaleOverride = 1f };
        using var picker = new VC.DatePicker { Width = 180f };

        VC.TextBox? editor = null;
        VC.Button? button = null;
        VC.VisualPopup? popup = null;
        VC.Border? indicator = null;
        var template = new VisualControlTemplate(scope =>
        {
            var root = new VC.StackPanel { Background = Color.Lime };
            editor = new VC.TextBox();
            button = new VC.Button { Text = "v" };
            indicator = new VC.Border
            {
                Width = 8f,
                Height = 8f,
                HorizontalAlignment = XamlHorizontalAlignment.Right,
                VerticalAlignment = XamlVerticalAlignment.Top,
            };
            root.Add(indicator);
            root.Add(editor);
            root.Add(button);
            popup = new VC.VisualPopup();
            scope.Register("PART_Indicator", indicator);
            scope.Register("PART_TextBox", editor);
            scope.Register("PART_Button", button);
            scope.Register("PART_Popup", popup);
            return root;
        })
        {
            TargetType = typeof(VC.DatePicker),
        };

        var date = new DateTime(2024, 5, 6);
        var selected = new VisualTrigger(VC.DatePicker.SelectedDateProperty, date) { TargetName = "PART_Indicator" };
        selected.Setters.Add(new VisualSetter(VisualElement.BackgroundProperty, new SolidColorBrush(Color.Red)));
        template.Triggers.Add(selected);

        picker.Template = template;
        surface.Root = picker;
        Layout(surface, 320, 300);

        Assert.IsNotNull(picker.TemplateRoot);
        Assert.AreSame(picker.DropDownCalendar, popup!.Child, "the template popup hosts the picker's calendar");
        Assert.AreEqual(Color.Lime.ToArgb(), RenderPixel(surface, 170, 3).ToArgb(),
            "the template root paints the field chrome");

        var buttonCenter = Center(button!.AbsoluteBounds);
        surface.ProcessPointerDown(buttonCenter, MouseButtons.Left);
        surface.ProcessPointerUp(buttonCenter, MouseButtons.Left);
        Assert.IsTrue(picker.IsDropDownOpen, "the named button opens the calendar");

        surface.ProcessKeyDown(Keys.Right);
        Assert.IsFalse(picker.IsDropDownOpen, "choosing a day closes the calendar");
        Assert.IsNotNull(picker.SelectedDate);
        Assert.AreEqual(DateTime.Today.AddDays(1).Date, picker.SelectedDate!.Value.Date);
        Assert.IsNull(indicator!.Background, "the trigger is inactive for a different date");

        editor!.Text = "2024-05-06";
        Assert.AreEqual(date.Date, picker.SelectedDate!.Value.Date, "a typed date commits through PART_TextBox");
        Assert.IsNotNull(indicator!.Background, "the SelectedDate trigger wrote the named part");

        picker.Template = null;
        Layout(surface, 320, 300);
        Assert.IsNull(picker.TemplateRoot);
        Assert.IsNull(picker.GetTemplateChild<VC.TextBox>("PART_TextBox"));
        Assert.AreNotEqual(Color.Lime.ToArgb(), RenderPixel(surface, 170, 3).ToArgb(),
            "the self-drawn field is visible again");
    }

    [TestMethod]
    public void DatePickerTemplate_MissingPartsThrowDiagnostics()
    {
        using var picker = new VC.DatePicker();
        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => picker.Template = new VisualControlTemplate(_ => new VC.Border()));
        StringAssert.Contains(error.Message, "PART_TextBox");
    }

    [TestMethod]
    public void DatePickerWithoutTemplate_StillOpensAndSelects()
    {
        using var surface = new VisualHost { Size = new Size(320, 280) };
        using var root = new VC.StackPanel();
        using var picker = new VC.DatePicker { Width = 180f };
        root.Add(picker);
        surface.Root = root;
        Layout(surface, 320, 280);

        Assert.IsNull(picker.TemplateRoot);
        var field = Center(picker.AbsoluteBounds);
        surface.ProcessPointerDown(field, MouseButtons.Left);
        surface.ProcessPointerUp(field, MouseButtons.Left);
        Assert.IsTrue(picker.IsDropDownOpen, "the no-template popup path is unchanged");

        surface.ProcessKeyDown(Keys.Right);
        Assert.IsFalse(picker.IsDropDownOpen);
        Assert.AreEqual(DateTime.Today.AddDays(1).Date, picker.SelectedDate!.Value.Date);
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

    private static Color RenderPixel(VisualHost surface, int x, int y)
    {
        using var bitmap = new Bitmap(surface.Size.Width, surface.Size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            surface.RenderTo(graphics, surface.Size);
        }

        return bitmap.GetPixel(x, y);
    }
}
