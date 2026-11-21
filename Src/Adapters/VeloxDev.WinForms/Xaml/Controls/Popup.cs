using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>Where a <see cref="Popup"/> appears relative to its <see cref="Popup.PlacementTarget"/>.</summary>
public enum PlacementMode
{
    /// <summary>Below the target.</summary>
    Bottom = 0,

    /// <summary>Above the target.</summary>
    Top = 1,

    /// <summary>Left of the target.</summary>
    Left = 2,

    /// <summary>Right of the target.</summary>
    Right = 3,

    /// <summary>Centred over the target.</summary>
    Center = 4,
}

/// <summary>
/// Hosts one child in a borderless top-level window, positioned relative to a target control.
/// </summary>
/// <remarks>
/// WinForms child windows cannot float above sibling windows, so an open popup is shown in its own form. The child
/// is moved into that form while open and detached when it closes; the popup keeps ownership either way.
/// </remarks>
public class Popup : Control
{
    /// <summary>Whether the popup window is shown.</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen),
        typeof(bool),
        typeof(Popup),
        new PropertyMetadata(false, OnIsOpenPropertyChanged));

    /// <summary>The control hosted by the popup.</summary>
    public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        nameof(Child),
        typeof(System.Windows.Forms.Control),
        typeof(Popup),
        new PropertyMetadata(null, OnChildPropertyChanged));

    /// <summary>The control the popup is positioned against; null uses the popup's parent.</summary>
    public static readonly DependencyProperty PlacementTargetProperty = DependencyProperty.Register(
        nameof(PlacementTarget),
        typeof(Control),
        typeof(Popup),
        new PropertyMetadata(null, OnPlacementPropertyChanged));

    /// <summary>Where the popup appears relative to its target.</summary>
    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement),
        typeof(PlacementMode),
        typeof(Popup),
        new PropertyMetadata(PlacementMode.Bottom, OnPlacementPropertyChanged));

    /// <summary>A horizontal offset added to the computed position.</summary>
    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HorizontalOffset),
        typeof(int),
        typeof(Popup),
        new PropertyMetadata(0, OnPlacementPropertyChanged));

    /// <summary>A vertical offset added to the computed position.</summary>
    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset),
        typeof(int),
        typeof(Popup),
        new PropertyMetadata(0, OnPlacementPropertyChanged));

    private Form? host;

    /// <summary>Creates an empty popup.</summary>
    public Popup()
    {
    }

    /// <summary>Gets or sets whether the popup window is shown.</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Gets or sets the control hosted by the popup.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? Child
    {
        get => (System.Windows.Forms.Control?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>Gets or sets the control the popup is positioned against.</summary>
    public Control? PlacementTarget
    {
        get => (Control?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    /// <summary>Gets or sets where the popup appears relative to its target.</summary>
    public PlacementMode Placement
    {
        get => (PlacementMode)GetValue(PlacementProperty)!;
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Gets or sets the horizontal offset added to the computed position.</summary>
    public int HorizontalOffset
    {
        get => (int)GetValue(HorizontalOffsetProperty)!;
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>Gets or sets the vertical offset added to the computed position.</summary>
    public int VerticalOffset
    {
        get => (int)GetValue(VerticalOffsetProperty)!;
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseHost(disposeChild: true);
        }

        base.Dispose(disposing);
    }

    private static void OnIsOpenPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Popup popup || popup.IsDisposed)
        {
            return;
        }

        if (e.NewValue is true)
        {
            popup.Open();
        }
        else
        {
            popup.CloseHost(disposeChild: false);
        }
    }

    private static void OnChildPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Popup popup || popup.IsDisposed)
        {
            return;
        }

        if (popup.host is not null)
        {
            popup.host.Controls.Remove(e.OldValue as System.Windows.Forms.Control);
            popup.AddChildToHost(e.NewValue as System.Windows.Forms.Control);
        }

        if (popup.IsOpen)
        {
            popup.Place();
        }
    }

    private static void OnPlacementPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is Popup { IsOpen: true } popup && !popup.IsDisposed)
        {
            popup.Place();
        }
    }

    private void Open()
    {
        host ??= CreateHost();
        AddChildToHost(Child);
        if (Child is Control xaml)
        {
            xaml.DataContext = DataContext;
        }

        host.Show();
        host.BringToFront();
        Place();
    }

    private Form CreateHost()
    {
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = System.Windows.Forms.Padding.Empty,
        };

        form.Deactivate += OnHostDeactivate;
        form.FormClosed += OnHostClosed;
        return form;
    }

    private void AddChildToHost(System.Windows.Forms.Control? child)
    {
        if (child is null || host is null || host.Controls.Contains(child))
        {
            return;
        }

        host.Controls.Add(child);
    }

    private void Place()
    {
        if (host is null)
        {
            return;
        }

        var target = PlacementTarget ?? Parent as Control;
        if (target is null)
        {
            var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
            host.Location = new Point(
                screen.Left + (screen.Width - host.Width) / 2 + HorizontalOffset,
                screen.Top + (screen.Height - host.Height) / 2 + VerticalOffset);
            return;
        }

        var origin = target.PointToScreen(Point.Empty);
        var width = host.Width;
        var height = host.Height;
        var point = Placement switch
        {
            PlacementMode.Top => new Point(origin.X, origin.Y - height),
            PlacementMode.Left => new Point(origin.X - width, origin.Y),
            PlacementMode.Right => new Point(origin.X + target.Width, origin.Y),
            PlacementMode.Center => new Point(
                origin.X + (target.Width - width) / 2,
                origin.Y + (target.Height - height) / 2),
            _ => new Point(origin.X, origin.Y + target.Height),
        };

        host.Location = new Point(point.X + HorizontalOffset, point.Y + VerticalOffset);
    }

    private void OnHostDeactivate(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            IsOpen = false;
        }
    }

    private void OnHostClosed(object? sender, FormClosedEventArgs e)
    {
        host = null;
    }

    private void CloseHost(bool disposeChild)
    {
        var child = Child;
        if (host is not null)
        {
            host.Deactivate -= OnHostDeactivate;
            host.FormClosed -= OnHostClosed;
            if (child is not null && host.Controls.Contains(child))
            {
                host.Controls.Remove(child);
            }

            host.Hide();
            host.Dispose();
            host = null;
        }

        if (disposeChild && child is not null && !child.IsDisposed)
        {
            child.Dispose();
        }
    }
}
