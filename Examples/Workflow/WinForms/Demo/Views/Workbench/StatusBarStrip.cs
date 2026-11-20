using System.Drawing;
using System.Windows.Forms;

using Demo.Views.Workflow;

namespace Demo.Views.Workbench;

/// <summary>
/// The 24px status bar: run state, compiled segment count and element totals on the left, realized count,
/// zoom and the gesture hint on the right — the same set the Avalonia shell binds, in the same order.
///
/// A plain <see cref="Panel"/> with labels, painted with the status bar's top hairline. The owner pushes text
/// into <see cref="SetState"/> and the label properties; nothing here reads the workflow model.
/// </summary>
internal sealed class StatusBarStrip : Panel
{
    public StatusBarStrip()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        BackColor = WorkbenchPalette.StatusBar;
        Padding = new Padding(8, 0, 8, 0);

        RunDot = CreateLabel("●", WorkbenchPalette.Success);
        RunDot.Font = CardTheme.Font(7.5f);

        RunState = CreateLabel("空闲", WorkbenchPalette.StatusBarText);
        Compiled = CreateLabel("编译 0 段", WorkbenchPalette.StatusBarText);
        Elements = CreateLabel("元素 0", WorkbenchPalette.StatusBarText);
        VisibleCount = CreateLabel("可见 0", WorkbenchPalette.StatusBarText);
        Zoom = CreateLabel("100%", WorkbenchPalette.StatusBarText);
        Hint = CreateLabel("Ctrl+滚轮缩放", WorkbenchPalette.TextTertiary);

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            BackColor = WorkbenchPalette.StatusBar,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        left.Controls.Add(RunDot);
        left.Controls.Add(RunState);
        left.Controls.Add(Compiled);
        left.Controls.Add(Elements);

        // Right to left would put the hint at the far right but reverse the reading order; adding the labels in
        // display order and right-aligning the flow keeps "可见 → zoom → hint" as the Avalonia shell reads.
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = WorkbenchPalette.StatusBar,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        right.Controls.Add(VisibleCount);
        right.Controls.Add(Zoom);
        right.Controls.Add(Hint);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = WorkbenchPalette.StatusBar,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);

        Controls.Add(layout);
    }

    /// <summary>The state dot; its colour is the only thing that changes between idle and running.</summary>
    public Label RunDot { get; }

    /// <summary>"空闲" / "运行中".</summary>
    public Label RunState { get; }

    /// <summary>"编译 N 段".</summary>
    public Label Compiled { get; }

    /// <summary>"元素 N（节点 … · 连线 …）".</summary>
    public Label Elements { get; }

    /// <summary>"可见 N（节点 … · 连线 …）".</summary>
    public Label VisibleCount { get; }

    /// <summary>"100%".</summary>
    public Label Zoom { get; }

    /// <summary>"Ctrl+滚轮缩放".</summary>
    public Label Hint { get; }

    /// <summary>Paints the same state the status text reports, so the dot and the words cannot disagree.</summary>
    public void SetState(bool running)
    {
        RunDot.ForeColor = running ? WorkbenchPalette.Warning : WorkbenchPalette.Success;
        RunState.Text = running ? "运行中" : "空闲";
        RunState.ForeColor = running ? WorkbenchPalette.Warning : WorkbenchPalette.StatusBarText;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var pen = new Pen(WorkbenchPalette.StatusBarBorder);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }

    private static Label CreateLabel(string text, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        BackColor = Color.Transparent,
        ForeColor = color,
        Font = CardTheme.Font(8.5f),
        Margin = new Padding(6, 4, 6, 0),
    };
}
