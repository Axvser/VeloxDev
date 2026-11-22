using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using NativeMonthCalendar = System.Windows.Forms.MonthCalendar;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A month calendar backed by a native <see cref="System.Windows.Forms.MonthCalendar"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SelectedDate"/> is mirrored into the native selection; when it is null the calendar displays
/// <see cref="DisplayDate"/>. A user selection writes both properties back.
/// </para>
/// <para>
/// The native control draws its own caption and navigation; only the palette around and inside it can be themed.
/// The wrapper paints a rounded border and background behind the native surface.
/// </para>
/// </remarks>
public class Calendar : Control
{
    /// <summary>The selected date, or null when the calendar has no selection.</summary>
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate),
        typeof(DateTime?),
        typeof(Calendar),
        new PropertyMetadata(null, OnSelectedDatePropertyChanged));

    /// <summary>The date shown when <see cref="SelectedDate"/> is null.</summary>
    public static readonly DependencyProperty DisplayDateProperty = DependencyProperty.Register(
        nameof(DisplayDate),
        typeof(DateTime),
        typeof(Calendar),
        new PropertyMetadata(DateTime.Today, OnDisplayDatePropertyChanged));

    /// <summary>Whether the native control shows the today marker.</summary>
    public static readonly DependencyProperty ShowTodayProperty = DependencyProperty.Register(
        nameof(ShowToday),
        typeof(bool),
        typeof(Calendar),
        new PropertyMetadata(true, OnNativeOptionPropertyChanged));

    /// <summary>Whether the native control shows week numbers.</summary>
    public static readonly DependencyProperty ShowWeekNumbersProperty = DependencyProperty.Register(
        nameof(ShowWeekNumbers),
        typeof(bool),
        typeof(Calendar),
        new PropertyMetadata(false, OnNativeOptionPropertyChanged));

    /// <summary>The border stroke colour; empty uses the active theme border.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Color),
        typeof(Calendar),
        new PropertyMetadata(Color.Empty, OnChromePropertyChanged));

    /// <summary>The border stroke width per side, in pixels.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
        nameof(BorderThickness),
        typeof(Thickness),
        typeof(Calendar),
        new PropertyMetadata(new Thickness(1f), OnChromePropertyChanged));

    /// <summary>The corner radius, in pixels; six by default.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(float),
        typeof(Calendar),
        new PropertyMetadata(6f, OnChromePropertyChanged));

    private readonly NativeMonthCalendar _calendar;
    private bool _syncing;

    /// <summary>Creates the wrapper and its native calendar.</summary>
    public Calendar()
    {
        TabStop = false;

        _calendar = new NativeMonthCalendar
        {
            TabStop = true,
            ShowToday = true,
        };

        _calendar.DateSelected += OnDateSelected;
        _calendar.KeyDown += (_, e) => OnKeyDown(e);
        _calendar.KeyUp += (_, e) => OnKeyUp(e);
        _calendar.MouseDown += (_, e) => OnMouseDown(e);
        _calendar.MouseUp += (_, e) => OnMouseUp(e);
        _calendar.MouseMove += (_, e) => OnMouseMove(e);
        _calendar.MouseEnter += (_, e) => OnMouseEnter(e);
        _calendar.MouseLeave += (_, e) => OnMouseLeave(e);
        _calendar.MouseWheel += (_, e) => OnMouseWheel(e);
        _calendar.GotFocus += (_, e) => OnGotFocus(e);
        _calendar.LostFocus += (_, e) => OnLostFocus(e);

        Controls.Add(_calendar);
        ApplyCalendarProperties();
    }

    /// <summary>Gets or sets the selected date.</summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>Gets or sets the displayed date when <see cref="SelectedDate"/> is null.</summary>
    public DateTime DisplayDate
    {
        get => (DateTime)GetValue(DisplayDateProperty)!;
        set => SetValue(DisplayDateProperty, value);
    }

    /// <summary>Gets or sets whether the today marker is shown.</summary>
    public bool ShowToday
    {
        get => (bool)GetValue(ShowTodayProperty)!;
        set => SetValue(ShowTodayProperty, value);
    }

    /// <summary>Gets or sets whether week numbers are shown.</summary>
    public bool ShowWeekNumbers
    {
        get => (bool)GetValue(ShowWeekNumbersProperty)!;
        set => SetValue(ShowWeekNumbersProperty, value);
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

    /// <summary>Raised after <see cref="SelectedDate"/> changes, whether from code or from the user.</summary>
    public event EventHandler? SelectedDateChanged;

    /// <summary>Moves focus to the native calendar.</summary>
    /// <returns><see langword="true"/> when the calendar received focus.</returns>
    public new bool Focus() => Focusable && _calendar.Focus();

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = _calendar.GetPreferredSize(new Size(proposedSize.Width, proposedSize.Height));
        var border = BorderThickness;
        return new Size(
            preferred.Width + (int)Math.Ceiling(Padding.Horizontal) + (int)Math.Ceiling(border.Horizontal),
            preferred.Height + (int)Math.Ceiling(Padding.Vertical) + (int)Math.Ceiling(border.Vertical));
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
        if (_calendar.Bounds != inset)
        {
            _calendar.Bounds = inset;
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
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);
        ApplyCalendarProperties();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _calendar.DateSelected -= OnDateSelected;
            if (Controls.Contains(_calendar))
            {
                Controls.Remove(_calendar);
            }

            _calendar.Dispose();
        }

        base.Dispose(disposing);
    }

    private new Color EffectiveBackground => HasXamlValue(BackgroundProperty) && Background is { A: > 0 } brush
        ? brush.ApproximateColor
        : Theme.Current.Surface;

    private new Color EffectiveForeground => HasXamlValue(ForegroundProperty) ? base.EffectiveForeground : Theme.Current.Foreground;

    private Color EffectiveBorder => HasXamlValue(BorderBrushProperty) ? BorderBrush : Theme.Current.ControlBorder;

    private void ApplyCalendarProperties()
    {
        if (_syncing || IsDisposed || _calendar.IsDisposed)
        {
            return;
        }

        _syncing = true;
        try
        {
            _calendar.ShowToday = ShowToday;
            _calendar.ShowWeekNumbers = ShowWeekNumbers;

            if (SelectedDate is { } selected)
            {
                var date = selected.Date;
                if (_calendar.SelectionStart.Date != date)
                {
                    _calendar.SetDate(date);
                }
            }
            else
            {
                var display = DisplayDate.Date;
                if (_calendar.SelectionStart.Date != display)
                {
                    _calendar.SetDate(display);
                }
            }
        }
        finally
        {
            _syncing = false;
        }

        ApplyColors();
    }

    private void ApplyColors()
    {
        var theme = Theme.Current;
        var background = XamlDrawing.ToOpaque(EffectiveBackground, SystemColors.Window);

        // MonthCalendar 拒绝与 BackColor 相同的标题底色，先换一档。
        var title = theme.SurfaceAlt == background ? theme.ControlHover : theme.SurfaceAlt;
        _calendar.BackColor = background;
        _calendar.ForeColor = EffectiveForeground;
        _calendar.TitleBackColor = title;
        _calendar.TitleForeColor = theme.Foreground;
        _calendar.TrailingForeColor = theme.MutedForeground;
        _calendar.Font = Font;
        _calendar.Enabled = IsEnabled;
        Invalidate();
    }

    private void OnDateSelected(object? sender, DateRangeEventArgs e)
    {
        if (_syncing || IsDisposed)
        {
            return;
        }

        _syncing = true;
        try
        {
            SetValue(SelectedDateProperty, e.Start.Date);
            SetValue(DisplayDateProperty, e.Start.Date);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void OnSelectedDatePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Calendar calendar || calendar.IsDisposed)
        {
            return;
        }

        if (!calendar._syncing)
        {
            calendar.ApplyCalendarProperties();
        }

        calendar.SelectedDateChanged?.Invoke(calendar, EventArgs.Empty);
    }

    private static void OnDisplayDatePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Calendar calendar && !calendar.IsDisposed && calendar.SelectedDate is null)
        {
            calendar.ApplyCalendarProperties();
        }
    }

    private static void OnNativeOptionPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Calendar calendar && !calendar.IsDisposed)
        {
            calendar.ApplyCalendarProperties();
        }
    }

    private static void OnChromePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Calendar calendar && !calendar.IsDisposed)
        {
            calendar.ApplyCalendarProperties();
        }
    }
}
