using Avalonia.Controls;
using Avalonia.Media;
using VeloxDev.Core.AspectOriented;

namespace AvaloniaTest.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        var button = new Button();
        button.Proxy.SetProxy(ProxyMembers.Setter, nameof(Button.UID), null, null, (c, r) =>
        {
            Background = Brushes.Violet;
            return null;
        });
        button.Proxy.UID = "Hello, Avalonia!";
    }
}
