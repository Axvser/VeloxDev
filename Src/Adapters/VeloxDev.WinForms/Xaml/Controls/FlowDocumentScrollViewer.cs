using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using NativeRichTextBox = System.Windows.Forms.RichTextBox;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A read-only view of a <see cref="FlowDocument"/> rendered by a native rich-text host.
/// </summary>
/// <remarks>
/// <para>
/// The document is converted to RTF (or plain text when the RTF is rejected) each time <see cref="Document"/> is
/// assigned or the theme changes. The view is not editable and does not write back into the document model.
/// </para>
/// <para>
/// This is the pragmatic WinForms counterpart of WPF's <c>FlowDocumentScrollViewer</c>: there is no paged layout,
/// no zoom control and no find toolbar. The native editor supplies selection, copy and vertical scrolling.
/// </para>
/// </remarks>
public class FlowDocumentScrollViewer : Control
{
    /// <summary>The document shown by this viewer.</summary>
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(FlowDocument),
        typeof(FlowDocumentScrollViewer),
        new PropertyMetadata(null, OnDocumentPropertyChanged));

    /// <summary>When the vertical scroll bar is shown.</summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(VerticalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(FlowDocumentScrollViewer),
        new PropertyMetadata(ScrollBarVisibility.Auto, OnVisualPropertyChanged) { AffectsRender = false });

    /// <summary>The border stroke colour; <see cref="Color.Empty"/> uses the active theme border.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(FlowDocumentScrollViewer),
        new PropertyMetadata(Color.Empty, OnVisualPropertyChanged));

    /// <summary>The border stroke width per side, in pixels; one pixel by default.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(FlowDocumentScrollViewer),
        new PropertyMetadata(new Thickness(1f), OnVisualPropertyChanged));

    private readonly NativeRichTextBox viewer;

    // 文档变更后重设 RTF 会再次触发 TextChanged；期间不把原生文本回写成 RTF。
    private bool _loading;

    /// <summary>Creates the viewer and its native read-only editor.</summary>
    public FlowDocumentScrollViewer()
    {
        TabStop = false;
        viewer = new NativeRichTextBox
        {
            BorderStyle = BorderStyle.None,
            AutoSize = false,
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            DetectUrls = true,
            BackColor = Theme.Current.WindowBackground,
            ForeColor = Theme.Current.Foreground,
        };

        Controls.Add(viewer);
        Theme.Changed += OnThemeChanged;
        ApplyViewerProperties();
    }

    /// <summary>Gets or sets the document shown by this viewer.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FlowDocument? Document
    {
        get => (FlowDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Gets or sets when the vertical scroll bar is shown.</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>Gets or sets the border stroke colour.</summary>
    public Color BorderBrush
    {
        get => (Color)GetValue(BorderBrushProperty)!;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Gets or sets the border stroke width per side, in pixels.</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>Gets the plain text of the current document, or an empty string when there is none.</summary>
    public string PlainText => Document?.ToPlainText() ?? string.Empty;

    /// <summary>Gets the plain text the native host currently renders.</summary>
    public new string Text => viewer.Text;

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = viewer.GetPreferredSize(new Size(proposedSize.Width, proposedSize.Height));
        var border = BorderThickness;
        return new Size(
            preferred.Width + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            Math.Max(120, preferred.Height) + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var content = XamlLayout.ContentBounds(this);
        var border = BorderThickness;
        var inset = new Rectangle(
            content.Left + (int)Math.Ceiling(border.Left),
            content.Top + (int)Math.Ceiling(border.Top),
            Math.Max(0, content.Width - (int)Math.Ceiling(border.Horizontal)),
            Math.Max(0, content.Height - (int)Math.Ceiling(border.Vertical)));
        if (viewer.Bounds != inset)
        {
            viewer.Bounds = inset;
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (!HasXamlValue(BackgroundProperty))
        {
            using var brush = new SolidBrush(Theme.Current.WindowBackground);
            e.Graphics.FillRectangle(brush, ClientRectangle);
            return;
        }

        base.OnPaintBackground(e);
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var border = BorderThickness;
        var brush = BorderBrush.A > 0 ? BorderBrush : Theme.Current.ControlBorder;
        if (brush.A <= 0 || border.IsEmpty)
        {
            return;
        }

        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1f), Math.Max(0, Height - 1f));
        var thickness = Math.Max(Math.Max(border.Left, border.Right), Math.Max(border.Top, border.Bottom));
        if (bounds.Width <= 0f || bounds.Height <= 0f || thickness <= 0f)
        {
            return;
        }

        using var path = XamlDrawing.RoundedRectangle(bounds, 0f);
        using var pen = new Pen(brush, Math.Min(thickness, Math.Min(bounds.Width, bounds.Height) / 2f));
        e.Graphics.DrawPath(pen, path);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
            if (Controls.Contains(viewer))
            {
                Controls.Remove(viewer);
            }

            viewer.Dispose();
        }

        base.Dispose(disposing);
    }

    private new Color EffectiveForeground
        => HasXamlValue(ForegroundProperty) ? base.EffectiveForeground : Theme.Current.Foreground;

    private void ApplyViewerProperties()
    {
        if (IsDisposed || viewer.IsDisposed)
        {
            return;
        }

        viewer.ReadOnly = true;
        viewer.BorderStyle = BorderStyle.None;
        viewer.BackColor = HasXamlValue(BackgroundProperty)
            ? XamlDrawing.ToOpaque(Background, Theme.Current.WindowBackground)
            : Theme.Current.WindowBackground;
        viewer.ForeColor = EffectiveForeground;
        viewer.Font = Font;
        viewer.Enabled = IsEnabled;
        viewer.ScrollBars = VerticalScrollBarVisibility switch
        {
            ScrollBarVisibility.Visible => RichTextBoxScrollBars.ForcedVertical,
            ScrollBarVisibility.Hidden or ScrollBarVisibility.Disabled => RichTextBoxScrollBars.None,
            _ => RichTextBoxScrollBars.Vertical,
        };

        LoadDocument();
        PerformLayout();
        Invalidate();
    }

    private void LoadDocument()
    {
        _loading = true;
        try
        {
            var document = Document;
            if (document is null)
            {
                viewer.Clear();
                return;
            }

            try
            {
                viewer.Rtf = document.ToRtf(EffectiveForeground);
            }
            catch (ArgumentException)
            {
                viewer.Text = document.ToPlainText();
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || _loading)
        {
            return;
        }

        ApplyViewerProperties();
    }

    private static void OnDocumentPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is FlowDocumentScrollViewer host && !host.IsDisposed)
        {
            host.LoadDocument();
        }
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.PerformLayout();
            control.Invalidate();
        }
    }
}
