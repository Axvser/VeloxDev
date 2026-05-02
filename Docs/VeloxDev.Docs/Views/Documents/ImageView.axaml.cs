using Avalonia.Media;
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

            var delta = e.Vector.X + e.Vector.Y;
            var factor = 1 + (delta / 240.0);
            image.ResizeByFactor(factor);

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

        if (e.PropertyName == nameof(ImageProvider.ScaleX) ||
            e.PropertyName == nameof(ImageProvider.ScaleY) ||
            e.PropertyName == nameof(ImageProvider.PixelWidth) ||
            e.PropertyName == nameof(ImageProvider.PixelHeight) ||
            e.PropertyName == nameof(ImageProvider.ImageSource) ||
            e.PropertyName == nameof(ImageProvider.SizeMode) ||
            e.PropertyName == nameof(ImageProvider.Alignment) ||
            e.PropertyName == nameof(ImageProvider.KeepAspectRatio))
            ApplyScale(_provider);
    }

    private void ApplyScale(ImageProvider image)
    {
        var alignment = image.GetHorizontalAlignment();
        var stretch = image.KeepAspectRatio ? Stretch.Uniform : Stretch.Fill;
        DisplayImage.HorizontalAlignment = alignment;
        EditImage.HorizontalAlignment = alignment;
        DisplayImage.Stretch = stretch;
        EditImage.Stretch = stretch;
        DisplayImage.Width = image.GetScaledWidth();
        DisplayImage.Height = image.GetScaledHeight();
        EditImage.Width = image.GetScaledWidth();
        EditImage.Height = image.GetScaledHeight();
    }
}
