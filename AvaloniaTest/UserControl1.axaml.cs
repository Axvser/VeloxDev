using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VeloxDev.Core.Generators;
using VeloxDev.Core.AspectOriented;
using VeloxDev.Core.AopInterfaces;
using Avalonia.Media;

namespace AvaloniaTest;

public partial class UserControl1 : UserControl, UserControl1_Global_Aop
{
    public UserControl1()
    {
        InitializeComponent();
    }

    [AspectOriented]
    public string UID { get; set; } = "UserControl1"; // Example property
}