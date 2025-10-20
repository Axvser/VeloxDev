using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Demo.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void User_Click(object? sender, RoutedEventArgs e)
    {
        object student = new Student();
        var type = student.GetType();
        var property = type.GetProperty("Name");
        var propertyValue = property?.GetValue(student);
        if (propertyValue != null)
            Bt0.Content = propertyValue.ToString();
    }
}