using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using NativeRichTextBox = System.Windows.Forms.RichTextBox;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A rich-text editor whose properties are dependency properties.
/// </summary>
/// <remarks>
/// <para>
/// The editor itself is a native <see cref="System.Windows.Forms.RichTextBox"/> child with its native border removed;
/// this control owns the XAML surface, the theme chrome and the dependency-property model. <see cref="Text"/> and
/// <see cref="Rtf"/> are kept in step with the native editor in both directions, so a two-way binding observes edits.
/// </para>
/// <para>
/// <see cref="SelectionFont"/> and <see cref="SelectionColor"/> are applied to the current selection when set; native
/// selection changes flow back to <see cref="SelectionStart"/> and <see cref="SelectionLength"/>.
/// </para>
/// <para>
/// This is not a WPF <c>RichTextBox</c>: there is no document object model behind the editor and no WPF command
/// surface. Assigning <see cref="Document"/> loads its RTF (or plain text when the RTF is rejected) once; later
/// edits do not write back to that <see cref="FlowDocument"/>.
/// </para>
/// </remarks>
public class RichTextBox : Control
{
    /// <summary>The current plain text; two-way with the native editor.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(RichTextBox),
        new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    /// <summary>The current content as RTF; two-way with the native editor.</summary>
    public static readonly DependencyProperty RtfProperty = DependencyProperty.Register(
        nameof(Rtf),
        typeof(string),
        typeof(RichTextBox),
        new PropertyMetadata(string.Empty, OnRtfPropertyChanged));

    /// <summary>A document loaded into the editor when assigned; it is not kept in sync afterwards.</summary>
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(FlowDocument),
        typeof(RichTextBox),
        new PropertyMetadata(null, OnDocumentPropertyChanged));

    /// <summary>Whether the text can be edited.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(RichTextBox),
        new PropertyMetadata(false, OnEditorPropertyChanged) { AffectsRender = false });

    /// <summary>Whether the Enter key inserts a line break; when false it is swallowed.</summary>
    public static readonly DependencyProperty AcceptsReturnProperty = DependencyProperty.Register(
        nameof(AcceptsReturn),
        typeof(bool),
        typeof(RichTextBox),
        new PropertyMetadata(true, OnEditorPropertyChanged) { AffectsRender = false });

    /// <summary>When the vertical scroll bar is shown.</summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(VerticalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(RichTextBox),
        new PropertyMetadata(ScrollBarVisibility.Auto, OnEditorPropertyChanged) { AffectsRender = false });

    /// <summary>The selection start index.</summary>
    public static readonly DependencyProperty SelectionStartProperty = DependencyProperty.Register(
        nameof(SelectionStart),
        typeof(int),
        typeof(RichTextBox),
        new PropertyMetadata(0, OnSelectionPropertyChanged) { AffectsRender = false });

    /// <summary>The selection length.</summary>
    public static readonly DependencyProperty SelectionLengthProperty = DependencyProperty.Register(
        nameof(SelectionLength),
        typeof(int),
        typeof(RichTextBox),
        new PropertyMetadata(0, OnSelectionPropertyChanged) { AffectsRender = false });

    /// <summary>The font of the current selection, or null to leave it unchanged.</summary>
    public static readonly DependencyProperty SelectionFontProperty = DependencyProperty.Register(
        nameof(SelectionFont),
        typeof(Font),
        typeof(RichTextBox),
        new PropertyMetadata(null, OnSelectionFontPropertyChanged) { AffectsRender = false });

    /// <summary>The colour of the current selection; <see cref="Color.Empty"/> leaves it unchanged.</summary>
    public static readonly DependencyProperty SelectionColorProperty = DependencyProperty.Register(
        nameof(SelectionColor),
        typeof(Color),
        typeof(RichTextBox),
        new PropertyMetadata(Color.Empty, OnSelectionColorPropertyChanged) { AffectsRender = false });

    /// <summary>The border stroke colour; <see cref="Color.Empty"/> uses the active theme border.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(RichTextBox),
        new PropertyMetadata(Color.Empty, OnVisualPropertyChanged));

    /// <summary>The border stroke width per side, in pixels; one pixel by default.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(RichTextBox),
        new PropertyMetadata(new Thickness(1f), OnVisualPropertyChanged));

    private readonly NativeRichTextBox editor;

    // 原生编辑器的变更回写 DP 时，DP 回调不能再写一次原生值，否则整段文本会被重置、光标跳回开头。
    private bool _syncing;

    /// <summary>Creates the wrapper and its native editor.</summary>
    public RichTextBox()
    {
        TabStop = false;
        editor = new NativeRichTextBox
        {
            BorderStyle = BorderStyle.None,
            AutoSize = false,
            Multiline = true,
            TabStop = true,
            DetectUrls = true,
        };

        editor.TextChanged += OnEditorTextChanged;
        editor.SelectionChanged += OnEditorSelectionChanged;
        editor.KeyDown += OnEditorKeyDown;
        editor.KeyUp += (_, e) => OnKeyUp(e);
        editor.MouseDown += (_, e) => OnMouseDown(e);
        editor.MouseUp += (_, e) => OnMouseUp(e);
        editor.MouseMove += (_, e) => OnMouseMove(e);
        editor.MouseEnter += (_, e) => OnMouseEnter(e);
        editor.MouseLeave += (_, e) => OnMouseLeave(e);
        editor.MouseWheel += (_, e) => OnMouseWheel(e);
        editor.GotFocus += (_, e) => OnGotFocus(e);
        editor.LostFocus += (_, e) => OnLostFocus(e);
        editor.HandleCreated += (_, _) => ApplyEditorProperties();
        Controls.Add(editor);

        Theme.Changed += OnThemeChanged;
        ApplyEditorProperties();
    }

    /// <summary>Gets or sets the current plain text.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the current content as RTF.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Rtf
    {
        get => (string?)GetValue(RtfProperty) ?? string.Empty;
        set => SetValue(RtfProperty, value);
    }

    /// <summary>Gets or sets a document loaded into the editor when assigned.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FlowDocument? Document
    {
        get => (FlowDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Gets or sets whether the text can be edited.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Gets or sets whether the Enter key inserts a line break.</summary>
    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty)!;
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>Gets or sets when the vertical scroll bar is shown.</summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>Gets or sets the selection start index.</summary>
    public int SelectionStart
    {
        get => (int)GetValue(SelectionStartProperty)!;
        set => SetValue(SelectionStartProperty, value);
    }

    /// <summary>Gets or sets the selection length.</summary>
    public int SelectionLength
    {
        get => (int)GetValue(SelectionLengthProperty)!;
        set => SetValue(SelectionLengthProperty, value);
    }

    /// <summary>Gets or sets the font of the current selection.</summary>
    public Font? SelectionFont
    {
        get => (Font?)GetValue(SelectionFontProperty);
        set => SetValue(SelectionFontProperty, value);
    }

    /// <summary>Gets or sets the colour of the current selection.</summary>
    public Color SelectionColor
    {
        get => (Color)GetValue(SelectionColorProperty)!;
        set => SetValue(SelectionColorProperty, value);
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

    /// <summary>Raised after <see cref="Text"/> changes, whether from code or from the user.</summary>
    public new event EventHandler? TextChanged;

    /// <summary>Moves focus to the native editor.</summary>
    /// <returns><see langword="true"/> when the editor received focus.</returns>
    public new bool Focus() => Focusable && editor.Focus();

    /// <summary>Selects all text in the editor.</summary>
    public void SelectAll() => editor.SelectAll();

    /// <summary>Appends text to the end of the editor.</summary>
    /// <param name="text">The text to append.</param>
    public void AppendText(string text) => editor.AppendText(text ?? string.Empty);

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = editor.GetPreferredSize(new Size(proposedSize.Width, proposedSize.Height));
        var border = BorderThickness;
        return new Size(
            preferred.Width + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            Math.Max(80, preferred.Height) + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
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
        if (editor.Bounds != inset)
        {
            editor.Bounds = inset;
        }
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 原生编辑器需要不透明底色；没有作者背景时用主题表面色，而不是系统灰。
        if (!HasXamlValue(BackgroundProperty))
        {
            using var brush = new SolidBrush(Theme.Current.ControlBackground);
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
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);

        if (ReferenceEquals(property, WidthProperty)
            || ReferenceEquals(property, HeightProperty)
            || ReferenceEquals(property, MinWidthProperty)
            || ReferenceEquals(property, MinHeightProperty)
            || ReferenceEquals(property, MaxWidthProperty)
            || ReferenceEquals(property, MaxHeightProperty))
        {
            PerformLayout();
            Invalidate();
            return;
        }

        if (ReferenceEquals(property, IsEnabledProperty) && newValue is bool enabled)
        {
            editor.Enabled = enabled;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
            if (Controls.Contains(editor))
            {
                Controls.Remove(editor);
            }

            editor.Dispose();
        }

        base.Dispose(disposing);
    }

    private new Color EffectiveForeground
        => HasXamlValue(ForegroundProperty) ? base.EffectiveForeground : Theme.Current.Foreground;

    private void ApplyEditorProperties()
    {
        if (IsDisposed || editor.IsDisposed)
        {
            return;
        }

        var previous = _syncing;
        _syncing = true;
        try
        {
            editor.ReadOnly = IsReadOnly;
            editor.BorderStyle = BorderStyle.None;
            editor.BackColor = HasXamlValue(BackgroundProperty)
                ? XamlDrawing.ToOpaque(Background, Theme.Current.ControlBackground)
                : Theme.Current.ControlBackground;
            editor.ForeColor = EffectiveForeground;
            editor.Font = Font;
            editor.Enabled = IsEnabled;
            editor.ScrollBars = VerticalScrollBarVisibility switch
            {
                ScrollBarVisibility.Visible => RichTextBoxScrollBars.ForcedVertical,
                ScrollBarVisibility.Hidden or ScrollBarVisibility.Disabled => RichTextBoxScrollBars.None,
                _ => RichTextBoxScrollBars.Vertical,
            };
        }
        finally
        {
            _syncing = previous;
        }

        ApplySelection();
        PerformLayout();
        Invalidate();
    }

    private void ApplySelection()
    {
        if (IsDisposed || editor.IsDisposed)
        {
            return;
        }

        var start = Math.Max(0, Math.Min(SelectionStart, editor.TextLength));
        var length = Math.Max(0, Math.Min(SelectionLength, editor.TextLength - start));
        if (editor.SelectionStart != start || editor.SelectionLength != length)
        {
            editor.Select(start, length);
        }
    }

    // RichTextBox.Rtf 只在句柄已创建时读写；没有句柄时读回空串、写入被丢弃。
    private void EnsureEditorHandle()
    {
        if (editor.IsDisposed || editor.IsHandleCreated)
        {
            return;
        }

        try
        {
            _ = editor.Handle;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine($"[VeloxDev.Xaml] Could not create the rich-text handle: {exception.Message}");
        }
    }

    private string ReadRtf()
    {
        if (editor.IsDisposed)
        {
            return Rtf;
        }

        EnsureEditorHandle();
        try
        {
            return editor.Rtf ?? string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[VeloxDev.Xaml] Could not read RTF: {exception.Message}");
            return Rtf;
        }
    }

    // 原生编辑器的当前状态是事实源；两侧 DP 都在一次同步里更新，避免 Text/Rtf 互相触发。
    private void SyncFromEditor()
    {
        if (IsDisposed || editor.IsDisposed)
        {
            return;
        }

        var previous = _syncing;
        _syncing = true;
        try
        {
            SetValue(TextProperty, editor.Text ?? string.Empty);
            SetValue(RtfProperty, ReadRtf());
            SetValue(SelectionStartProperty, editor.SelectionStart);
            SetValue(SelectionLengthProperty, editor.SelectionLength);
        }
        finally
        {
            _syncing = previous;
        }

        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadRtf(string rtf)
    {
        var previous = _syncing;
        _syncing = true;
        try
        {
            if (string.IsNullOrEmpty(rtf))
            {
                editor.Clear();
            }
            else
            {
                try
                {
                    editor.Rtf = rtf;
                }
                catch (ArgumentException)
                {
                    // 非 RTF 字符串按纯文本处理，至少内容不会丢。
                    editor.Text = rtf;
                }
            }
        }
        finally
        {
            _syncing = previous;
        }

        SyncFromEditor();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        SyncFromEditor();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncing || IsDisposed)
        {
            return;
        }

        var previous = _syncing;
        _syncing = true;
        try
        {
            SetValue(SelectionStartProperty, editor.SelectionStart);
            SetValue(SelectionLengthProperty, editor.SelectionLength);
        }
        finally
        {
            _syncing = previous;
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // RichTextBox 原生始终是多行的；AcceptsReturn=false 时只能在这里吞掉回车。
        if (!AcceptsReturn && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
        }

        OnKeyDown(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            ApplyEditorProperties();
            Invalidate();
        }
    }

    private static void OnTextPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not RichTextBox box || box.IsDisposed || box._syncing)
        {
            return;
        }

        var previous = box._syncing;
        box._syncing = true;
        try
        {
            var text = e.NewValue as string ?? string.Empty;
            if (box.editor.Text != text)
            {
                box.editor.Text = text;
            }
        }
        finally
        {
            box._syncing = previous;
        }

        box.SyncFromEditor();
    }

    private static void OnRtfPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not RichTextBox box || box.IsDisposed || box._syncing)
        {
            return;
        }

        box.LoadRtf(e.NewValue as string ?? string.Empty);
    }

    private static void OnDocumentPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not RichTextBox box || box.IsDisposed)
        {
            return;
        }

        if (e.NewValue is FlowDocument document)
        {
            box.LoadRtf(document.ToRtf(box.EffectiveForeground));
        }
        else if (e.OldValue is FlowDocument)
        {
            box.LoadRtf(string.Empty);
        }
    }

    private static void OnEditorPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is RichTextBox box && !box.IsDisposed)
        {
            box.ApplyEditorProperties();
        }
    }

    private static void OnSelectionPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is RichTextBox box && !box.IsDisposed && !box._syncing)
        {
            box.ApplySelection();
        }
    }

    private static void OnSelectionFontPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is RichTextBox box && !box.IsDisposed && !box._syncing && e.NewValue is Font font)
        {
            box.editor.SelectionFont = font;
        }
    }

    private static void OnSelectionColorPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is RichTextBox box && !box.IsDisposed && !box._syncing && e.NewValue is Color color && color.A > 0)
        {
            box.editor.SelectionColor = color;
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
