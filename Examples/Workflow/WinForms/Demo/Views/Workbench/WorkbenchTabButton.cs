using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Demo.Views.Workbench;

/// <summary>
/// One tab in the workbench's two strips — the editor tabs over the canvas and the panel tabs over the inspector.
///
/// Avalonia gets this from a radio-button template (background, 2px accent underline, no border). WinForms'
/// flat button can outline every edge but cannot draw a single bottom border, so the face is painted here:
/// hover lifts the ground, the active tab takes the editor's darker ground and the accent underline.
/// </summary>
internal sealed class WorkbenchTabButton : Button
{
    private bool _hover;
    private bool _isActive;

    public WorkbenchTabButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        AutoSize = false;
        Height = 32;
        Width = 78;
        Padding = Padding.Empty;
        Margin = Padding.Empty;
        TabStop = false;
        Cursor = Cursors.Hand;
        BackColor = WorkbenchPalette.Panel;
        ForeColor = WorkbenchPalette.TextTertiary;
        Font = CardTheme.Font(9.5f);
    }

    /// <summary>Whether this tab is the visible one; the strip's owner sets exactly one.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            Invalidate();
        }
    }

    /// <summary>The ground the strip sits on, so an inactive tab blends into it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Ground { get; set; } = WorkbenchPalette.Panel;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        g.Clear(_isActive ? WorkbenchPalette.Editor : _hover ? WorkbenchPalette.Hover : Ground);

        if (_isActive)
        {
            using var underline = new SolidBrush(WorkbenchPalette.Accent);
            g.FillRectangle(underline, 0, Height - 2, Width, 2);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            _isActive ? WorkbenchPalette.TextPrimary : _hover ? WorkbenchPalette.TextPrimary : WorkbenchPalette.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
