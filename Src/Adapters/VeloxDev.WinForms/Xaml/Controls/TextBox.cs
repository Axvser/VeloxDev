using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NativeTextBox = System.Windows.Forms.TextBox;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// An editable text field whose properties are dependency properties.
/// </summary>
/// <remarks>
/// <para>
/// The editor itself is a native <see cref="System.Windows.Forms.TextBox"/> child; this class owns the XAML surface,
/// the border and the dependency-property model. Binding <see cref="Text"/> two-way works because a native edit writes
/// the dependency property back through the ordinary local-value path.
/// </para>
/// <para>
/// The native border is disabled: <see cref="BorderBrush"/> and <see cref="BorderThickness"/> paint the outline and
/// the editor is inset inside it. Native input events are forwarded to this control so XAML can bind
/// <c>KeyDown</c>, <c>KeyUp</c> and the pointer events normally.
/// </para>
/// </remarks>
public class TextBox : Control
{
    /// <summary>The current text.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(TextBox),
        new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    /// <summary>Text shown while the field is empty.</summary>
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(TextBox),
        new PropertyMetadata(string.Empty, OnEditorPropertyChanged));

    /// <summary>Whether the text can be edited.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(TextBox),
        new PropertyMetadata(false, OnEditorPropertyChanged));

    /// <summary>Whether the field accepts multiple lines.</summary>
    public static readonly DependencyProperty AcceptsReturnProperty = DependencyProperty.Register(
        nameof(AcceptsReturn),
        typeof(bool),
        typeof(TextBox),
        new PropertyMetadata(false, OnEditorPropertyChanged));

    /// <summary>The maximum number of characters; zero means no limit.</summary>
    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength),
        typeof(int),
        typeof(TextBox),
        new PropertyMetadata(0, OnEditorPropertyChanged));

    /// <summary>How the text is aligned inside the field.</summary>
    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment),
        typeof(TextAlignment),
        typeof(TextBox),
        new PropertyMetadata(TextAlignment.Left, OnEditorPropertyChanged));

    /// <summary>The character used to mask input; <c>'\0'</c> shows the text normally.</summary>
    public static readonly DependencyProperty PasswordCharProperty = DependencyProperty.Register(
        nameof(PasswordChar),
        typeof(char),
        typeof(TextBox),
        new PropertyMetadata('\0', OnEditorPropertyChanged));

    /// <summary>The selection start index.</summary>
    public static readonly DependencyProperty SelectionStartProperty = DependencyProperty.Register(
        nameof(SelectionStart),
        typeof(int),
        typeof(TextBox),
        new PropertyMetadata(0, OnEditorPropertyChanged));

    /// <summary>The selection length.</summary>
    public static readonly DependencyProperty SelectionLengthProperty = DependencyProperty.Register(
        nameof(SelectionLength),
        typeof(int),
        typeof(TextBox),
        new PropertyMetadata(0, OnEditorPropertyChanged));

    /// <summary>The border stroke colour; a translucent black by default.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(TextBox),
        new PropertyMetadata(Color.FromArgb(0x66, 0, 0, 0), OnVisualPropertyChanged));

    /// <summary>The border stroke width per side, in pixels; one pixel by default.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(TextBox),
        new PropertyMetadata(new Thickness(1f), OnVisualPropertyChanged));

    private const int EmSetCueBanner = 0x1501;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, string lParam);

    private readonly NativeTextBox editor;

    // 原生的 TextChanged 回写 DP 时，DP 回调不能再写一次原生 Text，否则光标会跳。
    private bool syncing;

    /// <summary>Creates the wrapper and its native editor.</summary>
    public TextBox()
    {
        TabStop = false;
        editor = new NativeTextBox
        {
            BorderStyle = BorderStyle.None,
            AutoSize = false,
            Multiline = false,
            TabStop = true,
        };
        editor.TextChanged += OnEditorTextChanged;
        editor.KeyDown += (_, e) => OnKeyDown(e);
        editor.KeyUp += (_, e) => OnKeyUp(e);
        editor.MouseDown += (_, e) => OnMouseDown(e);
        editor.MouseUp += (_, e) => OnMouseUp(e);
        editor.MouseMove += (_, e) => OnMouseMove(e);
        editor.MouseEnter += (_, e) => OnMouseEnter(e);
        editor.MouseLeave += (_, e) => OnMouseLeave(e);
        editor.MouseWheel += (_, e) => OnMouseWheel(e);
        editor.GotFocus += (_, e) => OnGotFocus(e);
        editor.LostFocus += (_, e) => OnLostFocus(e);
        editor.HandleCreated += (_, _) => ApplyPlaceholder();
        Controls.Add(editor);
        ApplyEditorProperties();
    }

    /// <summary>Gets or sets the current text.</summary>
    public new string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the text shown while the field is empty.</summary>
    public string PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty) ?? string.Empty;
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Gets or sets whether the text can be edited.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Gets or sets whether the field accepts multiple lines.</summary>
    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty)!;
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>Gets or sets the maximum number of characters; zero means no limit.</summary>
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty)!;
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>Gets or sets how the text is aligned inside the field.</summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>Gets or sets the character used to mask input; <c>'\0'</c> shows the text normally.</summary>
    public char PasswordChar
    {
        get => (char)GetValue(PasswordCharProperty)!;
        set => SetValue(PasswordCharProperty, value);
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

    /// <summary>Selects all text in the field.</summary>
    public void SelectAll() => editor.SelectAll();

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = editor.GetPreferredSize(new Size(proposedSize.Width, proposedSize.Height));
        var border = BorderThickness;
        return new Size(
            preferred.Width + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            Math.Max(23, preferred.Height) + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
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
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var border = BorderThickness;
        if (BorderBrush.A <= 0 || border.IsEmpty)
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
        using var pen = new Pen(BorderBrush, Math.Min(thickness, Math.Min(bounds.Width, bounds.Height) / 2f));
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
            ApplyEditorBounds();
            return;
        }

        if (ReferenceEquals(property, BackColorProperty) && newValue is Color background && !background.IsEmpty)
        {
            editor.BackColor = XamlDrawing.ToOpaque(background, SystemColors.Window);
        }
        else if (ReferenceEquals(property, ForeColorProperty) && newValue is Color foreground && !foreground.IsEmpty)
        {
            editor.ForeColor = foreground;
        }
        else if (ReferenceEquals(property, FontProperty) && newValue is Font font)
        {
            editor.Font = font;
        }
        else if (ReferenceEquals(property, IsEnabledProperty) && newValue is bool enabled)
        {
            editor.Enabled = enabled;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && Controls.Contains(editor))
        {
            Controls.Remove(editor);
            editor.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ApplyEditorProperties()
    {
        if (syncing)
        {
            return;
        }

        syncing = true;
        try
        {
            if (editor.Text != Text)
            {
                editor.Text = Text;
            }

            editor.ReadOnly = IsReadOnly;
            editor.Multiline = AcceptsReturn;
            editor.MaxLength = Math.Max(0, MaxLength);
            editor.TextAlign = TextAlignment switch
            {
                TextAlignment.Right => System.Windows.Forms.HorizontalAlignment.Right,
                TextAlignment.Center => System.Windows.Forms.HorizontalAlignment.Center,
                _ => System.Windows.Forms.HorizontalAlignment.Left,
            };
            editor.UseSystemPasswordChar = false;
            editor.PasswordChar = PasswordChar;
            editor.BackColor = HasXamlValue(BackColorProperty)
                ? XamlDrawing.ToOpaque(Background, SystemColors.Window)
                : SystemColors.Window;
            editor.ForeColor = ForeColor;
            editor.Font = Font;
            editor.Enabled = IsEnabled;
            ApplyPlaceholder();

            var start = Clamp(SelectionStart, 0, editor.TextLength);
            var length = Clamp(SelectionLength, 0, editor.TextLength - start);
            if (editor.SelectionStart != start)
            {
                editor.SelectionStart = start;
            }

            if (editor.SelectionLength != length)
            {
                editor.SelectionLength = length;
            }
        }
        finally
        {
            syncing = false;
        }

        ApplyEditorBounds();
    }

    private void ApplyPlaceholder()
    {
        if (editor.IsDisposed || !editor.IsHandleCreated)
        {
            return;
        }

        SendMessage(editor.Handle, EmSetCueBanner, IntPtr.Zero, PlaceholderText ?? string.Empty);
    }

    private void ApplyEditorBounds()
    {
        if (IsDisposed || editor.IsDisposed)
        {
            return;
        }

        PerformLayout();
        Invalidate();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (syncing)
        {
            return;
        }

        syncing = true;
        try
        {
            SetValue(TextProperty, editor.Text);
        }
        finally
        {
            syncing = false;
        }
    }

    private static void OnTextPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TextBox textBox && !textBox.IsDisposed)
        {
            if (!textBox.syncing)
            {
                textBox.ApplyEditorProperties();
            }

            textBox.TextChanged?.Invoke(textBox, EventArgs.Empty);
        }
    }

    private static void OnEditorPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TextBox textBox && !textBox.IsDisposed)
        {
            textBox.ApplyEditorProperties();
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

/// <summary>
/// A <see cref="TextBox"/> that masks its input.
/// </summary>
/// <remarks><see cref="Password"/> is the unmasked text as a string; it is an alias for <see cref="TextBox.Text"/>.</remarks>
public class PasswordBox : TextBox
{
    /// <summary>The unmasked password text; it is the same dependency property as <see cref="TextBox.Text"/>.</summary>
    public static readonly DependencyProperty PasswordProperty = TextProperty;

    /// <summary>Creates the field with a bullet mask.</summary>
    public PasswordBox()
    {
        PasswordChar = '●';
    }

    /// <summary>Gets or sets the unmasked password text.</summary>
    public string Password
    {
        get => Text;
        set => Text = value;
    }
}
