using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A self-drawn push button with hover, press and keyboard states.</summary>
public class Button : ButtonBase
{
    /// <summary>The text drawn inside the button.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(Button),
            new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The font used to draw the text.</summary>
    public static readonly DependencyProperty FontProperty =
        DependencyProperty.Register(
            nameof(Font), typeof(Font), typeof(Button),
            new PropertyMetadata(DefaultFont) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The text colour as a brush value; an empty colour uses the active theme.</summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(
            nameof(Foreground), typeof(XamlBrush), typeof(Button),
            new PropertyMetadata(null) { AffectsRender = true });

    private bool _hover;
    private bool _pressed;
    private bool _keyDown;

    /// <summary>Creates a focusable button with theme metrics.</summary>
    public Button()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Cursor = Cursors.Hand;
        Padding = new XamlThickness(14f, 6f);
        MinHeight = Theme.Current.ControlHeight;
        CornerRadius = Theme.Current.CornerRadius;
    }

    /// <summary>The text drawn inside the button.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty)!;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>The font used to draw the text, composed with the inherited typography properties.</summary>
    public Font Font
    {
        get => ResolveFont((Font)GetValue(FontProperty)!);
        set
        {
            if (value is not null)
            {
                SetValue(FontProperty, value);
            }
        }
    }

    /// <summary>The text colour; an empty colour uses the active theme.</summary>
    /// <remarks>
    /// The dependency property stores a brush, so a style, trigger or binding may supply a
    /// <see cref="VeloxDev.WinForms.Xaml.Brush"/>, a hex colour string or a <see cref="Color"/>. The CLR wrapper
    /// reads back the representative colour.
    /// </remarks>
    public Color Foreground
    {
        get => GetValue(ForegroundProperty) is XamlBrush brush ? brush.ApproximateColor : Color.Empty;
        set
        {
            if (value.IsEmpty)
            {
                ClearValue(ForegroundProperty);
            }
            else
            {
                SetValue(ForegroundProperty, new SolidColorBrush(value));
            }
        }
    }

    /// <summary>Raised when the button is clicked by the pointer or the keyboard.</summary>
    public event EventHandler? Click;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var sample = string.IsNullOrEmpty(Text) ? "M" : Text;
        var measured = VisualText.Measure(sample, Font, 0f, wrap: false, RenderDpi);
        var width = (int)Math.Ceiling(measured.Width + Padding.Horizontal + 2);
        var height = Math.Max(
            Theme.Current.ControlHeight,
            (int)Math.Ceiling(measured.Height + Padding.Vertical + 2));
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics) => OnRender(graphics);

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var background = Background?.ApproximateColor
            ?? (!IsEnabled
                ? theme.DisabledBackground
                : _pressed
                    ? theme.ControlPressed
                    : _hover
                        ? theme.ControlHover
                        : theme.ControlBackground);
        var border = BorderBrush?.ApproximateColor ?? theme.ControlBorder;
        var bounds = new Rectangle(0, 0, Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        var thickness = BorderThickness.IsEmpty ? new XamlThickness(1f) : BorderThickness;
        XamlDrawing.PaintChrome(
            graphics,
            bounds,
            background,
            border,
            thickness,
            CornerRadius > 0 ? CornerRadius : theme.CornerRadius);

        if (IsFocusVisible)
        {
            using var focusPath = XamlDrawing.RoundedRectangle(
                new RectangleF(1.5f, 1.5f, Math.Max(0f, bounds.Width - 3f), Math.Max(0f, bounds.Height - 3f)),
                Math.Max(0f, (CornerRadius > 0 ? CornerRadius : theme.CornerRadius) - 1f));
            using var focusPen = new Pen(theme.FocusRing, 1.6f);
            graphics.DrawPath(focusPen, focusPath);
        }

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var content = new Rectangle(
            (int)Math.Ceiling(Padding.Left) + 1,
            (int)Math.Ceiling(Padding.Top) + 1,
            Math.Max(0, bounds.Width - (int)Math.Ceiling(Padding.Horizontal) - 2),
            Math.Max(0, bounds.Height - (int)Math.Ceiling(Padding.Vertical) - 2));
        var foreground = Foreground.IsEmpty
            ? (IsEnabled ? theme.Foreground : theme.DisabledForeground)
            : Foreground;
        VisualText.Draw(this,
            graphics,
            Text,
            Font,
            foreground,
            content,
            wrap: false,
            StringAlignment.Center,
            StringAlignment.Center);
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        _hover = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        _hover = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        _pressed = true;
        InvalidateVisual();
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        InvalidateVisual();
        args.Handled = true;
        if (IsEnabled && AbsoluteBounds.Contains(args.Position))
        {
            OnClick();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (!IsEnabled || _keyDown)
        {
            return;
        }

        if (args.KeyCode is Keys.Space or Keys.Enter)
        {
            _keyDown = true;
            if (args.KeyCode == Keys.Enter)
            {
                OnClick();
            }

            args.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(VisualKeyEventArgs args)
    {
        if (args.KeyCode is Keys.Space or Keys.Enter)
        {
            var activate = args.KeyCode == Keys.Space && _keyDown && IsEnabled;
            _keyDown = false;
            args.Handled = true;
            if (activate)
            {
                OnClick();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus() => InvalidateVisual();

    /// <inheritdoc />
    protected override void OnLostFocus() => InvalidateVisual();

    /// <summary>Raises <see cref="Click"/> after executing <see cref="ButtonBase.Command"/>.</summary>
    protected virtual void OnClick()
    {
        if (!IsEnabled)
        {
            return;
        }

        ExecuteCommand();
        Click?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// A self-drawn text field with caret, selection, clipboard, undo and multi-line editing.
/// </summary>
/// <remarks>
/// <para>
/// The field paints its own text, selection and caret, and receives characters through
/// <see cref="VisualHost.ProcessTextInput"/>. Navigation, editing and <c>Ctrl+Z</c>/<c>Ctrl+Y</c> are handled in
/// <see cref="OnKeyDown"/>; the edit history is the self-drawn path's own snapshot stack.
/// </para>
/// <para>
/// While the field has focus it also hosts a private <see cref="NativeControlHost"/> over its content rectangle. The
/// hosted <see cref="System.Windows.Forms.TextBox"/> is the native island that supplies IME composition and an
/// accessibility peer, and the self-drawn text and caret are hidden while it is active. The island is a real HWND, so
/// it always paints above the visual tree and cannot be transformed, blended or clipped: multi-line overflow is
/// clipped by the visual path, and only the content rectangle is covered — the border and padding stay visual.
/// </para>
/// </remarks>
public class TextBox : VisualElement
{
    /// <summary>The current text.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(TextBox),
            new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The text shown while the field is empty.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder), typeof(string), typeof(TextBox),
            new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The font used to draw the text.</summary>
    public static readonly DependencyProperty FontProperty =
        DependencyProperty.Register(
            nameof(Font), typeof(Font), typeof(TextBox),
            new PropertyMetadata(DefaultFont) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The text colour as a brush value; an empty colour uses the active theme.</summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(
            nameof(Foreground), typeof(XamlBrush), typeof(TextBox),
            new PropertyMetadata(null) { AffectsRender = true });

    /// <summary>Whether the text can be edited.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether the text is drawn as bullets.</summary>
    public static readonly DependencyProperty IsPasswordProperty =
        DependencyProperty.Register(
            nameof(IsPassword), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Whether Enter inserts a line break and the field measures and draws multiple lines.</summary>
    public static readonly DependencyProperty AcceptsReturnProperty =
        DependencyProperty.Register(
            nameof(AcceptsReturn), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>Whether overlong lines wrap at the content width instead of staying on one line.</summary>
    public static readonly DependencyProperty WrapProperty =
        DependencyProperty.Register(
            nameof(Wrap), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false) { AffectsMeasure = true, AffectsRender = true });

    private readonly System.Windows.Forms.Timer _caretTimer;
    private readonly System.Windows.Forms.TextBox _editor;
    private readonly NativeControlHost _nativeHost;
    private readonly Stack<TextSnapshot> _undo = new();
    private readonly Stack<TextSnapshot> _redo = new();
    private readonly List<TextLine> _lines = [];
    private int _selectionAnchor;
    private int _caretIndex;
    private bool _dragging;
    private bool _caretVisible;
    private bool _nativeActive;
    private bool _syncingNative;
    private bool _suppressHistoryReset;
    private bool _focusHandoff;
    private float _horizontalOffset;
    private float _lineHeight = 16f;
    private float _laidOutWidth = -1f;
    private bool _layoutDirty = true;

    /// <summary>Creates a focusable text field with theme metrics and its native editor island.</summary>
    public TextBox()
    {
        _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
        _caretTimer.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };

        _editor = new System.Windows.Forms.TextBox
        {
            BorderStyle = BorderStyle.None,
            AutoSize = false,
            TabStop = false,
            HideSelection = false,
            Visible = false,
        };
        _editor.TextChanged += OnNativeTextChanged;
        _editor.KeyUp += (_, _) => SyncSelectionFromNative();
        _editor.MouseUp += (_, _) => SyncSelectionFromNative();
        _editor.LostFocus += OnNativeEditorLostFocus;

        _nativeHost = Add(new NativeControlHost());
        _nativeHost.Child = _editor;
        _nativeHost.Visibility = VeloxDev.WinForms.Xaml.Controls.Visibility.Collapsed;

        Focusable = true;
        IsHitTestVisible = true;
        Cursor = Cursors.IBeam;
        Padding = new XamlThickness(8f, 6f);
        MinHeight = Theme.Current.ControlHeight;
        CornerRadius = Theme.Current.CornerRadius;
    }

    /// <summary>The current text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty)!;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>The text shown while the field is empty.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty)!;
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    /// <summary>The font used to draw the text, composed with the inherited typography properties.</summary>
    public Font Font
    {
        get => ResolveFont((Font)GetValue(FontProperty)!);
        set
        {
            if (value is not null)
            {
                SetValue(FontProperty, value);
            }
        }
    }

    /// <summary>The text colour; an empty colour uses the active theme.</summary>
    /// <remarks>
    /// The dependency property stores a brush, so a style, trigger or binding may supply a
    /// <see cref="VeloxDev.WinForms.Xaml.Brush"/>, a hex colour string or a <see cref="Color"/>. The CLR wrapper
    /// reads back the representative colour.
    /// </remarks>
    public Color Foreground
    {
        get => GetValue(ForegroundProperty) is XamlBrush brush ? brush.ApproximateColor : Color.Empty;
        set
        {
            if (value.IsEmpty)
            {
                ClearValue(ForegroundProperty);
            }
            else
            {
                SetValue(ForegroundProperty, new SolidColorBrush(value));
            }
        }
    }

    /// <summary>Whether the text can be edited.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Whether the text is drawn as bullets.</summary>
    public bool IsPassword
    {
        get => (bool)GetValue(IsPasswordProperty)!;
        set => SetValue(IsPasswordProperty, value);
    }

    /// <summary>Whether Enter inserts a line break and the field measures and draws multiple lines.</summary>
    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty)!;
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>Whether overlong lines wrap at the content width instead of staying on one line.</summary>
    public bool Wrap
    {
        get => (bool)GetValue(WrapProperty)!;
        set => SetValue(WrapProperty, value);
    }

    /// <summary>The first selected character.</summary>
    public int SelectionStart => Math.Min(_selectionAnchor, _caretIndex);

    /// <summary>The number of selected characters.</summary>
    public int SelectionLength => Math.Abs(_selectionAnchor - _caretIndex);

    /// <summary>The caret index, which can be either end of the selection.</summary>
    public int CaretIndex => _caretIndex;

    /// <summary>The horizontal scroll offset applied to long single-line text.</summary>
    public float HorizontalOffset
    {
        get => _horizontalOffset;
        set => _horizontalOffset = XamlMath.Clamp(value, 0f, MaxHorizontalOffset);
    }

    /// <summary>The largest valid <see cref="HorizontalOffset"/>.</summary>
    public float MaxHorizontalOffset
    {
        get
        {
            if (AcceptsReturn)
            {
                return 0f;
            }

            var content = ContentRectangle();
            var textWidth = (float)Math.Ceiling(VisualText.MeasureWidth(DisplayText, Font, RenderDpi));
            return Math.Max(0f, textWidth - content.Width);
        }
    }

    /// <summary>The native editor used for IME and accessibility while the field has focus.</summary>
    /// <remarks>The editor is hosted through <see cref="NativeControlHost"/> and only parented while focused.</remarks>
    public System.Windows.Forms.TextBox NativeEditor => _editor;

    /// <summary>Whether the native editor island is currently shown over the content rectangle.</summary>
    public bool IsUsingNativeEditor => _nativeActive;

    private bool MultiLine => AcceptsReturn || Wrap;

    private string DisplayText => IsPassword ? new string('●', Text.Length) : Text;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var contentWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(Padding.Horizontal));
        var sample = string.IsNullOrEmpty(Text)
            ? (string.IsNullOrEmpty(Placeholder) ? "M" : Placeholder)
            : DisplayText;
        var wrap = Wrap && contentWidth > 0;
        var measured = VisualText.Measure(sample, Font, wrap ? contentWidth : 0f, wrap, RenderDpi);
        var width = Math.Max(120, (int)Math.Ceiling(measured.Width + Padding.Horizontal + 2));
        var height = Math.Max(
            Theme.Current.ControlHeight,
            (int)Math.Ceiling(measured.Height + Padding.Vertical + 2));
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        base.ArrangeOverride(finalSize);
        if (_nativeActive)
        {
            ArrangeNativeEditor();
        }
    }

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics) => OnRender(graphics);

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var focused = IsFocused || _nativeActive;
        var bounds = new Rectangle(0, 0, Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        var background = Background?.ApproximateColor ?? theme.ControlBackground;
        var border = focused ? theme.FocusRing : BorderBrush?.ApproximateColor ?? theme.ControlBorder;
        var thickness = BorderThickness.IsEmpty ? new XamlThickness(1f) : BorderThickness;
        XamlDrawing.PaintChrome(
            graphics,
            bounds,
            background,
            border,
            thickness,
            CornerRadius > 0 ? CornerRadius : theme.CornerRadius);

        var content = ContentRectangle();
        if (content.Width <= 0 || content.Height <= 0)
        {
            return;
        }

        // 原生岛是独立 HWND，压在视觉树之上；它显示时不要再画一份文字，否则会透出两层。
        if (_nativeActive)
        {
            return;
        }

        EnsureLines(content.Width);
        var foreground = Foreground.IsEmpty
            ? (IsEnabled ? theme.Foreground : theme.DisabledForeground)
            : Foreground;
        var offset = AcceptsReturn ? 0f : _horizontalOffset;

        var state = graphics.Save();
        try
        {
            graphics.SetClip(content, CombineMode.Intersect);

            if (IsFocused && SelectionLength > 0)
            {
                DrawSelection(graphics, content, offset);
            }

            if (string.IsNullOrEmpty(Text))
            {
                if (!string.IsNullOrEmpty(Placeholder))
                {
                    VisualText.Draw(this,
                        graphics,
                        Placeholder,
                        Font,
                        theme.MutedForeground,
                        new RectangleF(content.Left, content.Top, content.Width, _lineHeight),
                        wrap: false,
                        StringAlignment.Near,
                        StringAlignment.Near);
                }
            }
            else
            {
                DrawLines(graphics, content, offset, foreground);
            }

            if (IsFocused && _caretVisible && !IsReadOnly)
            {
                DrawCaret(graphics, content, offset, foreground);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, TextProperty))
        {
            ClampSelection();
            ResetCaretBlink();
            _layoutDirty = true;
            if (!_suppressHistoryReset && !_syncingNative)
            {
                _undo.Clear();
                _redo.Clear();
            }

            ClampHorizontalOffset();
            if (_nativeActive && !_syncingNative)
            {
                SyncEditorFromVisual();
            }

            return;
        }

        if (ReferenceEquals(e.Property, FontProperty)
            || ReferenceEquals(e.Property, FontSizeProperty)
            || ReferenceEquals(e.Property, FontFamilyProperty)
            || ReferenceEquals(e.Property, FontWeightProperty)
            || ReferenceEquals(e.Property, WrapProperty)
            || ReferenceEquals(e.Property, AcceptsReturnProperty)
            || ReferenceEquals(e.Property, IsPasswordProperty)
            || ReferenceEquals(e.Property, PlaceholderProperty))
        {
            _layoutDirty = true;
            ClampHorizontalOffset();
            ApplyNativeProperties();
            return;
        }

        if (ReferenceEquals(e.Property, ForegroundProperty)
            || ReferenceEquals(e.Property, IsReadOnlyProperty)
            || ReferenceEquals(e.Property, IsEnabledProperty))
        {
            ApplyNativeProperties();
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus()
    {
        _caretVisible = true;
        _caretTimer.Start();
        ShowNativeEditor();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostFocus()
    {
        _caretTimer.Stop();
        _caretVisible = false;
        _dragging = false;

        // 原生岛自己拿到焦点时 surface 会先收到 WM_KILLFOCUS；那不是「离开字段」，不能把岛撤掉。
        if (!_focusHandoff && !_editor.Focused && !_editor.ContainsFocus)
        {
            HideNativeEditor();
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnTextInput(VisualTextInputEventArgs args)
    {
        if (!IsEnabled || IsReadOnly || string.IsNullOrEmpty(args.Text))
        {
            return;
        }

        ReplaceSelection(args.Text);
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        if (args.Button != MouseButtons.Left)
        {
            return;
        }

        var index = HitTestIndex(args.GetPosition(this));
        if (args.ClickCount >= 2)
        {
            var range = WordRange(Text, index);
            _selectionAnchor = range.Start;
            _caretIndex = range.End;
            _dragging = false;
        }
        else
        {
            _selectionAnchor = index;
            _caretIndex = index;
            _dragging = true;
        }

        _caretVisible = true;
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        if (!_dragging)
        {
            return;
        }

        _caretIndex = HitTestIndex(args.GetPosition(this));
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        if (_dragging)
        {
            _dragging = false;
            args.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (!IsEnabled)
        {
            return;
        }

        var shift = (args.Modifiers & Keys.Shift) != 0;
        var control = (args.Modifiers & Keys.Control) != 0;
        switch (args.KeyCode)
        {
            case Keys.Left when control:
                MoveCaret(FindPreviousWord(Text, CaretIndex), shift);
                args.Handled = true;
                break;
            case Keys.Right when control:
                MoveCaret(FindNextWord(Text, CaretIndex), shift);
                args.Handled = true;
                break;
            case Keys.Left:
                MoveCaret(Math.Max(0, CaretIndex - 1), shift);
                args.Handled = true;
                break;
            case Keys.Right:
                MoveCaret(Math.Min(Text.Length, CaretIndex + 1), shift);
                args.Handled = true;
                break;
            case Keys.Home:
                MoveCaret(control ? 0 : CurrentLineStart(), shift);
                args.Handled = true;
                break;
            case Keys.End:
                MoveCaret(control ? Text.Length : CurrentLineEnd(), shift);
                args.Handled = true;
                break;
            case Keys.Back:
                if (!IsReadOnly)
                {
                    Backspace();
                    args.Handled = true;
                }

                break;
            case Keys.Delete:
                if (!IsReadOnly)
                {
                    Delete();
                    args.Handled = true;
                }

                break;
            case Keys.Z when control:
                if (shift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }

                args.Handled = true;
                break;
            case Keys.Y when control:
                Redo();
                args.Handled = true;
                break;
            case Keys.A when control:
                _selectionAnchor = 0;
                _caretIndex = Text.Length;
                ResetCaretBlink();
                EnsureCaretVisible();
                InvalidateVisual();
                args.Handled = true;
                break;
            case Keys.C when control:
                Copy();
                args.Handled = true;
                break;
            case Keys.X when control:
                if (!IsReadOnly)
                {
                    Copy();
                    ReplaceSelection(string.Empty);
                }

                args.Handled = true;
                break;
            case Keys.V when control:
                if (!IsReadOnly && TryGetClipboardText(out var pasted))
                {
                    ReplaceSelection(pasted);
                }

                args.Handled = true;
                break;
            case Keys.Enter:
                if (AcceptsReturn && !IsReadOnly)
                {
                    ReplaceSelection("\n");
                }

                args.Handled = true;
                break;
        }
    }

    private static int FindPreviousWord(string text, int index)
    {
        var cursor = XamlMath.Clamp(index, 0, text.Length);
        while (cursor > 0 && char.IsWhiteSpace(text[cursor - 1]))
        {
            cursor--;
        }

        while (cursor > 0 && !char.IsWhiteSpace(text[cursor - 1]))
        {
            cursor--;
        }

        return cursor;
    }

    private static int FindNextWord(string text, int index)
    {
        var cursor = XamlMath.Clamp(index, 0, text.Length);
        while (cursor < text.Length && !char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        return cursor;
    }

    private static (int Start, int End) WordRange(string text, int index)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var cursor = XamlMath.Clamp(index, 0, text.Length - 1);
        if (char.IsWhiteSpace(text[cursor]))
        {
            var start = cursor;
            while (start > 0 && char.IsWhiteSpace(text[start - 1]))
            {
                start--;
            }

            var end = cursor;
            while (end < text.Length && char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            return (start, end);
        }

        var wordStart = cursor;
        while (wordStart > 0 && !char.IsWhiteSpace(text[wordStart - 1]))
        {
            wordStart--;
        }

        var wordEnd = cursor;
        while (wordEnd < text.Length && !char.IsWhiteSpace(text[wordEnd]))
        {
            wordEnd++;
        }

        return (wordStart, wordEnd);
    }

    private Rectangle ContentRectangle()
    {
        var bounds = new Rectangle(
            0,
            0,
            Math.Max(1, (int)Math.Ceiling(Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        return new Rectangle(
            (int)Math.Ceiling(Padding.Left) + 1,
            (int)Math.Ceiling(Padding.Top) + 1,
            Math.Max(0, bounds.Width - (int)Math.Ceiling(Padding.Horizontal) - 2),
            Math.Max(0, bounds.Height - (int)Math.Ceiling(Padding.Vertical) - 2));
    }

    private void EnsureLines(float contentWidth)
    {
        if (!_layoutDirty && Math.Abs(contentWidth - _laidOutWidth) < 0.5f)
        {
            return;
        }

        _layoutDirty = false;
        _laidOutWidth = contentWidth;
        _lineHeight = Math.Max(1f, VisualText.Measure("Mg", Font, 0f, wrap: false, RenderDpi).Height);
        _lines.Clear();

        var display = DisplayText;
        var wrapWidth = Wrap && contentWidth > 0f ? contentWidth : 0f;
        var cursor = 0;
        while (true)
        {
            var start = cursor;
            while (cursor < display.Length && display[cursor] != '\n' && display[cursor] != '\r')
            {
                cursor++;
            }

            AppendWrappedLines(display, start, cursor - start, wrapWidth);
            if (cursor >= display.Length)
            {
                break;
            }

            // \r\n 是一个换行，索引上占两个字符；视觉行只记录可见片段。
            cursor += display[cursor] == '\r' && cursor + 1 < display.Length && display[cursor + 1] == '\n' ? 2 : 1;
        }

        if (_lines.Count == 0)
        {
            _lines.Add(new TextLine(0, 0));
        }
    }

    private void AppendWrappedLines(string display, int start, int length, float wrapWidth)
    {
        if (length <= 0)
        {
            _lines.Add(new TextLine(start, 0));
            return;
        }

        if (wrapWidth <= 0f)
        {
            _lines.Add(new TextLine(start, length));
            return;
        }

        var lineStart = start;
        var hardEnd = start + length;
        while (lineStart < hardEnd)
        {
            var cursor = lineStart;
            var lastFit = lineStart;
            while (cursor < hardEnd)
            {
                var wordEnd = cursor;
                var wordSpace = char.IsWhiteSpace(display[wordEnd]);
                do
                {
                    wordEnd++;
                }
                while (wordEnd < hardEnd && char.IsWhiteSpace(display[wordEnd]) == wordSpace);

                var width = VisualText.MeasureWidth(display.Substring(lineStart, wordEnd - lineStart), Font, RenderDpi);
                if (width <= wrapWidth)
                {
                    lastFit = wordEnd;
                    cursor = wordEnd;
                    continue;
                }

                if (lastFit == lineStart)
                {
                    // 第一个词就比整行宽：按字符切到放得下的位置，至少留下一个字符。
                    var cut = lineStart + 1;
                    while (cut < wordEnd
                        && VisualText.MeasureWidth(display.Substring(lineStart, cut + 1 - lineStart), Font, RenderDpi) <= wrapWidth)
                    {
                        cut++;
                    }

                    lastFit = cut;
                }

                break;
            }

            if (lastFit <= lineStart)
            {
                lastFit = lineStart + 1;
            }

            _lines.Add(new TextLine(lineStart, lastFit - lineStart));
            lineStart = lastFit;
        }
    }

    private int FindLineIndex(int index)
    {
        for (var line = 0; line < _lines.Count; line++)
        {
            var current = _lines[line];
            if (index >= current.Start && index <= current.Start + current.Length)
            {
                return line;
            }
        }

        return Math.Max(0, _lines.Count - 1);
    }

    private float MeasurePrefix(TextLine line, int length)
    {
        var clamped = XamlMath.Clamp(length, 0, line.Length);
        if (clamped <= 0)
        {
            return 0f;
        }

        return VisualText.MeasureWidth(DisplayText.Substring(line.Start, clamped), Font, RenderDpi);
    }

    private void DrawLines(Graphics graphics, Rectangle content, float offset, Color foreground)
    {
        var display = DisplayText;
        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            if (line.Length <= 0)
            {
                continue;
            }

            var text = display.Substring(line.Start, line.Length);
            var width = (float)Math.Ceiling(VisualText.MeasureWidth(text, Font, RenderDpi));
            var destination = new RectangleF(
                content.Left - offset,
                content.Top + (index * _lineHeight),
                Math.Max(content.Width, width + 2f),
                _lineHeight);
            VisualText.Draw(this,
                graphics,
                text,
                Font,
                foreground,
                destination,
                wrap: false,
                StringAlignment.Near,
                StringAlignment.Near);
        }
    }

    private void DrawSelection(Graphics graphics, Rectangle content, float offset)
    {
        var selectionStart = SelectionStart;
        var selectionEnd = selectionStart + SelectionLength;
        using var selection = new SolidBrush(Theme.Current.Selection);
        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            var from = Math.Max(selectionStart, line.Start);
            var to = Math.Min(selectionEnd, line.Start + line.Length);
            if (from >= to)
            {
                continue;
            }

            var left = content.Left - offset + MeasurePrefix(line, from - line.Start);
            var right = content.Left - offset + MeasurePrefix(line, to - line.Start);
            graphics.FillRectangle(
                selection,
                left,
                content.Top + (index * _lineHeight),
                Math.Max(1f, right - left),
                _lineHeight);
        }
    }

    private void DrawCaret(Graphics graphics, Rectangle content, float offset, Color foreground)
    {
        var lineIndex = FindLineIndex(CaretIndex);
        var line = _lines[lineIndex];
        var caretX = content.Left - offset + MeasurePrefix(line, CaretIndex - line.Start);
        var top = content.Top + (lineIndex * _lineHeight) + 2f;
        using var pen = new Pen(foreground, 1f);
        graphics.DrawLine(pen, caretX + 0.5f, top, caretX + 0.5f, top + Math.Max(1f, _lineHeight - 4f));
    }

    private int HitTestIndex(PointF local)
    {
        var content = ContentRectangle();
        EnsureLines(content.Width);
        if (_lines.Count == 0)
        {
            return 0;
        }

        var offset = AcceptsReturn ? 0f : _horizontalOffset;
        var lineIndex = (int)Math.Floor((local.Y - content.Top) / Math.Max(1f, _lineHeight));
        lineIndex = XamlMath.Clamp(lineIndex, 0, _lines.Count - 1);
        var line = _lines[lineIndex];
        var x = local.X - content.Left + offset;
        var best = line.Start;
        var bestDistance = Math.Abs(x);
        for (var index = 1; index <= line.Length; index++)
        {
            var distance = Math.Abs(x - MeasurePrefix(line, index));
            if (distance < bestDistance)
            {
                best = line.Start + index;
                bestDistance = distance;
            }
        }

        return best;
    }

    private int CurrentLineStart()
    {
        EnsureLines(ContentRectangle().Width);
        return _lines[FindLineIndex(CaretIndex)].Start;
    }

    private int CurrentLineEnd()
    {
        EnsureLines(ContentRectangle().Width);
        var line = _lines[FindLineIndex(CaretIndex)];
        return line.Start + line.Length;
    }

    private void MoveCaret(int index, bool extend)
    {
        _caretIndex = XamlMath.Clamp(index, 0, Text.Length);
        if (!extend)
        {
            _selectionAnchor = _caretIndex;
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void EnsureCaretVisible()
    {
        if (AcceptsReturn)
        {
            _horizontalOffset = 0f;
            return;
        }

        var content = ContentRectangle();
        if (content.Width <= 0)
        {
            return;
        }

        EnsureLines(content.Width);
        var line = _lines[FindLineIndex(CaretIndex)];
        var caretX = MeasurePrefix(line, CaretIndex - line.Start);
        if (caretX < _horizontalOffset)
        {
            _horizontalOffset = caretX;
        }
        else if (caretX > _horizontalOffset + content.Width)
        {
            _horizontalOffset = caretX - content.Width;
        }

        _horizontalOffset = XamlMath.Clamp(_horizontalOffset, 0f, MaxHorizontalOffset);
    }

    private void ClampHorizontalOffset()
    {
        _horizontalOffset = XamlMath.Clamp(_horizontalOffset, 0f, MaxHorizontalOffset);
    }

    private void PushUndo()
    {
        _undo.Push(new TextSnapshot(Text, _caretIndex, _selectionAnchor));
        _redo.Clear();
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(new TextSnapshot(Text, _caretIndex, _selectionAnchor));
        RestoreSnapshot(_undo.Pop());
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(new TextSnapshot(Text, _caretIndex, _selectionAnchor));
        RestoreSnapshot(_redo.Pop());
    }

    private void RestoreSnapshot(TextSnapshot snapshot)
    {
        SetTextInternal(snapshot.Text);
        _caretIndex = XamlMath.Clamp(snapshot.CaretIndex, 0, Text.Length);
        _selectionAnchor = XamlMath.Clamp(snapshot.Anchor, 0, Text.Length);
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateMeasure();
        InvalidateVisual();
        if (_nativeActive)
        {
            SyncEditorFromVisual();
        }
    }

    private void SetTextInternal(string value)
    {
        _suppressHistoryReset = true;
        try
        {
            Text = value;
        }
        finally
        {
            _suppressHistoryReset = false;
        }
    }

    private void ReplaceSelection(string replacement)
    {
        PushUndo();
        var start = SelectionStart;
        var length = SelectionLength;
        var before = Text.Substring(0, start);
        var after = Text.Substring(start + length);
        SetTextInternal(before + replacement + after);
        _caretIndex = before.Length + replacement.Length;
        _selectionAnchor = _caretIndex;
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateMeasure();
        InvalidateVisual();
        if (_nativeActive)
        {
            SyncEditorFromVisual();
        }
    }

    private void Backspace()
    {
        if (SelectionLength > 0)
        {
            ReplaceSelection(string.Empty);
            return;
        }

        if (_caretIndex == 0)
        {
            return;
        }

        PushUndo();
        SetTextInternal(Text.Remove(_caretIndex - 1, 1));
        _caretIndex--;
        _selectionAnchor = _caretIndex;
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateMeasure();
        InvalidateVisual();
        if (_nativeActive)
        {
            SyncEditorFromVisual();
        }
    }

    private void Delete()
    {
        if (SelectionLength > 0)
        {
            ReplaceSelection(string.Empty);
            return;
        }

        if (_caretIndex >= Text.Length)
        {
            return;
        }

        PushUndo();
        SetTextInternal(Text.Remove(_caretIndex, 1));
        _selectionAnchor = _caretIndex;
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateMeasure();
        InvalidateVisual();
        if (_nativeActive)
        {
            SyncEditorFromVisual();
        }
    }

    private void Copy()
    {
        if (SelectionLength > 0 && !IsPassword)
        {
            try
            {
                Clipboard.SetText(Text.Substring(SelectionStart, SelectionLength));
            }
            catch (ExternalException)
            {
                // The clipboard can be locked by another process; editing must keep working.
            }
        }
    }

    private static bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        try
        {
            if (!Clipboard.ContainsText())
            {
                return false;
            }

            text = Clipboard.GetText();
            return text.Length > 0;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private void ClampSelection()
    {
        _selectionAnchor = XamlMath.Clamp(_selectionAnchor, 0, Text.Length);
        _caretIndex = XamlMath.Clamp(_caretIndex, 0, Text.Length);
    }

    private void ResetCaretBlink()
    {
        if (!IsFocused)
        {
            return;
        }

        _caretVisible = true;
        _caretTimer.Stop();
        _caretTimer.Start();
    }

    private void ShowNativeEditor()
    {
        // 没有窗口句柄时原生岛无处安放，保持自绘路径即可；真实宿主在拿到句柄后才有 IME/无障碍。
        if (Surface is not { IsHandleCreated: true })
        {
            return;
        }

        _nativeActive = true;
        ApplyNativeProperties();
        SyncEditorFromVisual();
        _nativeHost.Visibility = VeloxDev.WinForms.Xaml.Controls.Visibility.Visible;
        ArrangeNativeEditor();

        _focusHandoff = true;
        try
        {
            if (_editor.CanFocus)
            {
                _editor.Focus();
            }
        }
        finally
        {
            _focusHandoff = false;
        }

        InvalidateVisual();
    }

    private void HideNativeEditor()
    {
        if (!_nativeActive)
        {
            return;
        }

        _nativeActive = false;
        _nativeHost.Visibility = VeloxDev.WinForms.Xaml.Controls.Visibility.Collapsed;
        Surface?.RemoveNative(_nativeHost);
        if (!_editor.IsDisposed)
        {
            _editor.Visible = false;
        }

        InvalidateVisual();
    }

    private void ArrangeNativeEditor()
    {
        var content = ContentRectangle();
        var size = new Size(Math.Max(0, content.Width), Math.Max(0, content.Height));
        _nativeHost.Measure(size);
        _nativeHost.Arrange(new RectangleF(content.Left, content.Top, size.Width, size.Height));
    }

    private void ApplyNativeProperties()
    {
        if (!_nativeActive || _editor.IsDisposed)
        {
            return;
        }

        var theme = Theme.Current;
        _editor.Multiline = MultiLine;
        _editor.WordWrap = Wrap;
        _editor.ReadOnly = IsReadOnly;
        _editor.UseSystemPasswordChar = IsPassword;
        _editor.Font = Font;
        _editor.BackColor = ResolveNativeBackground();
        _editor.ForeColor = Foreground.IsEmpty
            ? (IsEnabled ? theme.Foreground : theme.DisabledForeground)
            : Foreground;
#if NET5_0_OR_GREATER
        _editor.PlaceholderText = Placeholder;
#endif
    }

    // 原生 TextBox 不支持透明背景色；视觉层的 Transparent 要落到最近一层不透明祖先或主题底色上。
    private Color ResolveNativeBackground()
    {
        if (Background?.ApproximateColor is Color own && own.A > 0)
        {
            return Color.FromArgb(255, own.R, own.G, own.B);
        }

        for (var current = ParentElement; current is not null; current = current.ParentElement)
        {
            if (current.Background?.ApproximateColor is Color inherited && inherited.A > 0)
            {
                return Color.FromArgb(255, inherited.R, inherited.G, inherited.B);
            }
        }

        var fallback = Theme.Current.ControlBackground;
        return Color.FromArgb(255, fallback.R, fallback.G, fallback.B);
    }

    private void SyncEditorFromVisual()
    {
        if (!_nativeActive || _editor.IsDisposed)
        {
            return;
        }

        var text = Text.Replace("\r\n", "\n").Replace('\r', '\n');
        var editorText = text.Replace("\n", Environment.NewLine);
        _syncingNative = true;
        try
        {
            if (!string.Equals(_editor.Text, editorText, StringComparison.Ordinal))
            {
                _editor.Text = editorText;
            }

            var length = _editor.TextLength;
            _editor.SelectionStart = XamlMath.Clamp(SelectionStart, 0, length);
            _editor.SelectionLength = XamlMath.Clamp(SelectionLength, 0, length - _editor.SelectionStart);
        }
        finally
        {
            _syncingNative = false;
        }
    }

    private void SyncSelectionFromNative()
    {
        if (!_nativeActive || _syncingNative || _editor.IsDisposed)
        {
            return;
        }

        var start = XamlMath.Clamp(_editor.SelectionStart, 0, Text.Length);
        var length = XamlMath.Clamp(_editor.SelectionLength, 0, Text.Length - start);
        _selectionAnchor = start;
        _caretIndex = start + length;
        ResetCaretBlink();
        InvalidateVisual();
    }

    private void OnNativeTextChanged(object? sender, EventArgs e)
    {
        if (_syncingNative || _editor.IsDisposed)
        {
            return;
        }

        var text = _editor.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        var length = text.Length;
        _selectionAnchor = XamlMath.Clamp(_editor.SelectionStart, 0, length);
        _caretIndex = XamlMath.Clamp(_editor.SelectionStart + _editor.SelectionLength, 0, length);

        // 原生编辑器的撤销栈由它自己维护；自绘路径的快照到这里已经对不上，直接作废。
        _undo.Clear();
        _redo.Clear();
        _syncingNative = true;
        try
        {
            SetTextInternal(text);
        }
        finally
        {
            _syncingNative = false;
        }

        InvalidateVisual();
    }

    private void OnNativeEditorLostFocus(object? sender, EventArgs e)
    {
        SyncSelectionFromNative();
        if (_nativeActive && !IsFocused && !_focusHandoff)
        {
            HideNativeEditor();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _caretTimer.Stop();
            _caretTimer.Dispose();
            Surface?.RemoveNative(_nativeHost);
            _nativeHost.Child = null;
            _editor.Dispose();
        }

        base.Dispose(disposing);
    }

    private readonly struct TextLine(int start, int length)
    {
        public int Start { get; } = start;

        public int Length { get; } = length;
    }

    private readonly struct TextSnapshot(string text, int caretIndex, int anchor)
    {
        public string Text { get; } = text;

        public int CaretIndex { get; } = caretIndex;

        public int Anchor { get; } = anchor;
    }
}
