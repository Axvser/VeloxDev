using Avalonia.Controls.Primitives;
using System.ComponentModel;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class ImageView : WikiElementViewBase
{
    private ImageProvider? _provider;

    public ImageView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DataContextChanged += (_, _) => AttachProvider();
        ResizeThumb.DragDelta += (_, e) =>
        {
            if (DataContext is not ImageProvider image)
                return;

            if (e.Vector.X + e.Vector.Y >= 0)
                image.IncreaseScale();
            else
                image.DecreaseScale();

            ApplyScale(image);
        };

        AttachProvider();
    }

    private void AttachProvider()
    {
        if (_provider is not null)
            _provider.PropertyChanged -= ProviderPropertyChanged;

        _provider = DataContext as ImageProvider;
        if (_provider is not null)
        {
            _provider.PropertyChanged += ProviderPropertyChanged;
            ApplyScale(_provider);
        }
    }

    private void ProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_provider is null)
            return;

        if (e.PropertyName == nameof(ImageProvider.Scale))
            ApplyScale(_provider);
    }

    private void ApplyScale(ImageProvider image)
    {
        var width = 640.0 * image.Scale;
        DisplayImage.Width = width;
        EditImage.Width = width;
    }
}
