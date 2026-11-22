using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Draws a bitmap with a dependency-property source and stretch mode.
/// </summary>
/// <remarks>
/// A string <see cref="Source"/> is loaded from disk and owned by this control; a <see cref="System.Drawing.Image"/>
/// assigned directly is not disposed by the control. A missing file leaves the control empty instead of throwing.
/// </remarks>
public class Image : Control
{
    /// <summary>The bitmap to draw: a file path or a <see cref="System.Drawing.Image"/>.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(object),
        typeof(Image),
        new PropertyMetadata(null, OnSourcePropertyChanged));

    /// <summary>How the bitmap is fitted into the control.</summary>
    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(Stretch),
        typeof(Image),
        new PropertyMetadata(Stretch.Uniform, OnVisualPropertyChanged));

    private System.Drawing.Image? _image;
    private bool _ownsImage;

    /// <summary>Gets or sets the bitmap to draw.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets how the bitmap is fitted into the control.</summary>
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (_image is { } image)
        {
            return new Size(
                image.Width + (int)Math.Ceiling(Padding.Horizontal),
                image.Height + (int)Math.Ceiling(Padding.Vertical));
        }

        return new Size(
            (int)Math.Ceiling(Padding.Horizontal),
            (int)Math.Ceiling(Padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!IsPainted || _image is not { } image)
        {
            return;
        }

        var bounds = XamlLayout.ContentBounds(this);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var destination = ResolveDestination(bounds, image.Size);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(image, destination);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseImage();
        }

        base.Dispose(disposing);
    }

    private Rectangle ResolveDestination(Rectangle bounds, Size source)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return bounds;
        }

        switch (Stretch)
        {
            case Stretch.None:
                return new Rectangle(bounds.Left, bounds.Top, Math.Min(source.Width, bounds.Width), Math.Min(source.Height, bounds.Height));
            case Stretch.Fill:
                return bounds;
            case Stretch.UniformToFill:
            {
                var scale = Math.Max((float)bounds.Width / source.Width, (float)bounds.Height / source.Height);
                var width = (int)Math.Ceiling(source.Width * scale);
                var height = (int)Math.Ceiling(source.Height * scale);
                return new Rectangle(bounds.Left + ((bounds.Width - width) / 2), bounds.Top + ((bounds.Height - height) / 2), width, height);
            }
            default:
            {
                var scale = Math.Min((float)bounds.Width / source.Width, (float)bounds.Height / source.Height);
                var width = (int)Math.Ceiling(source.Width * scale);
                var height = (int)Math.Ceiling(source.Height * scale);
                return new Rectangle(bounds.Left + ((bounds.Width - width) / 2), bounds.Top + ((bounds.Height - height) / 2), width, height);
            }
        }
    }

    private void ApplySource(object? source)
    {
        ReleaseImage();
        switch (source)
        {
            case string path when !string.IsNullOrWhiteSpace(path):
                try
                {
                    if (File.Exists(path))
                    {
                        _image = System.Drawing.Image.FromFile(path);
                        _ownsImage = true;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
                {
                    _image = null;
                    _ownsImage = false;
                }

                break;
            case System.Drawing.Image image:
                _image = image;
                _ownsImage = false;
                break;
        }

        PerformLayout();
        Invalidate();
    }

    private void ReleaseImage()
    {
        if (_ownsImage)
        {
            _image?.Dispose();
        }

        _image = null;
        _ownsImage = false;
    }

    private static void OnSourcePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Image image && !image.IsDisposed)
        {
            image.ApplySource(e.NewValue);
        }
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }
}

/// <summary>
/// A one-pixel separator line between groups of controls.
/// </summary>
/// <remarks>The line is centred in the control's content rectangle; orientation and thickness are dependency properties.</remarks>
public class Separator : Control
{
    /// <summary>Whether the line runs horizontally (the default) or vertically.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(Separator),
        new PropertyMetadata(Orientation.Horizontal, OnVisualPropertyChanged));

    /// <summary>The line thickness in pixels; one by default.</summary>
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness),
        typeof(int),
        typeof(Separator),
        new PropertyMetadata(1, OnVisualPropertyChanged));

    /// <summary>Gets or sets the separator orientation.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets the line thickness in pixels.</summary>
    public int Thickness
    {
        get => (int)GetValue(ThicknessProperty)!;
        set => SetValue(ThicknessProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
        => Orientation == Orientation.Horizontal
            ? new Size(proposedSize.Width, Math.Max(1, Thickness))
            : new Size(Math.Max(1, Thickness), proposedSize.Height);

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = XamlLayout.ContentBounds(this);
        var thickness = Math.Max(1, Thickness);
        if (Orientation == Orientation.Horizontal)
        {
            var y = bounds.Top + (bounds.Height / 2);
            using var pen = new Pen(EffectiveForeground, thickness);
            e.Graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
        }
        else
        {
            var x = bounds.Left + (bounds.Width / 2);
            using var pen = new Pen(EffectiveForeground, thickness);
            e.Graphics.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
        }
    }

    private static void OnVisualPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Control control && !control.IsDisposed)
        {
            control.Invalidate();
        }
    }
}
