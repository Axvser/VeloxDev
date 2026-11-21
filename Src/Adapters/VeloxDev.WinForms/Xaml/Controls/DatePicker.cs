using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NativeTextBox = System.Windows.Forms.TextBox;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// An editable date field with a drop-down <see cref="Calendar"/>.
/// </summary>
/// <remarks>
/// <para>
/// The field shows <see cref="Text"/>; editing it parses with the current culture and writes <see cref="SelectedDate"/>.
/// Assigning <see cref="SelectedDate"/> formats <see cref="Text"/> with <see cref="SelectedDateFormat"/> and updates the
/// drop-down calendar. <see cref="IsDropDownOpen"/> opens the popup, which closes itself when it loses focus.
/// </para>
/// <para>The popup is the existing <see cref="Popup"/> control hosting a borderless top-level form.</para>
/// </remarks>
public class DatePicker : Control
{
    /// <summary>The text shown in the field.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(DatePicker),
        new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    /// <summary>The selected date, or null when the text is empty or unparseable.</summary>
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate),
        typeof(DateTime?),
        typeof(DatePicker),
        new PropertyMetadata(null, OnSelectedDatePropertyChanged));

    /// <summary>Whether the drop-down calendar is open.</summary>
    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen),
        typeof(bool),
        typeof(DatePicker),
        new PropertyMetadata(false, OnIsDropDownOpenPropertyChanged));

    /// <summary>The format used to render <see cref="SelectedDate"/> into <see cref="Text"/>; the short date by default.</summary>
    public static readonly DependencyProperty SelectedDateFormatProperty = DependencyProperty.Register(
        nameof(SelectedDateFormat),
        typeof(string),
        typeof(DatePicker),
        new PropertyMetadata("d", OnDateFormatPropertyChanged));

    /// <summary>The border stroke colour; empty uses the active theme border.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(DatePicker),
        new PropertyMetadata(Color.Empty, OnChromePropertyChanged));

    /// <summary>The border stroke width per side, in pixels.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(DatePicker),
        new PropertyMetadata(new Thickness(1f), OnChromePropertyChanged));

    /// <summary>The corner radius, in pixels; six by default.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(float),
        typeof(DatePicker),
        new PropertyMetadata(6f, OnChromePropertyChanged));

    private const int ButtonWidth = 28;

    private readonly NativeTextBox _editor;
    private readonly Popup _popup;
    private readonly Calendar _calendar;
    private bool _syncingEditor;
    private bool _syncingDate;
    private bool _syncingDropDown;
    private bool _syncingCalendar;
    private bool _parsing;

    /// <summary>Creates the field, its native editor and its drop-down calendar.</summary>
    public DatePicker()
    {
        TabStop = false;

        _editor = new NativeTextBox
        {
            BorderStyle = BorderStyle.None,
            AutoSize = false,
            TabStop = true,
        };

        _editor.TextChanged += OnEditorTextChanged;
        _editor.KeyDown += (_, e) => OnKeyDown(e);
        _editor.KeyUp += (_, e) => OnKeyUp(e);
        _editor.MouseDown += (_, e) => OnMouseDown(e);
        _editor.MouseUp += (_, e) => OnMouseUp(e);
        _editor.MouseMove += (_, e) => OnMouseMove(e);
        _editor.MouseEnter += (_, e) => OnMouseEnter(e);
        _editor.MouseLeave += (_, e) => OnMouseLeave(e);
        _editor.MouseWheel += (_, e) => OnMouseWheel(e);
        _editor.GotFocus += (_, e) => OnGotFocus(e);
        _editor.LostFocus += (_, e) => OnLostFocus(e);

        _calendar = new Calendar();
        _calendar.SelectedDateChanged += OnCalendarSelectedDateChanged;

        _popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = this,
            Child = _calendar,
        };
        _popup.DependencyPropertyChanged += OnPopupPropertyChanged;

        Controls.Add(_editor);
        ApplyEditorProperties();
    }

    /// <summary>Gets or sets the text shown in the field.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the selected date.</summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>Gets or sets whether the drop-down calendar is open.</summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>Gets or sets the format used to render <see cref="SelectedDate"/> into <see cref="Text"/>.</summary>
    public string SelectedDateFormat
    {
        get => (string?)GetValue(SelectedDateFormatProperty) ?? "d";
        set => SetValue(SelectedDateFormatProperty, value);
    }

    /// <summary>Gets or sets the border stroke colour.</summary>
    public Color BorderBrush
    {
        get => (Color)GetValue(BorderBrushProperty)!;
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Gets or sets the border stroke width per side in pixels.</summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>Gets or sets the corner radius in pixels.</summary>
    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Moves focus to the native editor.</summary>
    /// <returns><see langword="true"/> when the editor received focus.</returns>
    public new bool Focus() => Focusable && _editor.Focus();

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = _editor.GetPreferredSize(new Size(proposedSize.Width, proposedSize.Height));
        var border = BorderThickness;
        return new Size(
            160 + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            Math.Max(Theme.Current.ControlHeight, preferred.Height) + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
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

        var editorBounds = new Rectangle(
            inset.Left + 6,
            inset.Top + 2,
            Math.Max(0, inset.Width - ButtonWidth - 8),
            Math.Max(0, inset.Height - 4));
        if (_editor.Bounds != editorBounds)
        {
            _editor.Bounds = editorBounds;
        }
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!IsPainted)
        {
            return;
        }

        XamlDrawing.PaintChrome(
            e.Graphics,
            ClientRectangle,
            EffectiveBackground,
            EffectiveBorder,
            BorderThickness,
            CornerRadius);

        var centerX = (int)Math.Round(Width - Math.Ceiling(BorderThickness.Right) - ButtonWidth / 2f);
        var centerY = Height / 2;
        using var brush = new SolidBrush(EffectiveForeground);
        e.Graphics.FillPolygon(
            brush,
            new[]
            {
                new Point(centerX - 4, centerY - 2),
                new Point(centerX + 4, centerY - 2),
                new Point(centerX, centerY + 3),
            });
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && IsOverButton(e.Location))
        {
            IsDropDownOpen = !IsDropDownOpen;
        }
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);
        ApplyEditorProperties();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _popup.DependencyPropertyChanged -= OnPopupPropertyChanged;
            _calendar.SelectedDateChanged -= OnCalendarSelectedDateChanged;
            _editor.TextChanged -= OnEditorTextChanged;
            if (Controls.Contains(_editor))
            {
                Controls.Remove(_editor);
            }

            _editor.Dispose();
            _popup.Dispose();
        }

        base.Dispose(disposing);
    }

    private Color EffectiveBackground => HasXamlValue(BackColorProperty) && Background is { A: > 0 } brush
        ? brush.ApproximateColor
        : Theme.Current.ControlBackground;

    private Color EffectiveForeground => HasXamlValue(ForeColorProperty) ? ForeColor : Theme.Current.Foreground;

    private Color EffectiveBorder => HasXamlValue(BorderBrushProperty) ? BorderBrush : Theme.Current.ControlBorder;

    private bool IsOverButton(Point point)
        => point.X >= Width - (int)Math.Ceiling(BorderThickness.Right) - ButtonWidth;

    private void ApplyEditorProperties()
    {
        if (_syncingEditor || IsDisposed || _editor.IsDisposed)
        {
            return;
        }

        _syncingEditor = true;
        try
        {
            if (_editor.Text != Text)
            {
                _editor.Text = Text;
            }

            _editor.BackColor = XamlDrawing.ToOpaque(EffectiveBackground, SystemColors.Window);
            _editor.ForeColor = EffectiveForeground;
            _editor.Font = Font;
            _editor.Enabled = IsEnabled;
        }
        finally
        {
            _syncingEditor = false;
        }

        Invalidate();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncingEditor || IsDisposed)
        {
            return;
        }

        _syncingEditor = true;
        try
        {
            SetValue(TextProperty, _editor.Text);
        }
        finally
        {
            _syncingEditor = false;
        }

        ParseText();
    }

    private void ParseText()
    {
        if (_syncingDate || IsDisposed)
        {
            return;
        }

        DateTime? parsed = null;
        if (!string.IsNullOrWhiteSpace(Text)
            && DateTime.TryParse(Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date))
        {
            parsed = date.Date;
        }

        _parsing = true;
        try
        {
            _syncingDate = true;
            try
            {
                SetValue(SelectedDateProperty, parsed);
            }
            finally
            {
                _syncingDate = false;
            }
        }
        finally
        {
            _parsing = false;
        }
    }

    private void SetSelectedDate(DateTime? date, bool updateText)
    {
        _syncingDate = true;
        try
        {
            SetValue(SelectedDateProperty, date);
            if (updateText)
            {
                var text = date is { } value
                    ? value.ToString(SelectedDateFormat, CultureInfo.CurrentCulture)
                    : string.Empty;
                SetValue(TextProperty, text);
            }
        }
        finally
        {
            _syncingDate = false;
        }
    }

    private void OpenDropDown()
    {
        _syncingDropDown = true;
        _syncingCalendar = true;
        try
        {
            _calendar.DisplayDate = SelectedDate ?? DateTime.Today;
            _calendar.SelectedDate = SelectedDate;
            _popup.IsOpen = true;
        }
        finally
        {
            _syncingCalendar = false;
            _syncingDropDown = false;
        }
    }

    private void CloseDropDown()
    {
        _syncingDropDown = true;
        try
        {
            _popup.IsOpen = false;
        }
        finally
        {
            _syncingDropDown = false;
        }
    }

    private void OnCalendarSelectedDateChanged(object? sender, EventArgs e)
    {
        if (_syncingDate || _syncingCalendar || IsDisposed)
        {
            return;
        }

        SetSelectedDate(_calendar.SelectedDate, updateText: true);

        if (_popup.IsOpen)
        {
            _syncingDropDown = true;
            try
            {
                _popup.IsOpen = false;
                SetValue(IsDropDownOpenProperty, false);
            }
            finally
            {
                _syncingDropDown = false;
            }
        }
    }

    private void OnPopupPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Property, Popup.IsOpenProperty) || _syncingDropDown || IsDisposed)
        {
            return;
        }

        _syncingDropDown = true;
        try
        {
            SetValue(IsDropDownOpenProperty, e.NewValue is true);
        }
        finally
        {
            _syncingDropDown = false;
        }
    }

    private static void OnTextPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not DatePicker picker || picker.IsDisposed)
        {
            return;
        }

        picker.ApplyEditorProperties();
        if (!picker._syncingDate)
        {
            picker.ParseText();
        }
    }

    private static void OnSelectedDatePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is DatePicker picker && !picker.IsDisposed && !picker._syncingDate && !picker._parsing)
        {
            picker.SetSelectedDate(picker.SelectedDate, updateText: true);
        }
    }

    private static void OnIsDropDownOpenPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not DatePicker picker || picker.IsDisposed)
        {
            return;
        }

        if (picker._syncingDropDown)
        {
            return;
        }

        if (e.NewValue is true)
        {
            picker.OpenDropDown();
        }
        else
        {
            picker.CloseDropDown();
        }
    }

    private static void OnDateFormatPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is DatePicker picker && !picker.IsDisposed && !picker._syncingDate && !picker._parsing)
        {
            picker.SetSelectedDate(picker.SelectedDate, updateText: true);
        }
    }

    private static void OnChromePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is DatePicker picker && !picker.IsDisposed)
        {
            picker.ApplyEditorProperties();
        }
    }
}
