using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>The template part that paints a <see cref="TextBox"/>'s text surface and locates its native editor island.</summary>
/// <remarks>
/// <para>
/// Declare it as <c>PART_ContentHost</c> (or <c>PART_EditorHost</c>) inside a <see cref="TextBox"/> control template.
/// The part paints the text, selection and caret through its <see cref="VisualElement.TemplatedParent"/> and defines
/// the rectangle the native editor island covers while the field has focus.
/// </para>
/// <para>
/// The island is a real HWND owned by the surface, so it is positioned over the part instead of being a visual child
/// of it: the part cannot transform, blend or clip the native editor.
/// </para>
/// </remarks>
public class TextBoxContentHost : VisualElement
{
    /// <summary>Creates a text host with the I-beam pointer.</summary>
    public TextBoxContentHost()
    {
        Cursor = Cursors.IBeam;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => TemplatedParent is TextBox owner
            ? owner.MeasureEditor(availableSize)
            : base.MeasureOverride(availableSize);

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (TemplatedParent is not TextBox owner)
        {
            return;
        }

        owner.RenderEditorSurface(
            graphics,
            new Rectangle(
                0,
                0,
                Math.Max(1, (int)Math.Ceiling(Bounds.Width)),
                Math.Max(1, (int)Math.Ceiling(Bounds.Height))));
    }
}

/// <summary>The template part that paints a <see cref="RichTextBox"/>'s document, selection and caret.</summary>
/// <remarks>
/// Declare it as <c>PART_ContentHost</c> inside a <see cref="RichTextBox"/> control template, optionally inside a
/// <c>PART_ScrollViewer</c>. The part asks its <see cref="VisualElement.TemplatedParent"/> for the document layout and
/// paints the runs; the control keeps ownership of hit-testing, editing state and the scroll offset. When the editor
/// wraps, set the scroll viewer's horizontal bar to <c>Disabled</c> so the document is measured at the viewport width
/// instead of an unbounded one.
/// </remarks>
public class RichTextContentHost : VisualElement
{
    /// <summary>Creates a rich text host with the I-beam pointer.</summary>
    public RichTextContentHost()
    {
        Cursor = Cursors.IBeam;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => TemplatedParent is RichTextBox owner
            ? owner.MeasureDocument(availableSize)
            : base.MeasureOverride(availableSize);

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (TemplatedParent is RichTextBox owner)
        {
            owner.RenderDocument(graphics, new RectangleF(0f, 0f, Bounds.Width, Bounds.Height));
        }
    }
}
