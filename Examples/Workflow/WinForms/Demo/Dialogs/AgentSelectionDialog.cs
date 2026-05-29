using System;
using System.Drawing;
using System.Windows.Forms;

namespace Demo;

/// <summary>
/// 供 AgentHelper.SelectionHandler 使用的深色风格选择对话框。
/// </summary>
internal sealed class AgentSelectionDialog : Form
{
    private static readonly Color BgDeep  = Color.FromArgb(0x1a, 0x1a, 0x2e);
    private static readonly Color BgHead  = Color.FromArgb(0x16, 0x21, 0x3e);
    private static readonly Color AccentBlue = Color.FromArgb(0x7e, 0xc8, 0xff);
    private static readonly Color BtnBg   = Color.FromArgb(0x0f, 0x34, 0x60);
    private static readonly Color TextMain = Color.FromArgb(0xe0, 0xe0, 0xe0);
    private static readonly Color TextDim  = Color.FromArgb(0x88, 0x88, 0x88);
    private static readonly Color CancelBg = Color.FromArgb(0x2a, 0x2a, 0x3e);
    private static readonly Color CancelBorder = Color.FromArgb(0x44, 0x44, 0x44);

    public string? ChosenOption { get; private set; }

    public AgentSelectionDialog(string prompt, string[] options)
    {
        ArgumentNullException.ThrowIfNull(options);

        SuspendLayout();

        Text = "Agent · 请选择";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = BgDeep;
        Width = 460;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, 0, 12);

        // ── Header ────────────────────────────────────────────────────────
        var header = new Panel
        {
            BackColor = BgHead,
            Dock = DockStyle.Top,
            Padding = new Padding(18, 14, 18, 14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var titleLabel = new Label
        {
            Text = "🤖  Agent · 请选择",
            ForeColor = AccentBlue,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        };

        var promptLabel = new Label
        {
            Text = prompt,
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            AutoSize = false,
            Width = 420,
            AutoEllipsis = false,
        };
        using var g = CreateGraphics();
        var measured = TextRenderer.MeasureText(g, prompt, promptLabel.Font, new Size(420, 0),
            TextFormatFlags.WordBreak);
        promptLabel.Height = measured.Height + 4;

        var headerFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = false,
        };
        headerFlow.Controls.Add(titleLabel);
        headerFlow.Controls.Add(promptLabel);
        header.Controls.Add(headerFlow);

        // ── Options ───────────────────────────────────────────────────────
        var optionsFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(16, 12, 16, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            BackColor = BgDeep,
            WrapContents = false,
        };

        foreach (var opt in options)
        {
            var captured = opt;
            var btn = MakeOptionButton(opt, BtnBg, TextMain, AccentBlue);
            btn.Click += (_, _) => { ChosenOption = captured; DialogResult = DialogResult.OK; Close(); };
            optionsFlow.Controls.Add(btn);
        }

        var cancelBtn = MakeOptionButton("取消（不选择）", CancelBg, TextDim, CancelBorder);
        cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        optionsFlow.Controls.Add(cancelBtn);

        // ── Layout ────────────────────────────────────────────────────────
        var wrapper = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgDeep,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        wrapper.Controls.Add(optionsFlow);

        Controls.Add(wrapper);
        Controls.Add(header);

        ResumeLayout(false);
        PerformLayout();
    }

    private static Button MakeOptionButton(string text, Color bg, Color fg, Color border)
    {
        var btn = new Button
        {
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = fg,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Width = 420,
            Height = 38,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(10, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = border;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
        return btn;
    }
}
