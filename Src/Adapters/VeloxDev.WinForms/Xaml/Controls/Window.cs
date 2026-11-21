using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A top-level window whose chrome and content are owned by this XAML control.
/// </summary>
/// <remarks>
/// <para>
/// The control itself is not a visible surface: <see cref="Show"/> creates a borderless <see cref="Form"/>, paints a
/// theme-coloured title strip, hosts <see cref="Content"/> below it and follows the active theme. The strip can be
/// dragged, double-clicked to maximise and carries a close button.
/// </para>
/// <para>
/// This is not a WPF <c>Window</c>. There is no <c>WindowChrome</c>, no owner/z-order relationship, no
/// <c>DialogResult</c>, no modal <c>ShowDialog</c>, and no automatic lifetime tie to a parent control. Closing the
/// form detaches <see cref="Content"/> so a later <see cref="Show"/> can reuse it; disposing the control disposes the
/// content and the form.
/// </para>
/// </remarks>
public class Window : Control
{
    /// <summary>The text drawn in the title strip and passed to the native form.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(Window),
        new PropertyMetadata(string.Empty, OnTitlePropertyChanged) { AffectsRender = false });

    /// <summary>The control hosted below the title strip.</summary>
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content),
        typeof(System.Windows.Forms.Control),
        typeof(Window),
        new PropertyMetadata(null, OnContentPropertyChanged));

    private const int HeaderHeight = 36;
    private const int WmNcLButtonDown = 0xA1;
    private const int HtCaption = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    private Form? _form;
    private System.Windows.Forms.Control? _hostedContent;
    private Rectangle _closeBounds;

    /// <summary>Creates the window and follows theme changes.</summary>
    public Window()
    {
        Theme.Changed += OnThemeChanged;
    }

    /// <summary>Gets or sets the text drawn in the title strip.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => (string?)GetValue(TitleProperty) ?? string.Empty;
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the control hosted below the title strip.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? Content
    {
        get => (System.Windows.Forms.Control?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>Whether the hosted form is currently visible.</summary>
    public bool IsVisible => _form is { Visible: true };

    /// <summary>The native form while it exists, or null before <see cref="Show"/> and after closing.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Form? HostForm => _form;

    /// <summary>Raised after the hosted form has closed, whether through <see cref="Close"/> or the window itself.</summary>
    public event EventHandler? Closed;

    /// <summary>Creates the hosted form when needed and shows it.</summary>
    /// <remarks>The call must run on a thread with a message loop; the form is modeless.</remarks>
    /// <exception cref="ObjectDisposedException">This control has been disposed.</exception>
    public new void Show()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(Window));
        }

        AdoptDirectChild();
        EnsureForm();
        _form!.Show();
        _form.BringToFront();
        AttachContent();
        LayoutContent();
    }

    /// <summary>Closes the hosted form when it is open.</summary>
    public void Close()
    {
        if (_form is { IsDisposed: false } form)
        {
            form.Close();
        }
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : 640;
        var height = proposedSize.Height > 0 ? proposedSize.Height : 480;
        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);

        if (_form is not { IsDisposed: false } form)
        {
            return;
        }

        if (ReferenceEquals(property, WidthProperty) && newValue is int width && width >= 0)
        {
            form.ClientSize = new Size(width, form.ClientSize.Height);
        }
        else if (ReferenceEquals(property, HeightProperty) && newValue is int height && height >= 0)
        {
            form.ClientSize = new Size(form.ClientSize.Width, height);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;

            // 关闭会先把内容从 Form 里摘出来；先抓住引用，Dispose 才是最终释放内容的那一方。
            var content = _hostedContent ?? Content;
            DetachFormEvents();
            if (_form is { IsDisposed: false } form)
            {
                form.Close();
            }

            _form = null;

            if (content is { IsDisposed: false })
            {
                content.Dispose();
            }

            _hostedContent = null;
        }

        base.Dispose(disposing);
    }

    private void AdoptDirectChild()
    {
        // 直接写在 <v:Window> 下的子控件会被 XAML 生成器加进 Controls；没有显式 Content 时把它当成内容。
        if (Content is null && Controls.Count > 0)
        {
            var child = Controls[0];
            Controls.RemoveAt(0);
            Content = child;
        }
    }

    private void EnsureForm()
    {
        if (_form is { IsDisposed: false })
        {
            return;
        }

        var widthValue = (int)GetValue(WidthProperty)!;
        var heightValue = (int)GetValue(HeightProperty)!;
        var form = new Form
        {
            AutoScaleMode = AutoScaleMode.None,
            BackColor = Theme.Current.WindowBackground,
            ClientSize = new Size(
                widthValue > 0 ? widthValue : 640,
                heightValue > 0 ? heightValue : 480),
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterScreen,
            Text = Title,
        };

        form.Paint += OnFormPaint;
        form.MouseDown += OnFormMouseDown;
        form.FormClosing += OnFormClosing;
        form.FormClosed += OnFormClosed;
        form.Resize += OnFormResize;
        _form = form;
    }

    private void AttachContent()
    {
        if (_form is not { IsDisposed: false } form)
        {
            return;
        }

        var content = Content;
        if (content is null)
        {
            DetachHostedContent();
            return;
        }

        if (ReferenceEquals(_hostedContent, content) && form.Controls.Contains(content))
        {
            return;
        }

        DetachHostedContent();
        _hostedContent = content;
        if (!form.Controls.Contains(content))
        {
            form.Controls.Add(content);
        }

        content.BringToFront();
        LayoutContent();
    }

    private void DetachHostedContent()
    {
        var content = _hostedContent;
        _hostedContent = null;
        if (content is not null
            && _form is { IsDisposed: false } form
            && form.Controls.Contains(content))
        {
            form.Controls.Remove(content);
        }
    }

    private void LayoutContent()
    {
        if (_form is not { IsDisposed: false } form || _hostedContent is null)
        {
            return;
        }

        var width = Math.Max(0, form.ClientSize.Width);
        var height = Math.Max(0, form.ClientSize.Height - HeaderHeight);
        var bounds = new Rectangle(0, HeaderHeight, width, height);
        if (_hostedContent.Bounds != bounds)
        {
            _hostedContent.Bounds = bounds;
        }
    }

    private void OnFormPaint(object? sender, PaintEventArgs e)
    {
        if (_form is not { IsDisposed: false } form)
        {
            return;
        }

        var width = form.ClientSize.Width;
        using (var header = new SolidBrush(Theme.Current.SurfaceAlt))
        {
            e.Graphics.FillRectangle(header, 0, 0, width, HeaderHeight);
        }

        using (var line = new Pen(Theme.Current.ControlBorder))
        {
            e.Graphics.DrawLine(line, 0, HeaderHeight - 1, width, HeaderHeight - 1);
        }

        _closeBounds = new Rectangle(Math.Max(0, width - HeaderHeight), 0, HeaderHeight, HeaderHeight);
        var titleBounds = new Rectangle(12, 0, Math.Max(0, _closeBounds.Left - 12), HeaderHeight);
        TextRenderer.DrawText(
            e.Graphics,
            Title,
            form.Font,
            titleBounds,
            Theme.Current.Foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            e.Graphics,
            "×",
            form.Font,
            _closeBounds,
            Theme.Current.MutedForeground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }

    private void OnFormMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _form is not { IsDisposed: false } form)
        {
            return;
        }

        if (_closeBounds.Contains(e.Location))
        {
            Close();
            return;
        }

        if (e.Y < HeaderHeight && form.Handle != IntPtr.Zero)
        {
            // 交给系统非客户区拖动：命中标题条后 WinForms 自己处理移动、双击最大化与贴边。
            ReleaseCapture();
            SendMessage(form.Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Form 关闭时会释放子控件；先把内容摘出来，再次 Show 才能复用它。
        DetachHostedContent();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        DetachFormEvents();
        _form = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFormResize(object? sender, EventArgs e) => LayoutContent();

    private void DetachFormEvents()
    {
        if (_form is not { IsDisposed: false } form)
        {
            return;
        }

        form.Paint -= OnFormPaint;
        form.MouseDown -= OnFormMouseDown;
        form.FormClosing -= OnFormClosing;
        form.FormClosed -= OnFormClosed;
        form.Resize -= OnFormResize;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_form is not { IsDisposed: false } form)
        {
            return;
        }

        form.BackColor = Theme.Current.WindowBackground;
        form.Invalidate();
        Theme.ApplyTo(form);
    }

    private static void OnTitlePropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Window window || window.IsDisposed)
        {
            return;
        }

        if (window._form is { IsDisposed: false } form)
        {
            form.Text = window.Title;
            form.Invalidate();
        }
    }

    private static void OnContentPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Window window && !window.IsDisposed && window._form is not null)
        {
            window.AttachContent();
        }
    }
}
