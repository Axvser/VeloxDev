using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>
/// An attached-property owner used by the XAML attached-attribute tests. The backing fields are declared by hand,
/// which is also the shape a generated <c>[XamlAttachedProperty]</c> owner exposes to consumers.
/// </summary>
public static class WorkflowPanel
{
    /// <summary>The zero-based column attached to a control.</summary>
    public static readonly DependencyProperty ColumnProperty = DependencyProperty.RegisterAttached(
        "Column",
        typeof(int),
        typeof(WorkflowPanel),
        new PropertyMetadata(0));

    /// <summary>A text label attached to a control.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.RegisterAttached(
        "Label",
        typeof(string),
        typeof(WorkflowPanel));

    /// <summary>Reads the attached column.</summary>
    /// <param name="control">The control carrying the value.</param>
    public static int GetColumn(System.Windows.Forms.Control control) => (int)ColumnProperty.GetValue(control)!;

    /// <summary>Writes the attached column.</summary>
    /// <param name="control">The control receiving the value.</param>
    /// <param name="value">The column index.</param>
    public static void SetColumn(System.Windows.Forms.Control control, int value) => ColumnProperty.SetValue(control, value);

    /// <summary>Reads the attached label.</summary>
    /// <param name="control">The control carrying the value.</param>
    public static string? GetLabel(System.Windows.Forms.Control control) => (string?)LabelProperty.GetValue(control);

    /// <summary>Writes the attached label.</summary>
    /// <param name="control">The control receiving the value.</param>
    /// <param name="value">The label text.</param>
    public static void SetLabel(System.Windows.Forms.Control control, string? value) => LabelProperty.SetValue(control, value);
}
