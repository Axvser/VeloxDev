using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>The character style of one run in a <see cref="RichTextBox"/>.</summary>
public sealed class RichTextStyle
{
    /// <summary>Whether the run is bold.</summary>
    public bool Bold { get; set; }

    /// <summary>Whether the run is italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Whether the run is underlined.</summary>
    public bool Underline { get; set; }

    /// <summary>The run colour; <see cref="Color.Empty"/> uses the control's theme foreground.</summary>
    public Color Foreground { get; set; } = Color.Empty;

    /// <summary>The font size in points; 0 uses the control's base font size.</summary>
    public float FontSize { get; set; }

    /// <summary>Returns a copy of this style.</summary>
    public RichTextStyle Clone() => new()
    {
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Foreground = Foreground,
        FontSize = FontSize,
    };
}

/// <summary>A self-drawn rich text editor with styled runs, selection, clipboard and undo.</summary>
/// <remarks>
/// <para>
/// The document is a flat character buffer; a <c>\n</c> character ends a paragraph. Each character carries a
/// <see cref="RichTextStyle"/>, so formatting is per-run without a separate document object model. Text is measured
/// and painted with GDI through the control's own font cache.
/// </para>
/// <para>
/// Editing supports caret movement, Shift selection, word movement, Home/End, Enter, Backspace/Delete, Ctrl+A/C/X/V,
/// Ctrl+B/I/U and Ctrl+Z/Y. The control measures its full height; put it inside a <see cref="ScrollViewer"/> for a
/// fixed viewport.
/// </para>
/// </remarks>
public class RichTextBox : VisualElement
{
    private const float CaretWidth = 1.5f;
    private const int UndoLimit = 100;

    /// <summary>The base font; runs without a size use it.</summary>
    public static readonly DependencyProperty FontProperty = DependencyProperty.Register(
        nameof(Font), typeof(Font), typeof(RichTextBox),
        new PropertyMetadata(DefaultFont) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The colour used by runs without their own colour.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Color), typeof(RichTextBox),
        new PropertyMetadata(Color.Empty) { AffectsRender = true });

    /// <summary>Whether the editor rejects edits but still supports selection and copy.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(RichTextBox),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Whether long lines wrap at the content width.</summary>
    public static readonly DependencyProperty WrapProperty = DependencyProperty.Register(
        nameof(Wrap), typeof(bool), typeof(RichTextBox),
        new PropertyMetadata(true) { AffectsMeasure = true, AffectsRender = true });

    private readonly List<RichChar> _chars = [];
    private readonly List<VisualLine> _lines = [];
    private readonly List<List<RichChar>> _undo = [];
    private readonly List<List<RichChar>> _redo = [];
    private readonly Dictionary<(bool Bold, bool Italic, bool Underline, float Size), Font> _fonts = [];
    private readonly System.Windows.Forms.Timer _caretTimer;
    private RichTextStyle _typingStyle = new();
    private int _caret;
    private int _selectionStart;
    private int _selectionEnd;
    private int _selectionAnchor;
    private bool _caretVisible = true;
    private bool _layoutValid;
    private float _layoutWidth = -1f;
    private bool _dragging;

    /// <summary>Creates an empty, focusable editor.</summary>
    public RichTextBox()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Padding = new XamlThickness(10f, 8f);
        MinHeight = Theme.Current.ControlHeight;
        CornerRadius = Theme.Current.CornerRadius;
        Cursor = Cursors.IBeam;

        _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
        _caretTimer.Tick += (_, _) =>
        {
            if (!IsFocused)
            {
                return;
            }

            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
        _caretTimer.Start();
    }

    /// <summary>Raised after the document text or formatting changed.</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>The base font; runs without a size use it.</summary>
    public Font Font
    {
        get => (Font)GetValue(FontProperty)!;
        set
        {
            if (value is not null)
            {
                SetValue(FontProperty, value);
            }
        }
    }

    /// <summary>The colour used by runs without their own colour.</summary>
    public Color Foreground
    {
        get => (Color)GetValue(ForegroundProperty)!;
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Whether the editor rejects edits but still supports selection and copy.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Whether long lines wrap at the content width.</summary>
    public bool Wrap
    {
        get => (bool)GetValue(WrapProperty)!;
        set => SetValue(WrapProperty, value);
    }

    /// <summary>The caret index in the flat character buffer.</summary>
    public int CaretIndex => _caret;

    /// <summary>The start of the current selection.</summary>
    public int SelectionStart => Math.Min(_selectionStart, _selectionEnd);

    /// <summary>The length of the current selection.</summary>
    public int SelectionLength => Math.Abs(_selectionEnd - _selectionStart);

    /// <summary>The document text; paragraphs are separated by <c>\n</c>.</summary>
    public string Text
    {
        get => new(_chars.Select(static item => item.Value).ToArray());
        set
        {
            _chars.Clear();
            foreach (var character in value ?? string.Empty)
            {
                _chars.Add(new RichChar(character == '\r' ? '\n' : character, new RichTextStyle()));
            }

            _caret = 0;
            _selectionStart = _selectionEnd = _selectionAnchor = 0;
            _undo.Clear();
            _redo.Clear();
            InvalidateLayout();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether every selected character (or the typing style) is bold.</summary>
    public bool IsBold => _typingStyle.Bold;

    /// <summary>Whether every selected character (or the typing style) is italic.</summary>
    public bool IsItalic => _typingStyle.Italic;

    /// <summary>Whether every selected character (or the typing style) is underlined.</summary>
    public bool IsUnderlined => _typingStyle.Underline;

    /// <summary>Returns a copy of the style at <paramref name="index"/>, or <see langword="null"/> when out of range.</summary>
    /// <param name="index">The flat character index.</param>
    public RichTextStyle? GetStyleAt(int index)
        => index >= 0 && index < _chars.Count ? _chars[index].Style.Clone() : null;

    /// <summary>Replaces the document with <paramref name="text"/> in one plain style.</summary>
    /// <param name="text">The new text; <c>\n</c> separates paragraphs.</param>
    public void SetPlainText(string? text)
    {
        _chars.Clear();
        var style = new RichTextStyle();
        foreach (var character in text ?? string.Empty)
        {
            _chars.Add(new RichChar(character == '\r' ? '\n' : character, style.Clone()));
        }

        _caret = _selectionStart = _selectionEnd = _selectionAnchor = 0;
        _undo.Clear();
        _redo.Clear();
        InvalidateLayout();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects a character range.</summary>
    /// <param name="start">The range start.</param>
    /// <param name="length">The range length.</param>
    public void Select(int start, int length)
    {
        _selectionStart = XamlMath.Clamp(start, 0, _chars.Count);
        _selectionEnd = XamlMath.Clamp(start + length, 0, _chars.Count);
        _selectionAnchor = _selectionStart;
        _caret = _selectionEnd;
        InvalidateVisual();
    }

    /// <summary>Toggles bold on the selection or the typing style.</summary>
    public void ToggleBold() => ApplyStyle(static style => style.Bold = !style.Bold);

    /// <summary>Toggles italic on the selection or the typing style.</summary>
    public void ToggleItalic() => ApplyStyle(static style => style.Italic = !style.Italic);

    /// <summary>Toggles underline on the selection or the typing style.</summary>
    public void ToggleUnderline() => ApplyStyle(static style => style.Underline = !style.Underline);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var contentWidth = Math.Max(0, availableSize.Width - (int)Math.Ceiling(padding.Horizontal));
        EnsureLayout(contentWidth);
        var height = _lines.Count == 0 ? 0f : _lines[_lines.Count - 1].Top + _lines[_lines.Count - 1].Height;
        var width = Wrap && contentWidth > 0
            ? contentWidth
            : _lines.Count == 0 ? 0f : _lines.Max(static line => line.Width);
        return new Size(
            (int)Math.Ceiling(width + padding.Horizontal),
            (int)Math.Ceiling(height + padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var bounds = new Rectangle(0, 0, Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        var radius = CornerRadius > 0 ? CornerRadius : theme.CornerRadius;
        XamlDrawing.PaintChrome(
            graphics,
            bounds,
            Background?.ApproximateColor ?? theme.ControlBackground,
            BorderBrush?.ApproximateColor ?? theme.ControlBorder,
            BorderThickness.IsEmpty ? new XamlThickness(1f) : BorderThickness,
            radius);

        if (IsFocusVisible)
        {
            using var focusPath = XamlDrawing.RoundedRectangle(
                new RectangleF(1.5f, 1.5f, Math.Max(0f, bounds.Width - 3f), Math.Max(0f, bounds.Height - 3f)),
                Math.Max(0f, radius - 1f));
            using var focusPen = new Pen(theme.FocusRing, 1.6f);
            graphics.DrawPath(focusPen, focusPath);
        }

        var content = ContentRectangle();
        EnsureLayout(content.Width);
        if (_lines.Count == 0)
        {
            return;
        }

        var selectionStart = SelectionStart;
        var selectionEnd = selectionStart + SelectionLength;
        using (var selection = new SolidBrush(theme.Selection))
        {
            foreach (var line in _lines)
            {
                foreach (var cell in line.Cells)
                {
                    if (cell.Index >= selectionStart && cell.Index < selectionEnd)
                    {
                        graphics.FillRectangle(
                            selection,
                            content.Left + cell.X,
                            content.Top + line.Top,
                            Math.Max(1f, cell.Width),
                            line.Height);
                    }
                }

                if (line.EndIndex >= selectionStart && line.EndIndex < selectionEnd)
                {
                    graphics.FillRectangle(selection, content.Left + line.Width, content.Top + line.Top, 5f, line.Height);
                }
            }
        }

        foreach (var line in _lines)
        {
            foreach (var cell in line.Cells)
            {
                var font = FontFor(cell.Style);
                var color = cell.Style.Foreground.IsEmpty
                    ? Foreground.IsEmpty ? theme.Foreground : Foreground
                    : cell.Style.Foreground;
                VisualText.Draw(this,
                    graphics,
                    cell.Value.ToString(),
                    font,
                    color,
                    new RectangleF(
                        content.Left + cell.X,
                        content.Top + line.Top,
                        Math.Max(1f, cell.Width + 2f),
                        line.Height),
                    wrap: false,
                    StringAlignment.Near,
                    StringAlignment.Center);
            }
        }

        if (IsFocused && _caretVisible)
        {
            var (caretX, caretY, caretHeight) = CaretPoint(content);
            using var caret = new Pen(theme.FocusRing, CaretWidth);
            graphics.DrawLine(caret, caretX, caretY, caretX, caretY + caretHeight);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (args.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        var index = HitTestText(args.GetPosition(this));
        if (args.ClickCount >= 2)
        {
            SelectWord(index);
        }
        else
        {
            _caret = index;
            _selectionStart = _selectionEnd = _selectionAnchor = index;
            _dragging = true;
        }

        _caretVisible = true;
        InvalidateVisual();
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        if (!_dragging)
        {
            return;
        }

        var index = HitTestText(args.GetPosition(this));
        _selectionStart = Math.Min(_selectionAnchor, index);
        _selectionEnd = Math.Max(_selectionAnchor, index);
        _caret = index;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        _dragging = false;
    }

    /// <inheritdoc />
    protected override void OnTextInput(VisualTextInputEventArgs args)
    {
        base.OnTextInput(args);
        if (!IsReadOnly && !string.IsNullOrEmpty(args.Text))
        {
            Insert(args.Text);
            args.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        if (args.KeyCode == Keys.Tab)
        {
            return;
        }

        var control = (args.Modifiers & Keys.Control) != 0;
        var shift = (args.Modifiers & Keys.Shift) != 0;
        switch (args.KeyCode)
        {
            case Keys.Left:
                MoveCaret(control ? PreviousWord(_caret) : Math.Max(0, _caret - 1), shift);
                args.Handled = true;
                break;
            case Keys.Right:
                MoveCaret(control ? NextWord(_caret) : Math.Min(_chars.Count, _caret + 1), shift);
                args.Handled = true;
                break;
            case Keys.Up:
                MoveCaret(VerticalTarget(ContentRectangle().Width, -1), shift);
                args.Handled = true;
                break;
            case Keys.Down:
                MoveCaret(VerticalTarget(ContentRectangle().Width, 1), shift);
                args.Handled = true;
                break;
            case Keys.Home:
                MoveCaret(control ? 0 : LineStart(_caret), shift);
                args.Handled = true;
                break;
            case Keys.End:
                MoveCaret(control ? _chars.Count : LineEnd(_caret), shift);
                args.Handled = true;
                break;
            case Keys.Enter when !IsReadOnly:
                Insert("\n");
                args.Handled = true;
                break;
            case Keys.Back when !IsReadOnly:
                Backspace();
                args.Handled = true;
                break;
            case Keys.Delete when !IsReadOnly:
                DeleteForward();
                args.Handled = true;
                break;
            case Keys.A when control:
                Select(0, _chars.Count);
                args.Handled = true;
                break;
            case Keys.C when control:
                CopySelection();
                args.Handled = true;
                break;
            case Keys.X when control && !IsReadOnly:
                CopySelection();
                DeleteSelection();
                args.Handled = true;
                break;
            case Keys.V when control && !IsReadOnly:
                Paste();
                args.Handled = true;
                break;
            case Keys.B when control:
                ToggleBold();
                args.Handled = true;
                break;
            case Keys.I when control:
                ToggleItalic();
                args.Handled = true;
                break;
            case Keys.U when control:
                ToggleUnderline();
                args.Handled = true;
                break;
            case Keys.Z when control:
                Undo();
                args.Handled = true;
                break;
            case Keys.Y when control:
                Redo();
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnGotFocus()
    {
        _caretVisible = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLostFocus() => InvalidateVisual();

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, FontProperty)
            || ReferenceEquals(e.Property, WrapProperty)
            || ReferenceEquals(e.Property, PaddingProperty))
        {
            InvalidateLayout();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _caretTimer.Stop();
            _caretTimer.Dispose();
            foreach (var font in _fonts.Values)
            {
                font.Dispose();
            }

            _fonts.Clear();
        }

        base.Dispose(disposing);
    }

    private void ApplyStyle(Action<RichTextStyle> change)
    {
        if (SelectionLength == 0)
        {
            change(_typingStyle);
            InvalidateVisual();
            return;
        }

        if (IsReadOnly)
        {
            return;
        }

        SaveUndo();
        for (var index = SelectionStart; index < SelectionStart + SelectionLength; index++)
        {
            if (index >= _chars.Count)
            {
                break;
            }

            var style = _chars[index].Style.Clone();
            change(style);
            _chars[index] = new RichChar(_chars[index].Value, style);
        }

        if (_caret > 0 && _caret <= _chars.Count)
        {
            var style = _chars[_caret - 1].Style.Clone();
            change(style);
            _typingStyle = style;
        }

        InvalidateLayout();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Insert(string text)
    {
        if (IsReadOnly)
        {
            return;
        }

        SaveUndo();
        DeleteSelectionCore();
        var style = _typingStyle.Clone();
        foreach (var character in text.Replace("\r", string.Empty))
        {
            _chars.Insert(_caret, new RichChar(character, style.Clone()));
            _caret++;
        }

        _selectionStart = _selectionEnd = _selectionAnchor = _caret;
        InvalidateLayout();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Backspace()
    {
        if (SelectionLength > 0)
        {
            SaveUndo();
            DeleteSelectionCore();
            InvalidateLayout();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_caret == 0)
        {
            return;
        }

        SaveUndo();
        _chars.RemoveAt(_caret - 1);
        _caret--;
        _selectionStart = _selectionEnd = _selectionAnchor = _caret;
        InvalidateLayout();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteForward()
    {
        if (SelectionLength > 0)
        {
            SaveUndo();
            DeleteSelectionCore();
            InvalidateLayout();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_caret >= _chars.Count)
        {
            return;
        }

        SaveUndo();
        _chars.RemoveAt(_caret);
        InvalidateLayout();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteSelection()
    {
        if (SelectionLength == 0)
        {
            return;
        }

        SaveUndo();
        DeleteSelectionCore();
        InvalidateLayout();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteSelectionCore()
    {
        var start = SelectionStart;
        var length = SelectionLength;
        if (length == 0)
        {
            return;
        }

        _chars.RemoveRange(start, length);
        _caret = start;
        _selectionStart = _selectionEnd = _selectionAnchor = start;
    }

    private void CopySelection()
    {
        if (SelectionLength == 0)
        {
            return;
        }

        var text = new string(_chars.Skip(SelectionStart).Take(SelectionLength).Select(static item => item.Value).ToArray());
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    private void Paste()
    {
        if (!Clipboard.ContainsText())
        {
            return;
        }

        Insert(Clipboard.GetText().Replace("\r\n", "\n"));
    }

    private void MoveCaret(int index, bool extend)
    {
        index = XamlMath.Clamp(index, 0, _chars.Count);
        if (extend)
        {
            _selectionStart = Math.Min(_selectionAnchor, index);
            _selectionEnd = Math.Max(_selectionAnchor, index);
        }
        else
        {
            _selectionAnchor = index;
            _selectionStart = _selectionEnd = index;
        }

        _caret = index;
        AdoptTypingStyle();
        _caretVisible = true;
        InvalidateVisual();
    }

    private void AdoptTypingStyle()
    {
        var reference = _caret > 0 && _caret - 1 < _chars.Count ? _caret - 1 : 0;
        if (reference < _chars.Count)
        {
            _typingStyle = _chars[reference].Style.Clone();
        }
    }

    private void SaveUndo()
    {
        _undo.Add(Snapshot());
        if (_undo.Count > UndoLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(Snapshot());
        Restore(_undo[_undo.Count - 1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(Snapshot());
        Restore(_redo[_redo.Count - 1]);
        _redo.RemoveAt(_redo.Count - 1);
    }

    private List<RichChar> Snapshot()
        => _chars.Select(static item => new RichChar(item.Value, item.Style.Clone())).ToList();

    private void Restore(List<RichChar> snapshot)
    {
        _chars.Clear();
        _chars.AddRange(snapshot.Select(static item => new RichChar(item.Value, item.Style.Clone())));
        _caret = XamlMath.Clamp(_caret, 0, _chars.Count);
        _selectionStart = _selectionEnd = _selectionAnchor = _caret;
        InvalidateLayout();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private int PreviousWord(int index)
    {
        var current = XamlMath.Clamp(index, 0, _chars.Count);
        while (current > 0 && char.IsWhiteSpace(_chars[current - 1].Value))
        {
            current--;
        }

        while (current > 0 && !char.IsWhiteSpace(_chars[current - 1].Value))
        {
            current--;
        }

        return current;
    }

    private int NextWord(int index)
    {
        var current = XamlMath.Clamp(index, 0, _chars.Count);
        while (current < _chars.Count && !char.IsWhiteSpace(_chars[current].Value))
        {
            current++;
        }

        while (current < _chars.Count && char.IsWhiteSpace(_chars[current].Value))
        {
            current++;
        }

        return current;
    }

    private int LineStart(int index)
    {
        var current = XamlMath.Clamp(index, 0, _chars.Count);
        while (current > 0 && _chars[current - 1].Value != '\n')
        {
            current--;
        }

        return current;
    }

    private int LineEnd(int index)
    {
        var current = XamlMath.Clamp(index, 0, _chars.Count);
        while (current < _chars.Count && _chars[current].Value != '\n')
        {
            current++;
        }

        return current;
    }

    private void SelectWord(int index)
    {
        var start = XamlMath.Clamp(index, 0, _chars.Count);
        while (start > 0 && !char.IsWhiteSpace(_chars[start - 1].Value) && _chars[start - 1].Value != '\n')
        {
            start--;
        }

        var end = XamlMath.Clamp(index, 0, _chars.Count);
        while (end < _chars.Count && !char.IsWhiteSpace(_chars[end].Value) && _chars[end].Value != '\n')
        {
            end++;
        }

        _selectionStart = start;
        _selectionEnd = end;
        _selectionAnchor = start;
        _caret = end;
        InvalidateVisual();
    }

    private int VerticalTarget(float contentWidth, int direction)
    {
        EnsureLayout(contentWidth);
        if (_lines.Count == 0)
        {
            return _caret;
        }

        var lineIndex = _lines.FindIndex(line => _caret >= line.StartIndex && _caret <= line.EndIndex);
        if (lineIndex < 0)
        {
            lineIndex = 0;
        }

        var x = CaretPoint(ContentRectangle()).X - ContentRectangle().Left;
        var targetLine = XamlMath.Clamp(lineIndex + direction, 0, _lines.Count - 1);
        var line = _lines[targetLine];
        foreach (var cell in line.Cells)
        {
            if (cell.X + (cell.Width / 2f) >= x)
            {
                return cell.Index;
            }
        }

        return line.EndIndex;
    }

    private (float X, float Y, float Height) CaretPoint(RectangleF content)
    {
        if (_lines.Count == 0)
        {
            return (content.Left + 1f, content.Top + 1f, Math.Max(12f, Font.Height));
        }

        var line = _lines.Find(line => _caret >= line.StartIndex && _caret <= line.EndIndex) ?? _lines[_lines.Count - 1];
        var x = content.Left;
        foreach (var cell in line.Cells)
        {
            if (cell.Index == _caret)
            {
                x = content.Left + cell.X;
                break;
            }

            x = content.Left + cell.X + cell.Width;
        }

        return (x, content.Top + line.Top, Math.Max(12f, line.Height));
    }

    private int HitTestText(PointF point)
    {
        var content = ContentRectangle();
        EnsureLayout(content.Width);
        var localY = point.Y - content.Top;
        var line = _lines.FirstOrDefault(candidate => localY >= candidate.Top && localY < candidate.Top + candidate.Height)
                   ?? _lines[_lines.Count - 1];
        var localX = point.X - content.Left;
        foreach (var cell in line.Cells)
        {
            if (localX < cell.X + (cell.Width / 2f))
            {
                return cell.Index;
            }
        }

        return line.EndIndex;
    }

    private RectangleF ContentRectangle()
    {
        var padding = Padding;
        return new RectangleF(
            (int)Math.Ceiling(padding.Left),
            (int)Math.Ceiling(padding.Top),
            Math.Max(0f, Bounds.Width - padding.Horizontal),
            Math.Max(0f, Bounds.Height - padding.Vertical));
    }

    private void InvalidateLayout()
    {
        _layoutValid = false;
        InvalidateMeasure();
    }

    private void EnsureLayout(float contentWidth)
    {
        if (_layoutValid && Math.Abs(_layoutWidth - contentWidth) < 0.5f)
        {
            return;
        }

        _lines.Clear();
        var wrapWidth = Wrap && contentWidth > 0f ? contentWidth : float.PositiveInfinity;
        var line = new VisualLine(0);
        var x = 0f;
        var lineHeight = LineHeight();

        for (var index = 0; index < _chars.Count; index++)
        {
            var character = _chars[index];
            if (character.Value == '\n')
            {
                line.EndIndex = index;
                line.Width = x;
                line.Height = Math.Max(line.Height, lineHeight);
                line.Top = _lines.Count == 0 ? 0f : _lines[_lines.Count - 1].Top + _lines[_lines.Count - 1].Height;
                _lines.Add(line);
                line = new VisualLine(index + 1);
                x = 0f;
                continue;
            }

            var font = FontFor(character.Style);
            var width = VisualText.MeasureWidth(character.Value.ToString(), font, RenderDpi);
            if (wrapWidth < float.PositiveInfinity && x + width > wrapWidth && line.Cells.Count > 0)
            {
                line.EndIndex = index;
                line.Width = x;
                line.Height = Math.Max(line.Height, lineHeight);
                line.Top = _lines.Count == 0 ? 0f : _lines[_lines.Count - 1].Top + _lines[_lines.Count - 1].Height;
                _lines.Add(line);
                line = new VisualLine(index);
                x = 0f;
            }

            line.Cells.Add(new VisualCell(index, character.Value, x, width, font.Height, character.Style));
            line.Height = Math.Max(line.Height, font.Height);
            x += width;
        }

        line.EndIndex = _chars.Count;
        line.Width = x;
        line.Height = Math.Max(line.Height, lineHeight);
        line.Top = _lines.Count == 0 ? 0f : _lines[_lines.Count - 1].Top + _lines[_lines.Count - 1].Height;
        _lines.Add(line);

        _layoutWidth = contentWidth;
        _layoutValid = true;
    }

    private float LineHeight() => Math.Max(Font.Height, 14f);

    private Font FontFor(RichTextStyle style)
    {
        var size = style.FontSize > 0f ? style.FontSize : Font.Size;
        var key = (style.Bold, style.Italic, style.Underline, size);
        if (_fonts.TryGetValue(key, out var font))
        {
            return font;
        }

        var fontStyle = FontStyle.Regular;
        if (style.Bold) fontStyle |= FontStyle.Bold;
        if (style.Italic) fontStyle |= FontStyle.Italic;
        if (style.Underline) fontStyle |= FontStyle.Underline;
        font = new Font(Font.FontFamily, size, fontStyle, GraphicsUnit.Point);
        _fonts[key] = font;
        return font;
    }

    private readonly record struct RichChar(char Value, RichTextStyle Style);

    private sealed class VisualLine(int startIndex)
    {
        public int StartIndex { get; } = startIndex;
        public int EndIndex { get; set; }
        public float Top { get; set; }
        public float Height { get; set; }
        public float Width { get; set; }
        public List<VisualCell> Cells { get; } = [];
    }

    private readonly record struct VisualCell(int Index, char Value, float X, float Width, float Height, RichTextStyle Style);
}
