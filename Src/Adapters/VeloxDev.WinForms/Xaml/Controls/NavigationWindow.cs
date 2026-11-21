using System.ComponentModel;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A <see cref="Window"/> whose content is a <see cref="Frame"/>, so navigation is available at the window level.
/// </summary>
/// <remarks>
/// <para>
/// The frame is created once and installed as <see cref="Window.Content"/>. Navigation methods forward to it; the
/// window-level <see cref="NavigationChanged"/> event is the frame's event.
/// </para>
/// <para>
/// Assigning a different <see cref="Window.Content"/> detaches the frame; this class is a convenience shell, not a
/// WPF <c>NavigationWindow</c> with its own journal, chrome buttons or URI navigation.
/// </para>
/// </remarks>
public class NavigationWindow : Window
{
    private readonly Frame _frame = new();

    /// <summary>Creates the window with an empty frame as its content.</summary>
    public NavigationWindow()
    {
        Content = _frame;
        Title = "Navigation";
    }

    /// <summary>The frame that hosts the pages.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Frame Frame => _frame;

    /// <summary>The page currently shown, or null when the frame is empty.</summary>
    public Page? CurrentPage => _frame.CurrentPage;

    /// <summary>Whether the frame can navigate back.</summary>
    public bool CanGoBack => _frame.CanGoBack;

    /// <summary>Whether the frame can navigate forward.</summary>
    public bool CanGoForward => _frame.CanGoForward;

    /// <summary>Raised after the current page changes through navigation.</summary>
    public event EventHandler? NavigationChanged
    {
        add => _frame.NavigationChanged += value;
        remove => _frame.NavigationChanged -= value;
    }

    /// <summary>Shows <paramref name="page"/> in the frame and appends it to the journal.</summary>
    /// <param name="page">The page to show.</param>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    public void Navigate(Page page) => _frame.Navigate(page);

    /// <summary>Navigates the frame back.</summary>
    /// <returns><see langword="true"/> when the frame moved back.</returns>
    public bool GoBack() => _frame.GoBack();

    /// <summary>Navigates the frame forward.</summary>
    /// <returns><see langword="true"/> when the frame moved forward.</returns>
    public bool GoForward() => _frame.GoForward();
}
