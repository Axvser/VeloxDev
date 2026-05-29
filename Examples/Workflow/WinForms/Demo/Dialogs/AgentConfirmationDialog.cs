using System;
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.AI.Workflow;

namespace Demo;

/// <summary>
/// 供 AgentHelper.ConfirmationHandler 使用的深色风格确认对话框。
/// </summary>
internal sealed class AgentConfirmationDialog : Form
{
    private static readonly Color BgDeep   = Color.FromArgb(0x1a, 0x1a, 0x2e);
    private static readonly Color BgHead   = Color.FromArgb(0x16, 0x21, 0x3e);
    private static readonly Color Gold     = Color.FromArgb(0xff, 0xd1, 0x66);
    private static readonly Color GoldDark = Color.FromArgb(0x2a, 0x1f, 0x00);
    private static readonly Color TextMain = Color.FromArgb(0xe0, 0xe0, 0xe0);

    public AgentConfirmationResult Result { get; private set; } = AgentConfirmationResult.Deny;

    public AgentConfirmationDialog(string operationKey, string description)
    {
        SuspendLayout();

        Text = "Agent · 操作确认";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = BgDeep;
        Width = 460;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, 0, 16);

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
            Text = "⚠️  Agent · 操作确认",
            ForeColor = Gold,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        var keyPanel = new Panel
        {
            BackColor = GoldDark,
            BorderStyle = BorderStyle.FixedSingle,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(0, 0, 0, 0),
        };
        keyPanel.Controls.Add(new Label
        {
            Text = operationKey,
            ForeColor = Gold,
            Font = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            AutoSize = true,
        });

        var headerFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = false,
        };
        headerFlow.Controls.Add(titleLabel);
        headerFlow.Controls.Add(keyPanel);
        header.Controls.Add(headerFlow);

        // ── Body ──────────────────────────────────────────────────────────
        var body = new Panel
        {
            BackColor = BgDeep,
            Dock = DockStyle.Top,
            Padding = new Padding(18, 14, 18, 6),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var descLabel = new Label
        {
            Text = description,
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            AutoSize = false,
            Width = 420,
        };
        using var g = CreateGraphics();
        var measured = TextRenderer.MeasureText(g, description, descLabel.Font, new Size(420, 0),
            TextFormatFlags.WordBreak);
        descLabel.Height = measured.Height + 4;
        body.Controls.Add(descLabel);

        // ── Buttons ───────────────────────────────────────────────────────
        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Top,
            Padding = new Padding(18, 10, 18, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = BgDeep,
            WrapContents = false,
        };

        var alwaysBtn = MakeBtn("✓✓  本次会话始终同意",
            Color.FromArgb(0x0d, 0x3b, 0x1a), Color.FromArgb(0x6b, 0xff, 0xb8), Color.FromArgb(0x6b, 0xff, 0xb8));
        var onceBtn   = MakeBtn("✓  仅同意一次",
            Color.FromArgb(0x0f, 0x34, 0x60), Color.FromArgb(0x7e, 0xc8, 0xff), Color.FromArgb(0x7e, 0xc8, 0xff));
        var denyBtn   = MakeBtn("✕  拒绝",
            Color.FromArgb(0x3b, 0x00, 0x00), Color.FromArgb(0xff, 0x6b, 0x6b), Color.FromArgb(0xff, 0x6b, 0x6b));

        alwaysBtn.Click += (_, _) => { Result = AgentConfirmationResult.AllowAlways; DialogResult = DialogResult.OK; Close(); };
        onceBtn.Click   += (_, _) => { Result = AgentConfirmationResult.AllowOnce;   DialogResult = DialogResult.OK; Close(); };
        denyBtn.Click   += (_, _) => { Result = AgentConfirmationResult.Deny;        DialogResult = DialogResult.Cancel; Close(); };

        btnPanel.Controls.Add(alwaysBtn);
        btnPanel.Controls.Add(onceBtn);
        btnPanel.Controls.Add(denyBtn);

        // ── Layout ────────────────────────────────────────────────────────
        Controls.Add(btnPanel);
        Controls.Add(body);
        Controls.Add(header);

        ResumeLayout(false);
        PerformLayout();
    }

    private static Button MakeBtn(string text, Color bg, Color fg, Color border)
    {
        var btn = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = fg,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            Height = 34,
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(8, 0, 8, 0),
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = border;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
        return btn;
    }
}
