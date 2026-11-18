using System.Drawing;
using System.Windows.Forms;
using Demo.ViewModels;

namespace Demo.Views.Workflow;

/// <summary>
/// The explorer's per-kind element table, with Locate/Delete on every row. A ListBox could show only
/// the label and push the two actions to the bottom of the page; the Avalonia/WinUI/MAUI lists put them
/// on the row, and this keeps the WinForms shell on that same shape.
/// </summary>
internal sealed class ElementRowList : UserControl
{
    private readonly Panel _host;
    private readonly ToolTip _toolTip = new();
    private WorkflowElementEntry? _selected;
    private bool _suppress;

    public ElementRowList()
    {
        BackColor = CardTheme.Field;
        ForeColor = CardTheme.Value;
        Padding = Padding.Empty;

        _host = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = CardTheme.Field,
            Padding = new Padding(0, 2, 0, 2),
        };
        Controls.Add(_host);
    }

    public event EventHandler<WorkflowElementEntry?>? SelectedEntryChanged;
    public event EventHandler<WorkflowElementEntry>? LocateRequested;
    public event EventHandler<WorkflowElementEntry>? DeleteRequested;

    public WorkflowElementEntry? SelectedEntry => _selected;

    public void SetEntries(IEnumerable<WorkflowElementEntry> entries)
    {
        _suppress = true;
        try
        {
            _host.SuspendLayout();
            _host.Controls.Clear();

            // Dock=Top stacks the last added control at the top; add in reverse so node #0 stays first.
            foreach (var entry in entries.Reverse()) _host.Controls.Add(CreateRow(entry));
            _host.ResumeLayout();
            _host.AutoScrollPosition = new Point(0, 0);
        }
        finally
        {
            _suppress = false;
        }

        ApplySelection();
    }

    public void Select(WorkflowElementEntry? entry)
    {
        _suppress = true;
        try
        {
            _selected = entry;
        }
        finally
        {
            _suppress = false;
        }

        ApplySelection();
    }

    private Control CreateRow(WorkflowElementEntry entry)
    {
        var row = new Panel
        {
            Dock = DockStyle.Top,
            Height = 27,
            BackColor = CardTheme.Field,
            Cursor = Cursors.Hand,
            Tag = entry,
            Margin = Padding.Empty,
        };

        var label = new Label
        {
            Text = entry.Label,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = CardTheme.Value,
            BackColor = CardTheme.Field,
            Font = CardTheme.Font(CardTheme.ValueSize),
            Padding = new Padding(6, 0, 4, 0),
            Cursor = Cursors.Hand,
        };
        var locate = CreateAction("", "定位", CardTheme.ActionCompile);
        var delete = CreateAction("", "删除", CardTheme.ActionStop);
        _toolTip.SetToolTip(locate, "定位");
        _toolTip.SetToolTip(delete, "删除");
        locate.Click += (_, _) => LocateRequested?.Invoke(this, entry);
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, entry);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = false,
            Width = 52,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = CardTheme.Field,
            Margin = Padding.Empty,
            Padding = new Padding(1, 3, 2, 0),
        };
        actions.Controls.Add(locate);
        actions.Controls.Add(delete);

        row.Controls.Add(label);
        row.Controls.Add(actions);

        void Enter(object? _, EventArgs __)
        {
            if (ReferenceEquals(_selected, entry)) return;
            row.BackColor = CardTheme.Hover;
            label.BackColor = CardTheme.Hover;
            actions.BackColor = CardTheme.Hover;
        }

        void Leave(object? _, EventArgs __)
        {
            if (ReferenceEquals(_selected, entry)) return;
            row.BackColor = CardTheme.Field;
            label.BackColor = CardTheme.Field;
            actions.BackColor = CardTheme.Field;
        }

        void Pick(object? _, EventArgs __) => Choose(entry);

        row.MouseEnter += Enter;
        row.MouseLeave += Leave;
        label.MouseEnter += Enter;
        label.MouseLeave += Leave;
        row.Click += Pick;
        label.Click += Pick;

        return row;
    }

    private void Choose(WorkflowElementEntry entry)
    {
        if (_suppress) return;
        _selected = entry;
        ApplySelection();
        SelectedEntryChanged?.Invoke(this, entry);
    }

    private void ApplySelection()
    {
        foreach (Control row in _host.Controls)
        {
            if (row is not Panel || row.Tag is not WorkflowElementEntry entry) continue;
            var selected = ReferenceEquals(_selected, entry);
            row.BackColor = selected ? CardTheme.Hover : CardTheme.Field;

            foreach (Control child in row.Controls)
            {
                if (child is Label label)
                {
                    label.BackColor = row.BackColor;
                }
                else if (child is FlowLayoutPanel actions)
                {
                    actions.BackColor = row.BackColor;
                }
            }
        }
    }

    private static GhostButton CreateAction(string glyph, string accessibleName, Color color) => new()
    {
        Text = glyph,
        AccessibleName = accessibleName,
        TextColor = color,
        AutoSize = false,
        Size = new Size(22, 20),
        FrameWidth = 0F,
        Radius = 3,
        Margin = new Padding(1, 0, 0, 0),
        Font = new Font("Segoe Fluent Icons", 8.5F),
    };
}
