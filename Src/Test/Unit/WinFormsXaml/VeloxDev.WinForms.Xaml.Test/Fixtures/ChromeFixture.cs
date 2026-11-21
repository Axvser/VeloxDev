namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>The code-behind of the chrome/template XAML smoke fixture.</summary>
public partial class ChromeView : VeloxDev.WinForms.Xaml.Controls.Control
{
    /// <summary>Creates the view and runs the generated initializer.</summary>
    public ChromeView()
    {
        InitializeComponent();
    }

    /// <summary>The decorator host.</summary>
    public VeloxDev.WinForms.Xaml.Controls.Decorator Decorator => decorator;

    /// <summary>The adorner decorator host.</summary>
    public VeloxDev.WinForms.Xaml.Controls.AdornerDecorator AdornerHost => adornerHost;

    /// <summary>The headered content control.</summary>
    public VeloxDev.WinForms.Xaml.Controls.HeaderedContentControl Headered => headered;

    /// <summary>The content presenter.</summary>
    public VeloxDev.WinForms.Xaml.Controls.ContentPresenter Presenter => presenter;

    /// <summary>The toolbar.</summary>
    public VeloxDev.WinForms.Xaml.Controls.ToolBar Toolbar => toolbar;

    /// <summary>The status bar.</summary>
    public VeloxDev.WinForms.Xaml.Controls.StatusBar Status => status;

    /// <summary>The fixed-row virtualizing panel.</summary>
    public VeloxDev.WinForms.Xaml.Controls.VirtualizingStackPanel Virtualizing => virtualizing;
}
