using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A self-drawn push button with hover, press and keyboard states.</summary>
public class Button : VisualElement
{
    private string _text = string.Empty;
    private Font _font = DefaultFont;
    private Color _foreground = Color.Empty;
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
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value)
            {
                return;
            }

            _text = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The font used to draw the text.</summary>
    public Font Font
    {
        get => _font;
        set
        {
            if (value is null || ReferenceEquals(_font, value))
            {
                return;
            }

            _font = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The text colour; empty uses the active theme.</summary>
    public Color Foreground
    {
        get => _foreground;
        set
        {
            _foreground = value;
            InvalidateVisual();
        }
    }

    /// <summary>Raised when the button is clicked by the pointer or the keyboard.</summary>
    public event EventHandler? Click;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var sample = string.IsNullOrEmpty(_text) ? "M" : _text;
        var measured = VisualText.Measure(sample, _font, 0f, wrap: false, RenderDpi);
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

        if (IsFocused)
        {
            using var focusPath = XamlDrawing.RoundedRectangle(
                new RectangleF(1.5f, 1.5f, Math.Max(0f, bounds.Width - 3f), Math.Max(0f, bounds.Height - 3f)),
                Math.Max(0f, (CornerRadius > 0 ? CornerRadius : theme.CornerRadius) - 1f));
            using var focusPen = new Pen(theme.FocusRing, 1.6f);
            graphics.DrawPath(focusPen, focusPath);
        }

        if (string.IsNullOrEmpty(_text))
        {
            return;
        }

        var content = new Rectangle(
            (int)Math.Ceiling(Padding.Left) + 1,
            (int)Math.Ceiling(Padding.Top) + 1,
            Math.Max(0, bounds.Width - (int)Math.Ceiling(Padding.Horizontal) - 2),
            Math.Max(0, bounds.Height - (int)Math.Ceiling(Padding.Vertical) - 2));
        var foreground = _foreground.IsEmpty
            ? (IsEnabled ? theme.Foreground : theme.DisabledForeground)
            : _foreground;
        VisualText.Draw(
            graphics,
            _text,
            _font,
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

    /// <summary>Raises <see cref="Click"/>.</summary>
    protected virtual void OnClick()
    {
        if (IsEnabled)
        {
            Click?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>A single-line self-drawn text field with caret, selection and clipboard support.</summary>
/// <remarks>
/// The field paints its own text and caret. It receives characters through <see cref="VisualSurface.ProcessTextInput"/>
/// and handles navigation, selection and editing keys in <see cref="OnKeyDown"/>. IME composition and accessibility
/// are not part of this first version.
/// </remarks>
public class TextBox : VisualElement
{
    private readonly System.Windows.Forms.Timer _caretTimer;
    private string _text = string.Empty;
    private string _placeholder = string.Empty;
    private Font _font = DefaultFont;
    private Color _foreground = Color.Empty;
    private bool _isReadOnly;
    private bool _isPassword;
    private int _selectionAnchor;
    private int _caretIndex;
    private bool _dragging;
    private bool _caretVisible;

    /// <summary>Creates a focusable text field with theme metrics.</summary>
    public TextBox()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Cursor = Cursors.IBeam;
        Padding = new XamlThickness(8f, 6f);
        MinHeight = Theme.Current.ControlHeight;
        CornerRadius = Theme.Current.CornerRadius;
        _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
        _caretTimer.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
    }

    /// <summary>The current text.</summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value)
            {
                return;
            }

            _text = value;
            ClampSelection();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The text shown while the field is empty.</summary>
    public string Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value ?? string.Empty;
            InvalidateMeasure();
        }
    }

    /// <summary>The font used to draw the text.</summary>
    public Font Font
    {
        get => _font;
        set
        {
            if (value is null || ReferenceEquals(_font, value))
            {
                return;
            }

            _font = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The text colour; empty uses the active theme.</summary>
    public Color Foreground
    {
        get => _foreground;
        set
        {
            _foreground = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether the text can be edited.</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            _isReadOnly = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether the text is drawn as bullets.</summary>
    public bool IsPassword
    {
        get => _isPassword;
        set
        {
            _isPassword = value;
            InvalidateMeasure();
        }
    }

    /// <summary>The first selected character.</summary>
    public int SelectionStart => Math.Min(_selectionAnchor, _caretIndex);

    /// <summary>The number of selected characters.</summary>
    public int SelectionLength => Math.Abs(_selectionAnchor - _caretIndex);

    /// <summary>The caret index, which can be either end of the selection.</summary>
    public int CaretIndex => _caretIndex;

    private string DisplayText => _isPassword ? new string('●', _text.Length) : _text;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var sample = string.IsNullOrEmpty(_text)
            ? (string.IsNullOrEmpty(_placeholder) ? "M" : _placeholder)
            : DisplayText;
        var measured = VisualText.Measure(sample, _font, 0f, wrap: false, RenderDpi);
        var width = Math.Max(120, (int)Math.Ceiling(measured.Width + Padding.Horizontal + 2));
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
        var bounds = new Rectangle(0, 0, Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        var background = Background?.ApproximateColor ?? theme.ControlBackground;
        var border = IsFocused ? theme.FocusRing : BorderBrush?.ApproximateColor ?? theme.ControlBorder;
        var thickness = BorderThickness.IsEmpty ? new XamlThickness(1f) : BorderThickness;
        XamlDrawing.PaintChrome(
            graphics,
            bounds,
            background,
            border,
            thickness,
            CornerRadius > 0 ? CornerRadius : theme.CornerRadius);

        var content = new Rectangle(
            (int)Math.Ceiling(Padding.Left) + 1,
            (int)Math.Ceiling(Padding.Top) + 1,
            Math.Max(0, bounds.Width - (int)Math.Ceiling(Padding.Horizontal) - 2),
            Math.Max(0, bounds.Height - (int)Math.Ceiling(Padding.Vertical) - 2));
        if (content.Width <= 0 || content.Height <= 0)
        {
            return;
        }

        var state = graphics.Save();
        try
        {
            graphics.SetClip(content, CombineMode.Intersect);
            var foreground = _foreground.IsEmpty
                ? (IsEnabled ? theme.Foreground : theme.DisabledForeground)
                : _foreground;

            if (IsFocused && SelectionLength > 0)
            {
                var start = MeasurePrefix(SelectionStart);
                var end = MeasurePrefix(SelectionStart + SelectionLength);
                using var selection = new SolidBrush(theme.Selection);
                graphics.FillRectangle(selection, content.Left + start, content.Top, Math.Max(1, end - start), content.Height);
            }

            var text = string.IsNullOrEmpty(_text) ? _placeholder : DisplayText;
            var textColor = string.IsNullOrEmpty(_text) ? theme.MutedForeground : foreground;
            if (!string.IsNullOrEmpty(text))
            {
                VisualText.Draw(
                    graphics,
                    text,
                    _font,
                    textColor,
                    content,
                    wrap: false,
                    StringAlignment.Near,
                    StringAlignment.Center);
            }

            if (IsFocused && _caretVisible && !_isReadOnly)
            {
                var caretX = content.Left + MeasurePrefix(CaretIndex);
                using var pen = new Pen(foreground, 1f);
                graphics.DrawLine(pen, caretX + 0.5f, content.Top + 3f, caretX + 0.5f, content.Bottom - 3f);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus()
    {
        _caretVisible = true;
        _caretTimer.Start();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostFocus()
    {
        _caretTimer.Stop();
        _caretVisible = false;
        _dragging = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnTextInput(VisualTextInputEventArgs args)
    {
        if (!IsEnabled || _isReadOnly || string.IsNullOrEmpty(args.Text))
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
        _selectionAnchor = index;
        _caretIndex = index;
        _dragging = true;
        _caretVisible = true;
        ResetCaretBlink();
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
            case Keys.Left:
                MoveCaret(Math.Max(0, CaretIndex - 1), shift);
                args.Handled = true;
                break;
            case Keys.Right:
                MoveCaret(Math.Min(_text.Length, CaretIndex + 1), shift);
                args.Handled = true;
                break;
            case Keys.Home:
                MoveCaret(0, shift);
                args.Handled = true;
                break;
            case Keys.End:
                MoveCaret(_text.Length, shift);
                args.Handled = true;
                break;
            case Keys.Back:
                if (!_isReadOnly)
                {
                    Backspace();
                    args.Handled = true;
                }

                break;
            case Keys.Delete:
                if (!_isReadOnly)
                {
                    Delete();
                    args.Handled = true;
                }

                break;
            case Keys.A when control:
                _selectionAnchor = 0;
                _caretIndex = _text.Length;
                ResetCaretBlink();
                InvalidateVisual();
                args.Handled = true;
                break;
            case Keys.C when control:
                Copy();
                args.Handled = true;
                break;
            case Keys.X when control:
                if (!_isReadOnly)
                {
                    Copy();
                    ReplaceSelection(string.Empty);
                }

                args.Handled = true;
                break;
            case Keys.V when control:
                if (!_isReadOnly && TryGetClipboardText(out var pasted))
                {
                    ReplaceSelection(pasted);
                }

                args.Handled = true;
                break;
            case Keys.Enter:
                args.Handled = true;
                break;
        }
    }

    private int MeasurePrefix(int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        var display = DisplayText;
        length = Math.Min(length, display.Length);
        return (int)Math.Ceiling(VisualText.MeasureWidth(display.Substring(0, length), _font, RenderDpi));
    }

    private int HitTestIndex(PointF local)
    {
        var x = local.X - Padding.Left - 1;
        var display = DisplayText;
        var best = 0;
        var bestDistance = Math.Abs(x);
        for (var index = 1; index <= display.Length; index++)
        {
            var distance = Math.Abs(x - MeasurePrefix(index));
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void MoveCaret(int index, bool extend)
    {
        _caretIndex = XamlMath.Clamp(index, 0, _text.Length);
        if (!extend)
        {
            _selectionAnchor = _caretIndex;
        }

        ResetCaretBlink();
        InvalidateVisual();
    }

    private void ReplaceSelection(string replacement)
    {
        var start = SelectionStart;
        var length = SelectionLength;
        var before = _text.Substring(0, start);
        var after = _text.Substring(start + length);
        _text = before + replacement + after;
        _caretIndex = before.Length + replacement.Length;
        _selectionAnchor = _caretIndex;
        ResetCaretBlink();
        InvalidateMeasure();
        InvalidateVisual();
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

        _text = _text.Remove(_caretIndex - 1, 1);
        _caretIndex--;
        _selectionAnchor = _caretIndex;
        ResetCaretBlink();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Delete()
    {
        if (SelectionLength > 0)
        {
            ReplaceSelection(string.Empty);
            return;
        }

        if (_caretIndex >= _text.Length)
        {
            return;
        }

        _text = _text.Remove(_caretIndex, 1);
        _selectionAnchor = _caretIndex;
        ResetCaretBlink();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Copy()
    {
        if (SelectionLength > 0 && !_isPassword)
        {
            try
            {
                Clipboard.SetText(_text.Substring(SelectionStart, SelectionLength));
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
        _selectionAnchor = XamlMath.Clamp(_selectionAnchor, 0, _text.Length);
        _caretIndex = XamlMath.Clamp(_caretIndex, 0, _text.Length);
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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _caretTimer.Stop();
            _caretTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
