using System.Drawing;
using System.Windows.Forms;
using Easel;
using Easel.Visuals;
using Easel.Visuals.Controls;
using Button = Easel.Visuals.Controls.Button;

namespace Demo.Views.Workbench;

/// <summary>
/// One square button in the 48px activity bar. The visual backend's <see cref="Button"/> can outline every edge
/// but not the 2px active strip on the left alone, so the face is painted by the base button and the strip is
/// added here: hover lifts the ground, active adds the accent strip and brightens the glyph.
/// </summary>
public sealed class ActivityButton : Button
{
    /// <summary>Whether this activity is the visible sidebar page.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ActivityButton),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Creates a glyph button with no chrome of its own.</summary>
    public ActivityButton()
    {
        Focusable = false;
        IsTabStop = false;
        BorderBrush = new SolidColorBrush(Color.Transparent);
        Padding = default;
        CornerRadius = 0;
        Cursor = Cursors.Hand;
        Background = new SolidColorBrush(WorkbenchPalette.ActivityBar);
        Foreground = WorkbenchPalette.TextTertiary;
    }

    /// <summary>Whether this activity is the visible sidebar page.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty)!;
        set => SetValue(IsActiveProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, IsActiveProperty))
        {
            ApplyState();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        Background = new SolidColorBrush(WorkbenchPalette.Hover);
        ApplyState();
        base.OnPointerEntered(args);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        Background = new SolidColorBrush(WorkbenchPalette.ActivityBar);
        ApplyState();
        base.OnPointerExited(args);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        base.OnRender(graphics);

        if (!IsActive)
        {
            return;
        }

        using var strip = new SolidBrush(WorkbenchPalette.Accent);
        graphics.FillRectangle(strip, 0, 0, 2, Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
    }

    private void ApplyState()
    {
        Foreground = IsActive
            ? Color.White
            : IsPointerOver ? WorkbenchPalette.TextPrimary : WorkbenchPalette.TextTertiary;
        InvalidateVisual();
    }
}

/// <summary>
/// An editor/inspector tab: the active one carries the accent underline and brighter text. The base button draws
/// the label; this class only adds the underline, so DPI-scaled text keeps the runtime's own path.
/// </summary>
public sealed class TabButton : Button
{
    /// <summary>Whether this tab owns the visible page.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(TabButton),
        new PropertyMetadata(false) { AffectsRender = true });

    /// <summary>Creates a flat tab with no chrome.</summary>
    public TabButton()
    {
        Focusable = true;
        BorderBrush = new SolidColorBrush(Color.Transparent);
        Padding = default;
        CornerRadius = 0;
        FontSize = 8.5;
        Cursor = Cursors.Hand;
        Background = new SolidColorBrush(Color.Transparent);
        Foreground = WorkbenchPalette.TextSecondary;
    }

    /// <summary>Whether this tab owns the visible page.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty)!;
        set => SetValue(IsActiveProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, IsActiveProperty))
        {
            ApplyState();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(VisualPointerEventArgs args)
    {
        Background = new SolidColorBrush(WorkbenchPalette.Hover);
        ApplyState();
        base.OnPointerEntered(args);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        Background = new SolidColorBrush(Color.Transparent);
        ApplyState();
        base.OnPointerExited(args);
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        base.OnRender(graphics);

        if (!IsActive)
        {
            return;
        }

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height));
        using var strip = new SolidBrush(WorkbenchPalette.Accent);
        graphics.FillRectangle(strip, 0, height - 2, width, 2);
    }

    private void ApplyState()
    {
        Foreground = IsActive
            ? WorkbenchPalette.TextPrimary
            : IsPointerOver ? WorkbenchPalette.TextPrimary : WorkbenchPalette.TextSecondary;
        InvalidateVisual();
    }
}
