using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlDrawing = VeloxDev.WinForms.Xaml.Controls.XamlDrawing;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace VeloxDev.WinForms.Xaml.Visuals.Controls;

/// <summary>A month grid for picking a date.</summary>
/// <remarks>
/// The calendar draws a header with month navigation, a weekday row and up to six week rows. Pointer and keyboard
/// move the selection; <see cref="SelectedDate"/> can be <see langword="null"/> until the user picks a day.
/// </remarks>
public class Calendar : VisualElement
{
    /// <summary>The selected day, or <see langword="null"/>.</summary>
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate), typeof(DateTime?), typeof(Calendar),
        new PropertyMetadata(null) { AffectsRender = true });

    /// <summary>The first month shown in the grid.</summary>
    public static readonly DependencyProperty DisplayMonthProperty = DependencyProperty.Register(
        nameof(DisplayMonth), typeof(DateTime), typeof(Calendar),
        new PropertyMetadata(new DateTime(2000, 1, 1)) { AffectsRender = true });

    /// <summary>The first day of the week column.</summary>
    public static readonly DependencyProperty FirstDayOfWeekProperty = DependencyProperty.Register(
        nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(Calendar),
        new PropertyMetadata(DayOfWeek.Monday) { AffectsRender = true });

    private const float HeaderHeight = 34f;
    private const float WeekHeaderHeight = 22f;
    private const float CellHeight = 28f;
    private const float CellWidth = 34f;

    private readonly List<(RectangleF Bounds, DateTime Date)> _cells = [];
    private RectangleF _previous;
    private RectangleF _next;

    /// <summary>Creates a focusable calendar showing the current month.</summary>
    public Calendar()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Padding = new XamlThickness(6f);
        Background = Theme.Current.Surface;
        BorderBrush = Theme.Current.ControlBorder;
        BorderThickness = new XamlThickness(1f);
        CornerRadius = Theme.Current.CornerRadius;
        DisplayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    }

    /// <summary>Raised after <see cref="SelectedDate"/> changes.</summary>
    public event EventHandler? SelectedDateChanged;

    /// <summary>The selected day, or <see langword="null"/>.</summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>The month shown in the grid; only the year and month are used.</summary>
    public DateTime DisplayMonth
    {
        get => (DateTime)GetValue(DisplayMonthProperty)!;
        set => SetValue(DisplayMonthProperty, new DateTime(value.Year, value.Month, 1));
    }

    /// <summary>The first day of the week column.</summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)GetValue(FirstDayOfWeekProperty)!;
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => new(
            (int)Math.Ceiling((CellWidth * 7f) + Padding.Horizontal),
            (int)Math.Ceiling(HeaderHeight + WeekHeaderHeight + (CellHeight * 6f) + Padding.Vertical));

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var left = Padding.Left;
        var top = Padding.Top;
        var gridWidth = CellWidth * 7f;

        _previous = new RectangleF(left + 4f, top + 5f, 24f, 24f);
        _next = new RectangleF(left + gridWidth - 28f, top + 5f, 24f, 24f);
        using (var chevron = new SolidBrush(theme.MutedForeground))
        {
            graphics.FillPolygon(chevron, new[]
            {
                new PointF(_previous.Right - 8f, _previous.Top + 6f),
                new PointF(_previous.Left + 6f, _previous.Top + 12f),
                new PointF(_previous.Right - 8f, _previous.Bottom - 6f),
            });
            graphics.FillPolygon(chevron, new[]
            {
                new PointF(_next.Left + 8f, _next.Top + 6f),
                new PointF(_next.Right - 6f, _next.Top + 12f),
                new PointF(_next.Left + 8f, _next.Bottom - 6f),
            });
        }

        var monthTitle = DisplayMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        VisualText.Draw(this,
            graphics,
            monthTitle,
            DefaultFont,
            theme.Foreground,
            new Rectangle((int)left, (int)top, (int)gridWidth, (int)HeaderHeight),
            wrap: false,
            StringAlignment.Center,
            StringAlignment.Center);

        var weekTop = top + HeaderHeight;
        var firstWeekDay = (int)FirstDayOfWeek;
        for (var column = 0; column < 7; column++)
        {
            var day = (DayOfWeek)((firstWeekDay + column) % 7);
            var label = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day);
            label = label.Length > 2 ? label.Substring(0, 2) : label;
            VisualText.Draw(this,
                graphics,
                label,
                DefaultFont,
                theme.MutedForeground,
                new Rectangle(
                    (int)(left + (column * CellWidth)),
                    (int)weekTop,
                    (int)CellWidth,
                    (int)WeekHeaderHeight),
                wrap: false,
                StringAlignment.Center,
                StringAlignment.Center);
        }

        _cells.Clear();
        var firstOfMonth = DisplayMonth;
        var offset = ((int)firstOfMonth.DayOfWeek - firstWeekDay + 7) % 7;
        var days = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
        var gridTop = weekTop + WeekHeaderHeight;
        for (var index = 0; index < 42; index++)
        {
            var cell = new RectangleF(
                left + (((index % 7) * CellWidth)),
                gridTop + ((index / 7) * CellHeight),
                CellWidth,
                CellHeight);
            var dayNumber = index - offset + 1;
            if (dayNumber < 1 || dayNumber > days)
            {
                continue;
            }

            var date = new DateTime(firstOfMonth.Year, firstOfMonth.Month, dayNumber);
            _cells.Add((cell, date));
            var selected = SelectedDate is { } selectedDate && selectedDate.Date == date;
            if (selected)
            {
                using var fill = new SolidBrush(theme.Accent);
                using var path = XamlDrawing.RoundedRectangle(
                    new RectangleF(cell.X + 3f, cell.Y + 2f, cell.Width - 6f, cell.Height - 4f),
                    4f);
                graphics.FillPath(fill, path);
            }
            else if (date == DateTime.Today)
            {
                using var outline = new Pen(theme.FocusRing, 1f);
                graphics.DrawRectangle(
                    outline,
                    cell.X + 3.5f,
                    cell.Y + 2.5f,
                    cell.Width - 7f,
                    cell.Height - 5f);
            }

            VisualText.Draw(this,
                graphics,
                dayNumber.ToString(CultureInfo.CurrentCulture),
                DefaultFont,
                selected ? theme.AccentForeground : theme.Foreground,
                new Rectangle((int)cell.X, (int)cell.Y, (int)cell.Width, (int)cell.Height),
                wrap: false,
                StringAlignment.Center,
                StringAlignment.Center);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        var point = args.GetPosition(this);
        if (_previous.Contains(point))
        {
            DisplayMonth = DisplayMonth.AddMonths(-1);
            args.Handled = true;
            return;
        }

        if (_next.Contains(point))
        {
            DisplayMonth = DisplayMonth.AddMonths(1);
            args.Handled = true;
            return;
        }

        foreach (var (bounds, date) in _cells)
        {
            if (bounds.Contains(point))
            {
                Focus();
                SelectedDate = date;
                args.Handled = true;
                return;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        var current = SelectedDate ?? DateTime.Today;
        DateTime next;
        switch (args.KeyCode)
        {
            case Keys.Left:
                next = current.AddDays(-1);
                break;
            case Keys.Right:
                next = current.AddDays(1);
                break;
            case Keys.Up:
                next = current.AddDays(-7);
                break;
            case Keys.Down:
                next = current.AddDays(7);
                break;
            case Keys.PageUp:
                next = current.AddMonths(-1);
                break;
            case Keys.PageDown:
                next = current.AddMonths(1);
                break;
            default:
                return;
        }

        DisplayMonth = new DateTime(next.Year, next.Month, 1);
        SelectedDate = next;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, SelectedDateProperty))
        {
            if (e.NewValue is DateTime selected)
            {
                DisplayMonth = new DateTime(selected.Year, selected.Month, 1);
            }

            SelectedDateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>A date field whose calendar opens in a <see cref="VisualPopup"/>.</summary>
public class DatePicker : VisualElement
{
    /// <summary>The selected day, or <see langword="null"/>.</summary>
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate), typeof(DateTime?), typeof(DatePicker),
        new PropertyMetadata(null) { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The display format passed to <see cref="DateTime.ToString(string, IFormatProvider)"/>.</summary>
    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(DatePicker),
        new PropertyMetadata("yyyy-MM-dd") { AffectsMeasure = true, AffectsRender = true });

    /// <summary>The text shown while nothing is selected.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(DatePicker),
        new PropertyMetadata(string.Empty) { AffectsMeasure = true, AffectsRender = true });

    private readonly Calendar _calendar = new();
    private readonly VisualPopup _popup = new();
    private bool _hover;
    private bool _opening;

    /// <summary>Creates a focusable date field with theme metrics.</summary>
    public DatePicker()
    {
        Focusable = true;
        IsHitTestVisible = true;
        Cursor = Cursors.Hand;
        Padding = new XamlThickness(10f, 6f);
        MinHeight = Theme.Current.ControlHeight;
        CornerRadius = Theme.Current.CornerRadius;

        _calendar.Width = 250f;
        _calendar.Height = 226f;
        _popup.Child = _calendar;
        _popup.PlacementTarget = this;
        _popup.Placement = VisualPlacement.Bottom;
        _popup.StaysOpen = false;
        _popup.Closed += OnPopupClosed;
        _calendar.SelectedDateChanged += OnCalendarSelected;
    }

    /// <summary>Raised after <see cref="SelectedDate"/> changes.</summary>
    public event EventHandler? SelectedDateChanged;

    /// <summary>The selected day, or <see langword="null"/>.</summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>The display format passed to <see cref="DateTime.ToString(string, IFormatProvider)"/>.</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty)!;
        set => SetValue(FormatProperty, value ?? string.Empty);
    }

    /// <summary>The text shown while nothing is selected.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty)!;
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    /// <summary>Whether the calendar is open.</summary>
    public bool IsDropDownOpen
    {
        get => _popup.IsOpen;
        set
        {
            if (value)
            {
                _opening = true;
                try
                {
                    _calendar.SelectedDate = SelectedDate ?? DateTime.Today;
                }
                finally
                {
                    _opening = false;
                }

                _popup.IsOpen = true;
                _calendar.Focus();
            }
            else
            {
                _popup.IsOpen = false;
            }
        }
    }

    private string DisplayText
        => SelectedDate is { } date
            ? date.ToString(string.IsNullOrEmpty(Format) ? "yyyy-MM-dd" : Format, CultureInfo.CurrentCulture)
            : Placeholder;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var sample = string.IsNullOrEmpty(DisplayText) ? "yyyy-MM-dd" : DisplayText;
        var measured = VisualText.Measure(sample, DefaultFont, 0f, wrap: false, RenderDpi);
        return new Size(
            (int)Math.Ceiling(measured.Width + Padding.Horizontal + 30),
            Math.Max(Theme.Current.ControlHeight, (int)Math.Ceiling(measured.Height + Padding.Vertical + 2)));
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var theme = Theme.Current;
        var background = Background?.ApproximateColor
            ?? (_hover || _popup.IsOpen ? theme.ControlHover : theme.ControlBackground);
        var bounds = new Rectangle(0, 0, Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        var radius = CornerRadius > 0 ? CornerRadius : theme.CornerRadius;
        XamlDrawing.PaintChrome(
            graphics,
            bounds,
            background,
            BorderBrush?.ApproximateColor ?? theme.ControlBorder,
            BorderThickness.IsEmpty ? new XamlThickness(1f) : BorderThickness,
            radius);

        if (IsFocused)
        {
            using var focusPath = XamlDrawing.RoundedRectangle(
                new RectangleF(1.5f, 1.5f, Math.Max(0f, bounds.Width - 3f), Math.Max(0f, bounds.Height - 3f)),
                Math.Max(0f, radius - 1f));
            using var focusPen = new Pen(theme.FocusRing, 1.6f);
            graphics.DrawPath(focusPen, focusPath);
        }

        var glyph = new RectangleF(bounds.Width - 24f, (bounds.Height - 14f) / 2f, 14f, 14f);
        using (var pen = new Pen(theme.MutedForeground, 1.3f))
        {
            graphics.DrawRectangle(pen, glyph.X, glyph.Y, glyph.Width, glyph.Height);
            graphics.DrawLine(pen, glyph.X, glyph.Y + 4f, glyph.Right, glyph.Y + 4f);
            graphics.DrawLine(pen, glyph.X + 4.5f, glyph.Y, glyph.X + 4.5f, glyph.Bottom);
            graphics.DrawLine(pen, glyph.Right - 4.5f, glyph.Y, glyph.Right - 4.5f, glyph.Bottom);
        }

        var text = DisplayText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        VisualText.Draw(this,
            graphics,
            text,
            DefaultFont,
            SelectedDate is null ? theme.MutedForeground : theme.Foreground,
            new Rectangle(
                (int)Math.Ceiling(Padding.Left),
                (int)Math.Ceiling(Padding.Top),
                Math.Max(0, bounds.Width - (int)Math.Ceiling(Padding.Horizontal) - 20),
                Math.Max(0, bounds.Height - (int)Math.Ceiling(Padding.Vertical))),
            wrap: false,
            StringAlignment.Near,
            StringAlignment.Center);
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        base.OnPointerEntered(args);
        _hover = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        _hover = false;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        if (!IsEnabled || args.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();
        IsDropDownOpen = !IsDropDownOpen;
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        switch (args.KeyCode)
        {
            case Keys.Down when (args.Modifiers & Keys.Alt) != 0:
            case Keys.Enter:
            case Keys.Space:
                IsDropDownOpen = true;
                args.Handled = true;
                break;
            case Keys.Escape when _popup.IsOpen:
                _popup.IsOpen = false;
                args.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _popup.Closed -= OnPopupClosed;
            _calendar.SelectedDateChanged -= OnCalendarSelected;
            _popup.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnCalendarSelected(object? sender, EventArgs e)
    {
        if (_opening)
        {
            return;
        }

        SelectedDate = _calendar.SelectedDate;
        _popup.IsOpen = false;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        Focus();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, SelectedDateProperty))
        {
            if (e.NewValue is null && _popup.IsOpen)
            {
                _calendar.SelectedDate = null;
            }

            SelectedDateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
