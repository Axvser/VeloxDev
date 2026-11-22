using System.Drawing;
using V = VeloxDev.WinForms.Xaml.Controls;
using S = VeloxDev.WinForms.Xaml.Controls.Shapes;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The small rendering contracts that keep XAML controls aligned with WPF: the border box owns its stroke,
/// shapes have no outline unless one is authored, and native editors sit in the middle of their fields.
/// </summary>
[STATestClass]
public sealed class RenderingParityTests
{
    [TestMethod]
    public void Border_InsetsTheChildByItsBorderThickness()
    {
        using var border = new V.Border
        {
            Size = new Size(100, 60),
            BorderThickness = new V.Thickness(2f),
            Padding = V.Thickness.Empty,
        };
        using var child = new V.Border { Margin = V.Thickness.Empty };
        border.Controls.Add(child);

        border.PerformLayout();

        Assert.AreEqual(new Rectangle(2, 2, 96, 56), child.Bounds);
    }

    [TestMethod]
    public void Shape_HasNoStrokeUntilOneIsAuthored()
    {
        using var ellipse = new S.Ellipse();

        Assert.IsNull(ellipse.Stroke, "a fill-only shape must not inherit a black outline");
    }

    [TestMethod]
    public void TextBox_CentresItsSingleLineEditor()
    {
        using var text = new V.TextBox
        {
            Size = new Size(200, 60),
            BorderThickness = V.Thickness.Empty,
            Margin = V.Thickness.Empty,
        };

        text.PerformLayout();

        var editor = (System.Windows.Forms.TextBox)text.Controls[0];
        var expectedTop = (60 - editor.Height) / 2;
        Assert.AreEqual(expectedTop, editor.Top);
        Assert.IsTrue(editor.Height < 60, "the editor keeps its single-line height instead of stretching");
    }

    [TestMethod]
    public void ComboBox_CentresItsNativeSelector()
    {
        using var combo = new V.ComboBox
        {
            Size = new Size(200, 60),
            Margin = V.Thickness.Empty,
        };

        combo.PerformLayout();

        var native = (System.Windows.Forms.ComboBox)combo.Controls[0];
        var contentHeight = 60 - (int)Math.Ceiling(combo.BorderThickness.Vertical);
        var expectedTop = (int)Math.Ceiling(combo.BorderThickness.Top) + ((contentHeight - native.Height) / 2);
        Assert.AreEqual(expectedTop, native.Top);
        Assert.IsTrue(native.Height < contentHeight, "the combo keeps its font-driven height");
    }
}
