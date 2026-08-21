using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Demo.Models;

/// <summary>
/// Represents an animatable "rectangle" state object
/// VeloxDev animations act directly on this ViewModel's properties
/// Blazor drives UI re-rendering through the PropertyChanged event
/// </summary>
public class BoxModel : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private double _width = 120;
    private double _height = 80;
    private double _opacity = 1;
    private double _rotate;
    private double _scale = 1;
    private string _color = "#00bcd4";

    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
    }

    public double Width
    {
        get => _width;
        set { _width = value; OnPropertyChanged(); }
    }

    public double Height
    {
        get => _height;
        set { _height = value; OnPropertyChanged(); }
    }

    public double Opacity
    {
        get => _opacity;
        set { _opacity = value; OnPropertyChanged(); }
    }

    /// <summary>Rotation angle (degrees)</summary>
    public double Rotate
    {
        get => _rotate;
        set { _rotate = value; OnPropertyChanged(); }
    }

    /// <summary>Scale factor</summary>
    public double Scale
    {
        get => _scale;
        set { _scale = value; OnPropertyChanged(); }
    }

    /// <summary>CSS color string, e.g. "#00bcd4" or "rgba(255,0,0,0.5)"</summary>
    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
    }

    /// <summary>Builds the corresponding CSS inline style string</summary>
    public string Style =>
        $"width:{Width:F1}px;" +
        $"height:{Height:F1}px;" +
        $"opacity:{Opacity:F3};" +
        $"background:{Color};" +
        $"transform:translateX({X:F1}px) translateY({Y:F1}px) rotate({Rotate:F1}deg) scale({Scale:F4});";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
