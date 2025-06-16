using Avalonia.Controls;
using VeloxDev.Core.AspectOriented;
using VeloxDev.Core.Generators;

namespace AvaloniaTest;

[MonoBehaviour]
public partial class Button : UserControl
{
    public Button()
    {
        InitializeComponent();
    }

    [AspectOriented]
    public string UID { get; set; } = string.Empty;
}