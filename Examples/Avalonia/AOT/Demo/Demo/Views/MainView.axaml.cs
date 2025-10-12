using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Demo.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void Click0(object? sender, RoutedEventArgs e)
    {
        object instance = new ReflectiveInstance
        {
            Name = "Hello, AOT Reflection!"
        };
        var type = instance.GetType();
        var prop = type.GetProperty("Name");
        var value = prop?.GetValue(instance);
        if (value != null)
            bt.Content = value.ToString();
    }
}