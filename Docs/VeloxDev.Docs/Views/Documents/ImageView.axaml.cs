using Avalonia.Media;
using System;
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
        SizeChanged += (_, _) =>
        {
            if (_provider is { } provider)
                ApplyScale(provider);
        };
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
            var provider = _provider;
            provider.PropertyChanged += ProviderPropertyChanged;
            ApplyScale(provider);
            _ = provider.EnsureLoadedAsync();
        }
    }

    private void ProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_provider is not { } provider)
            return;

        if (e.PropertyName == nameof(ImageProvider.ScaleX) ||
            e.PropertyName == nameof(ImageProvider.ScaleY) ||
            e.PropertyName == nameof(ImageProvider.PixelWidth) ||
            e.PropertyName == nameof(ImageProvider.PixelHeight) ||
            e.PropertyName == nameof(ImageProvider.ImageSource) ||
            e.PropertyName == nameof(ImageProvider.SizeMode) ||
            e.PropertyName == nameof(ImageProvider.Alignment) ||
            e.PropertyName == nameof(ImageProvider.KeepAspectRatio))
            ApplyScale(provider);
    }

    private void ApplyScale(ImageProvider image)
    {
        var alignment = image.GetHorizontalAlignment();
        var stretch = image.KeepAspectRatio ? Stretch.Uniform : Stretch.Fill;
        var fillWidth = image.IsFillWidthMode;
        var availableWidth = GetAvailableImageWidth();
        var height = fillWidth
            ? image.GetScaledHeightForWidth(availableWidth)
            : image.GetScaledHeight();

        DisplayImage.HorizontalAlignment = alignment;
        EditImage.HorizontalAlignment = alignment;
        DisplayImage.Stretch = stretch;
        EditImage.Stretch = stretch;
        DisplayImage.Width = fillWidth ? availableWidth : image.GetScaledWidth();
        DisplayImage.Height = height;
        EditImage.Width = fillWidth ? availableWidth : image.GetScaledWidth();
        EditImage.Height = height;
    }

    private double GetAvailableImageWidth()
    {
        var width = Bounds.Width;
        if (double.IsNaN(width) || width <= 0)
            return _provider?.GetScaledWidth() ?? 0;

        const double chromePadding = 16;
        return Math.Max(16, width - chromePadding);
    }
}
