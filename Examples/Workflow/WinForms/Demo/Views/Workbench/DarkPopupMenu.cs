using System.Drawing;
using System.Windows.Forms;

using Demo.Views.Workflow;

namespace Demo.Views.Workbench;

/// <summary>
/// A small border-less popup used by the title strip's menus. WinForms' <see cref="ContextMenuStrip"/>
/// keeps the system's light drop-down surface on this stack even with a custom renderer, which made
/// light menu text sit on a white panel. A Form of our own paints the panel and the hover row with the
/// same tokens as the rest of the workbench, so there is no theme to fight.
/// </summary>
internal sealed class DarkPopupMenu : Form
{
    private readonly FlowLayoutPanel _rows;

    public DarkPopupMenu(int width = 250)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = WorkbenchPalette.Border;
        Padding = new Padding(1);
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;

        _rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = WorkbenchPalette.Panel,
            Margin = Padding.Empty,
            Padding = new Padding(4, 3, 4, 3),
            Width = width,
        };
        Controls.Add(_rows);

        // 菜单实例由标题栏复用；Close() 会把它释放，第二次点击就是 Show on disposed Form。
        Deactivate += (_, _) => Hide();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Hide();
        };
    }

    public void AddItem(string text, string? shortcut, Action action)
    {
        var row = new TableLayoutPanel
        {
            Width = _rows.Width - 8,
            Height = 27,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = WorkbenchPalette.Panel,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Cursor = Cursors.Hand,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var label = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = WorkbenchPalette.TextPrimary,
            BackColor = WorkbenchPalette.Panel,
            Font = CardTheme.Font(9F),
            Padding = new Padding(8, 0, 4, 0),
            Cursor = Cursors.Hand,
        };
        var gesture = new Label
        {
            Text = shortcut ?? string.Empty,
            AutoSize = false,
            Width = 64,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = WorkbenchPalette.TextTertiary,
            BackColor = WorkbenchPalette.Panel,
            Font = CardTheme.Font(8.5F),
            Padding = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
        };

        void Enter(object? _, EventArgs __) => SetRowBack(row, label, gesture, WorkbenchPalette.Hover);
        void Leave(object? _, EventArgs __) => SetRowBack(row, label, gesture, WorkbenchPalette.Panel);
        void Pick(object? _, EventArgs __)
        {
            Hide();
            action();
        }

        foreach (var control in new Control[] { row, label, gesture })
        {
            control.MouseEnter += Enter;
            control.MouseLeave += Leave;
            control.Click += Pick;
        }

        row.Controls.Add(label, 0, 0);
        row.Controls.Add(gesture, 1, 0);
        _rows.Controls.Add(row);
    }

    public void AddSeparator()
    {
        _rows.Controls.Add(new Panel
        {
            Width = _rows.Width - 8,
            Height = 1,
            BackColor = WorkbenchPalette.Border,
            Margin = new Padding(0, 4, 0, 4),
        });
    }

    public void ShowBelow(Control anchor)
    {
        _rows.PerformLayout();
        var size = _rows.PreferredSize;
        ClientSize = new Size(size.Width + 2, size.Height + 2);

        var point = anchor.PointToScreen(new Point(0, anchor.Height));
        var screen = Screen.FromControl(anchor).WorkingArea;
        if (point.Y + Height > screen.Bottom) point.Y = anchor.PointToScreen(Point.Empty).Y - Height;
        Location = point;
        Show(anchor.FindForm());
        BringToFront();
    }

    public void ShowAt(Point screenPoint, Form? owner)
    {
        _rows.PerformLayout();
        var size = _rows.PreferredSize;
        ClientSize = new Size(size.Width + 2, size.Height + 2);

        var screen = Screen.FromPoint(screenPoint).WorkingArea;
        if (screenPoint.Y + Height > screen.Bottom) screenPoint.Y -= Height;
        Location = screenPoint;
        if (owner is null) Show(); else Show(owner);
        BringToFront();
    }

    private static void SetRowBack(Control row, Label label, Label gesture, Color color)
    {
        row.BackColor = color;
        label.BackColor = color;
        gesture.BackColor = color;
    }
}
