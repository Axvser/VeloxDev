using VeloxDev.WinForms.Xaml.Test.Infrastructure;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// Rich text and navigation controls: the document model, the native rich-text hosts and the frame journal.
/// </summary>
[STATestClass]
public sealed class RichTextNavigationTests
{
    [TestMethod]
    public void FlowDocument_ExtractsPlainTextAcrossParagraphsAndFormatting()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = "Hello " });

        var bold = new Bold();
        bold.Inlines.Add(new Run { Text = "world" });
        paragraph.Inlines.Add(bold);
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run { Text = "again" });

        var section = new Section();
        var nested = new Paragraph();
        nested.Inlines.Add(new Run { Text = "nested" });
        section.Blocks.Add(nested);

        document.Blocks.Add(paragraph);
        document.Blocks.Add(section);

        Assert.AreEqual("Hello world\nagain\nnested", document.ToPlainText());
    }

    [TestMethod]
    public void FlowDocument_ProducesRtfWithFormattingAndEscapedText()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = "a{b}c\\d" });

        var italic = new Italic();
        italic.Inlines.Add(new Run { Text = "styled" });
        paragraph.Inlines.Add(italic);
        document.Blocks.Add(paragraph);

        var rtf = document.ToRtf();

        StringAssert.StartsWith(rtf, "{\\rtf1");
        StringAssert.Contains(rtf, "a\\{b\\}c\\\\d");
        StringAssert.Contains(rtf, "{\\i styled}");
        StringAssert.Contains(rtf, "\\par ");
    }

    [TestMethod]
    public void FlowDocument_ListAndTableProduceReadableText()
    {
        var document = new FlowDocument();

        var list = new ListBlock();
        var first = new ListItem();
        var firstParagraph = new Paragraph();
        firstParagraph.Inlines.Add(new Run { Text = "one" });
        first.Blocks.Add(firstParagraph);
        list.ListItems.Add(first);

        var second = new ListItem();
        var secondParagraph = new Paragraph();
        secondParagraph.Inlines.Add(new Run { Text = "two" });
        second.Blocks.Add(secondParagraph);
        list.ListItems.Add(second);

        var table = new Table();
        var row = new TableRow();
        var left = new TableCell();
        var leftParagraph = new Paragraph();
        leftParagraph.Inlines.Add(new Run { Text = "A" });
        left.Blocks.Add(leftParagraph);
        var right = new TableCell();
        var rightParagraph = new Paragraph();
        rightParagraph.Inlines.Add(new Run { Text = "B" });
        right.Blocks.Add(rightParagraph);
        row.Cells.Add(left);
        row.Cells.Add(right);
        table.Rows.Add(row);

        document.Blocks.Add(list);
        document.Blocks.Add(table);

        Assert.AreEqual("• one\n• two\nA\tB", document.ToPlainText());
    }

    [TestMethod]
    public void RichTextBox_TextRoundTripsThroughTheNativeEditor()
    {
        using var box = new V.RichTextBox();
        XamlTestSurface.EnsureHandle(box);

        box.Text = "Hello rich text";

        Assert.AreEqual("Hello rich text", box.Text);
        StringAssert.Contains(box.Rtf, "Hello rich text");
        StringAssert.StartsWith(box.Rtf, "{\\rtf");
    }

    [TestMethod]
    public void RichTextBox_LoadsRtfIntoPlainText()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = "From RTF" });
        document.Blocks.Add(paragraph);

        using var box = new V.RichTextBox();
        XamlTestSurface.EnsureHandle(box);

        box.Rtf = document.ToRtf();

        StringAssert.Contains(box.Text, "From RTF");
    }

    [TestMethod]
    public void RichTextBox_LoadsFlowDocumentWhenAssigned()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = "From document" });
        document.Blocks.Add(paragraph);

        using var box = new V.RichTextBox { Document = document };
        XamlTestSurface.EnsureHandle(box);

        StringAssert.Contains(box.Text, "From document");
    }

    [TestMethod]
    public void RichTextBox_SynchronisesTheSelection()
    {
        using var box = new V.RichTextBox();
        XamlTestSurface.EnsureHandle(box);

        box.Text = "abcdef";
        box.SelectionStart = 2;
        box.SelectionLength = 3;

        Assert.AreEqual(2, box.SelectionStart);
        Assert.AreEqual(3, box.SelectionLength);
    }

    [TestMethod]
    public void FlowDocumentScrollViewer_RendersTheDocumentAsPlainText()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = "Rendered" });
        document.Blocks.Add(paragraph);

        using var viewer = new V.FlowDocumentScrollViewer { Document = document };
        XamlTestSurface.EnsureHandle(viewer);

        Assert.AreEqual("Rendered", viewer.PlainText);
        StringAssert.Contains(viewer.Text, "Rendered");
    }

    [TestMethod]
    public void DocumentViewer_RendersTheDocumentAndExposesPrintAvailability()
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = "Printable" });
        document.Blocks.Add(paragraph);

        using var viewer = new V.DocumentViewer { Document = document };
        XamlTestSurface.EnsureHandle(viewer);

        Assert.IsTrue(viewer.CanPrint);
        Assert.AreEqual("Printable", viewer.PlainText);
        StringAssert.Contains(viewer.Text, "Printable");
    }

    [TestMethod]
    public void Frame_NavigatesAndJournalsBackAndForward()
    {
        using var frame = new V.Frame();
        var first = new V.Page { Title = "First" };
        var second = new V.Page { Title = "Second" };
        var changes = 0;
        frame.NavigationChanged += (_, _) => changes++;

        frame.Navigate(first);

        Assert.AreSame(first, frame.CurrentPage);
        Assert.IsFalse(frame.CanGoBack);
        Assert.IsFalse(frame.CanGoForward);

        frame.Navigate(second);

        Assert.AreSame(second, frame.CurrentPage);
        Assert.IsTrue(frame.CanGoBack);
        Assert.IsFalse(frame.CanGoForward);
        Assert.AreEqual(2, frame.Journal.Count);

        Assert.IsTrue(frame.GoBack());
        Assert.AreSame(first, frame.CurrentPage);
        Assert.IsTrue(frame.CanGoForward);

        Assert.IsTrue(frame.GoForward());
        Assert.AreSame(second, frame.CurrentPage);
        Assert.AreEqual(4, changes);
    }

    [TestMethod]
    public void Page_ExposesItsTitle()
    {
        using var page = new V.Page { Title = "Home" };

        Assert.AreEqual("Home", page.Title);
    }

    [TestMethod]
    public void Window_ShowAndCloseTogglesVisibility()
    {
        using var window = new V.Window
        {
            Title = "Smoke",
            Content = new V.Border(),
            Width = 320,
            Height = 200,
        };

        try
        {
            window.Show();

            Assert.IsTrue(window.IsVisible);
            Assert.IsNotNull(window.HostForm);
            Assert.AreEqual("Smoke", window.HostForm!.Text);
        }
        finally
        {
            window.Close();
        }

        Assert.IsFalse(window.IsVisible);
    }

    [TestMethod]
    public void NavigationWindow_ForwardsNavigationToItsFrame()
    {
        using var window = new V.NavigationWindow();
        var page = new V.Page { Title = "Home" };

        window.Navigate(page);

        Assert.AreSame(page, window.CurrentPage);
        Assert.IsFalse(window.CanGoBack);
        Assert.IsFalse(window.CanGoForward);
        Assert.AreSame(window.Frame, window.Content);
    }
}
