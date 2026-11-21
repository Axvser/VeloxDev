using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Scales a single child into the available area, like a WPF or Avalonia viewbox.
/// </summary>
/// <remarks>
/// <para>
/// This is a best-effort WinForms implementation. A child is a native window, so its pixel content cannot be
/// vector-scaled; the viewbox instead resizes the child's bounds (and, for a XAML <see cref="Control"/>, its
/// <see cref="Control.Font"/>). Use it for layout scaling and for self-painted XAML controls; text and geometry
/// inside native controls keep their own layout metrics.
/// </para>
/// <para>
/// <see cref="Stretch"/> follows the usual meanings: <see cref="Stretch.None"/> keeps the measured size,
/// <see cref="Stretch.Fill"/> fills both axes, <see cref="Stretch.Uniform"/> keeps the aspect ratio inside the
/// available area and <see cref="Stretch.UniformToFill"/> keeps it while covering the area.
/// </para>
/// </remarks>
public class Viewbox : Control
{
    /// <summary>The single child scaled by this viewbox; a child added directly to <see cref="Control.Controls"/> also works.</summary>
    public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        nameof(Child),
        typeof(System.Windows.Forms.Control),
        typeof(Viewbox),
        new PropertyMetadata(null, OnChildPropertyChanged));

    /// <summary>How the child fills or fits the available area; <see cref="Stretch.Uniform"/> by default.</summary>
    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(Stretch),
        typeof(Viewbox),
        new PropertyMetadata(Stretch.Uniform, OnStretchPropertyChanged));

    private System.Windows.Forms.Control? _hostChild;
    private System.Windows.Forms.Control? _fontChild;
    private Font? _originalFont;
    private Font? _appliedFont;
    private bool _originalFontHadLocalValue;
    private float _appliedScale = 1f;
    private bool _applyingScale;
    private bool _arranging;

    /// <summary>Creates the viewbox with double buffering and resize redrawing.</summary>
    public Viewbox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the single child scaled by this viewbox.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? Child
    {
        get => (System.Windows.Forms.Control?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>Gets or sets how the child fills or fits the available area.</summary>
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var padding = Padding;
        var inset = new Size((int)Math.Ceiling(padding.Horizontal), (int)Math.Ceiling(padding.Vertical));
        var child = ResolveChild();
        if (child is null || child.IsDisposed)
        {
            return inset;
        }

        var measured = XamlLayout.Measure(child, proposedSize);
        return new Size(measured.Width + inset.Width, measured.Height + inset.Height);
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_arranging)
        {
            return;
        }

        _arranging = true;
        try
        {
            ArrangeChild();
        }
        finally
        {
            _arranging = false;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RestoreFont();
        }

        base.Dispose(disposing);
    }

    private System.Windows.Forms.Control? ResolveChild()
        => _hostChild ?? (Controls.Count > 0 ? Controls[0] : null);

    private void ArrangeChild()
    {
        var child = ResolveChild();
        if (child is null || child.IsDisposed)
        {
            return;
        }

        var content = XamlLayout.ContentBounds(this);
        if (content.Width <= 0 || content.Height <= 0)
        {
            return;
        }

        var natural = XamlLayout.Measure(child, Size.Empty);
        var naturalWidth = Math.Max(1, natural.Width);
        var naturalHeight = Math.Max(1, natural.Height);

        var scaleX = 1f;
        var scaleY = 1f;
        switch (Stretch)
        {
            case Stretch.Fill:
                scaleX = content.Width / (float)naturalWidth;
                scaleY = content.Height / (float)naturalHeight;
                break;
            case Stretch.Uniform:
                scaleX = scaleY = Math.Min(content.Width / (float)naturalWidth, content.Height / (float)naturalHeight);
                break;
            case Stretch.UniformToFill:
                scaleX = scaleY = Math.Max(content.Width / (float)naturalWidth, content.Height / (float)naturalHeight);
                break;
        }

        if (float.IsNaN(scaleX) || float.IsInfinity(scaleX) || scaleX <= 0f)
        {
            scaleX = 1f;
        }

        if (float.IsNaN(scaleY) || float.IsInfinity(scaleY) || scaleY <= 0f)
        {
            scaleY = 1f;
        }

        int width;
        int height;
        int x;
        int y;
        if (Stretch == Stretch.Fill)
        {
            x = content.Left;
            y = content.Top;
            width = content.Width;
            height = content.Height;
        }
        else
        {
            width = Math.Max(0, (int)Math.Round(naturalWidth * scaleX));
            height = Math.Max(0, (int)Math.Round(naturalHeight * scaleY));
            x = content.Left + Math.Max(0, (content.Width - width) / 2);
            y = content.Top + Math.Max(0, (content.Height - height) / 2);
        }

        var bounds = new Rectangle(x, y, width, height);
        if (child.Bounds != bounds)
        {
            child.Bounds = bounds;
        }

        // 字体只能等比缩放：Fill 的两轴比例不同，取较小值以免文字超出高度。
        ApplyFontScale(child, Math.Min(scaleX, scaleY));
    }

    private void ApplyFontScale(System.Windows.Forms.Control child, float scale)
    {
        if (_applyingScale)
        {
            return;
        }

        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
        {
            scale = 1f;
        }

        if (!ReferenceEquals(_fontChild, child))
        {
            RestoreFont();
            CaptureFont(child);
        }

        if (Math.Abs(scale - 1f) < 0.001f)
        {
            RestoreAppliedFont();
            return;
        }

        if (_appliedFont is not null && Math.Abs(scale - _appliedScale) < 0.001f)
        {
            return;
        }

        if (_originalFont is null)
        {
            return;
        }

        var size = _originalFont.SizeInPoints * scale;
        if (size < 0.5f || size > 2000f)
        {
            RestoreAppliedFont();
            return;
        }

        Font scaled;
        try
        {
            scaled = new Font(_originalFont.FontFamily, size, _originalFont.Style, _originalFont.Unit);
        }
        catch (ArgumentException)
        {
            return;
        }

        SetFontValue(child, scaled, hasLocalValue: true);
        _appliedFont?.Dispose();
        _appliedFont = scaled;
        _appliedScale = scale;
    }

    private void CaptureFont(System.Windows.Forms.Control child)
    {
        _fontChild = child;
        _originalFont = child is Control xaml ? xaml.Font : child.Font;
        _originalFontHadLocalValue = child is Control xamlControl && xamlControl.HasLocalValue(FontProperty);
    }

    private void RestoreAppliedFont()
    {
        if (_appliedFont is null)
        {
            return;
        }

        SetFontValue(_fontChild, _originalFont, _originalFontHadLocalValue);
        _appliedFont.Dispose();
        _appliedFont = null;
        _appliedScale = 1f;
    }

    private void RestoreFont()
    {
        if (_fontChild is null)
        {
            return;
        }

        if (_appliedFont is not null)
        {
            SetFontValue(_fontChild, _originalFont, _originalFontHadLocalValue);
            _appliedFont.Dispose();
        }

        _fontChild = null;
        _originalFont = null;
        _appliedFont = null;
        _appliedScale = 1f;
        _originalFontHadLocalValue = false;
    }

    private void SetFontValue(System.Windows.Forms.Control? child, Font? font, bool hasLocalValue)
    {
        if (child is null || child.IsDisposed)
        {
            return;
        }

        _applyingScale = true;
        try
        {
            if (child is Control xaml)
            {
                if (hasLocalValue && font is not null)
                {
                    xaml.SetValue(FontProperty, font);
                }
                else
                {
                    xaml.ClearValue(FontProperty);
                }
            }
            else if (font is not null)
            {
                child.Font = font;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            // 子控件可能在布局与销毁之间被移走；缩放失败不该带走布局本身。
        }
        finally
        {
            _applyingScale = false;
        }
    }

    private static void OnChildPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Viewbox viewbox && !viewbox.IsDisposed)
        {
            viewbox.ApplyChild(e.NewValue as System.Windows.Forms.Control);
        }
    }

    private void ApplyChild(System.Windows.Forms.Control? child)
    {
        if (ReferenceEquals(_hostChild, child))
        {
            PerformLayout();
            return;
        }

        if (_hostChild is not null && !ReferenceEquals(_hostChild, child) && Controls.Contains(_hostChild))
        {
            if (ReferenceEquals(_fontChild, _hostChild))
            {
                RestoreFont();
            }

            Controls.Remove(_hostChild);
        }

        _hostChild = child;
        if (child is not null && !Controls.Contains(child))
        {
            Controls.Add(child);
        }

        PerformLayout();
        Invalidate();
    }

    private static void OnStretchPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Viewbox viewbox && !viewbox.IsDisposed)
        {
            viewbox.PerformLayout();
            viewbox.Invalidate();
        }
    }
}
