using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using V = VeloxDev.WinForms.Xaml.Controls;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>Hover state follows the pointer through nested XAML elements.</summary>
[STATestClass]
public sealed class HoverTests
{
    [TestMethod]
    public void HoveringAChild_MarksTheAncestorPointerOver()
    {
        using var form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(80, 80),
            Size = new Size(260, 180),
            ShowInTaskbar = false,
        };
        using var parent = new V.Panel { Dock = DockStyle.Fill };
        using var child = new V.Panel { Bounds = new Rectangle(30, 24, 80, 50) };
        form.Controls.Add(parent);
        parent.Controls.Add(child);
        form.Show();
        Application.DoEvents();

        try
        {
            var update = typeof(V.Control).GetMethod(
                "UpdatePointerOver",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(Point)],
                modifiers: null)
                ?? throw new InvalidOperationException("Control.UpdatePointerOver moved; update this test.");

            var childBounds = child.RectangleToScreen(child.ClientRectangle);
            update.Invoke(child, [new Point(childBounds.Left + 8, childBounds.Top + 8)]);

            Assert.IsTrue(child.IsPointerOver, "the child is hovered");
            Assert.IsTrue(parent.IsPointerOver, "the XAML ancestor sees the child hover");

            update.Invoke(child, [new Point(form.Bounds.Right + 40, form.Bounds.Bottom + 40)]);

            Assert.IsFalse(child.IsPointerOver);
            Assert.IsFalse(parent.IsPointerOver, "leaving the whole subtree clears the ancestor");
        }
        finally
        {
            form.Close();
        }
    }
}
