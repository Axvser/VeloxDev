using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NativeTextBox = System.Windows.Forms.TextBox;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// The shared editable text field behind <see cref="TextBox"/> and <see cref="PasswordBox"/>.
/// </summary>
/// <remarks>
/// <para>
/// The editor itself is a native <see cref="System.Windows.Forms.TextBox"/> child; this class owns the XAML surface,
/// the border and the dependency-property model. A derived control exposes the committed value under its aligned
/// name (<see cref="TextBox.Text"/> or <see cref="PasswordBox.Password"/>), and a native edit writes it back through
/// the ordinary local-value path.
/// </para>
/// <para>
/// The native border is disabled: <see cref="BorderBrush"/> and <see cref="BorderThickness"/> paint the outline and
/// the editor is inset inside it. Native input events are forwarded to this control so XAML can bind
/// <c>KeyDown</c>, <c>KeyUp</c> and the pointer events normally.
/// </para>
/// </remarks>
public abstract class TextBoxBase : Control
{
    /// <summary>Text shown while the field is empty.</summary>
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(TextBoxBase),
        new PropertyMetadata(string.Empty, OnEditorPropertyChanged));

    /// <summary>Whether the text can be edited.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(TextBoxBase),
        new PropertyMetadata(false, OnEditorPropertyChanged));

    /// <summary>Whether the field accepts multiple lines.</summary>
    public static readonly DependencyProperty AcceptsReturnProperty = DependencyProperty.Register(
        nameof(AcceptsReturn),
        typeof(bool),
        typeof(TextBoxBase),
        new PropertyMetadata(false, OnEditorPropertyChanged));

    /// <summary>The maximum number of characters; zero means no limit.</summary>
    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength),
        typeof(int),
        typeof(TextBoxBase),
        new PropertyMetadata(0, OnEditorPropertyChanged));

    /// <summary>How the text is aligned inside the field.</summary>
    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment),
        typeof(TextAlignment),
        typeof(TextBoxBase),
        new PropertyMetadata(TextAlignment.Left, OnEditorPropertyChanged));

    /// <summary>The character used to mask input; <c>'\0'</c> shows the text normally.</summary>
    public static readonly DependencyProperty PasswordCharProperty = DependencyProperty.Register(
        nameof(PasswordChar),
        typeof(char),
        typeof(TextBoxBase),
        new PropertyMetadata('\0', OnEditorPropertyChanged));

    /// <summary>The selection start index.</summary>
    public static readonly DependencyProperty SelectionStartProperty = DependencyProperty.Register(
        nameof(SelectionStart),
        typeof(int),
        typeof(TextBoxBase),
        new PropertyMetadata(0, OnEditorPropertyChanged));

    /// <summary>The selection length.</summary>
    public static readonly DependencyProperty SelectionLengthProperty = DependencyProperty.Register(
        nameof(SelectionLength),
        typeof(int),
        typeof(TextBoxBase),
        new PropertyMetadata(0, OnEditorPropertyChanged));

    /// <summary>The border stroke brush; a translucent black by default.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Brush),
        typeof(TextBoxBase),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), OnVisualPropertyChanged));

    /// <summary>The border stroke width per side, in pixels; one pixel by default.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(TextBoxBase),
        new PropertyMetadata(new Thickness(1f), OnVisualPropertyChanged));

    private const int EmSetCueBanner = 0x1501;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, string lParam);

    private readonly NativeTextBox editor;

    // 原生的 TextChanged 回写 DP 时，DP 回调不能再写一次原生 Text，否则光标会跳。
    private bool syncing;

    /// <summary>Creates the wrapper and its native editor.</summary>
    protected TextBoxBase()
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

    /// <summary>The value the native editor commits; the derived control exposes it under its aligned name.</summary>
    protected abstract string CurrentText { get; set; }

    /// <summary>Whether the native editor and the dependency property are currently being synchronized.</summary>
    protected bool Syncing => syncing;

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

    /// <summary>Gets or sets the border stroke brush.</summary>
    /// <remarks>GDI+ pens are solid, so a gradient outline is flattened to its <see cref="Brush.ApproximateColor"/>.</remarks>
    public Brush? BorderBrush
    {
        get => GetValue(BorderBrushProperty) as Brush;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Gets or sets the border stroke width per side, in pixels.</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

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

        // 原生单行 EDIT 在比行高大的窗口里文字贴顶；把编辑器本身按首选高度垂直居中，文字才落在框中间。
        if (!editor.Multiline)
        {
            var preferred = editor.PreferredHeight;
            if (preferred > 0 && preferred < inset.Height)
            {
                inset = new Rectangle(inset.Left, inset.Top + ((inset.Height - preferred) / 2), inset.Width, preferred);
            }
        }

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
        if (BorderBrush is not { } borderBrush || borderBrush.ApproximateColor.A <= 0 || border.IsEmpty)
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
        using var pen = new Pen(borderBrush, Math.Min(thickness, Math.Min(bounds.Width, bounds.Height) / 2f));
        pen.Draw(e.Graphics, path);
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        // 编辑器只占内容区中部；点在上下留白也要聚焦，否则框看起来可点、实际没反应。
        if (!editor.IsDisposed)
        {
            editor.Focus();
        }

        base.OnMouseDown(e);
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

        if (ReferenceEquals(property, BackgroundProperty) && newValue is Brush background)
        {
            editor.BackColor = XamlDrawing.ToOpaque(background.ApproximateColor, SystemColors.Window);
        }
        else if (ReferenceEquals(property, ForegroundProperty) && newValue is Brush foreground)
        {
            editor.ForeColor = foreground.ApproximateColor;
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

    /// <summary>Copies every dependency property to the native editor.</summary>
    protected void ApplyEditorProperties()
    {
        if (syncing)
        {
            return;
        }

        syncing = true;
        try
        {
            if (editor.Text != CurrentText)
            {
                editor.Text = CurrentText;
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
            editor.BackColor = HasXamlValue(BackgroundProperty) && Background is { } background
                ? XamlDrawing.ToOpaque(background.ApproximateColor, SystemColors.Window)
                : SystemColors.Window;
            editor.ForeColor = EffectiveForeground;
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
            CurrentText = editor.Text;
        }
        finally
        {
            syncing = false;
        }
    }

    private static void OnEditorPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TextBoxBase textBox && !textBox.IsDisposed)
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

/// <summary>An editable text field exposing <see cref="Text"/>.</summary>
public class TextBox : TextBoxBase
{
    /// <summary>The current text.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(TextBox),
        new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    /// <summary>Gets or sets the current text.</summary>
    public new string Text
    {
        get => CurrentText;
        set => CurrentText = value;
    }

    /// <summary>Raised after <see cref="Text"/> changes, whether from code or from the user.</summary>
    public new event EventHandler? TextChanged;

    /// <inheritdoc />
    protected override string CurrentText
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    private static void OnTextPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is TextBox textBox && !textBox.IsDisposed)
        {
            if (!textBox.Syncing)
            {
                textBox.ApplyEditorProperties();
            }

            textBox.TextChanged?.Invoke(textBox, EventArgs.Empty);
        }
    }
}

/// <summary>A <see cref="TextBoxBase"/> that masks its input and exposes only <see cref="Password"/>.</summary>
public class PasswordBox : TextBoxBase
{
    /// <summary>The unmasked password text.</summary>
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.Register(
        nameof(Password),
        typeof(string),
        typeof(PasswordBox),
        new PropertyMetadata(string.Empty, OnPasswordPropertyChanged));

    /// <summary>Creates the field with a bullet mask.</summary>
    public PasswordBox()
    {
        PasswordChar = '●';
    }

    /// <summary>Gets or sets the unmasked password text.</summary>
    public string Password
    {
        get => CurrentText;
        set => CurrentText = value;
    }

    /// <summary>Raised after <see cref="Password"/> changes, whether from code or from the user.</summary>
    public event EventHandler? PasswordChanged;

    /// <inheritdoc />
    protected override string CurrentText
    {
        get => (string?)GetValue(PasswordProperty) ?? string.Empty;
        set => SetValue(PasswordProperty, value);
    }

    private static void OnPasswordPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is PasswordBox passwordBox && !passwordBox.IsDisposed)
        {
            if (!passwordBox.Syncing)
            {
                passwordBox.ApplyEditorProperties();
            }

            passwordBox.PasswordChanged?.Invoke(passwordBox, EventArgs.Empty);
        }
    }
}
