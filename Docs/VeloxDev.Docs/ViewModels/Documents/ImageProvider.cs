using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class ImageProvider : IWikiElement
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const double DefaultMaxWidth = 640;
    private const double DefaultMaxHeight = 360;
    private bool _syncingSize;

    private CancellationTokenSource? _loadCts;
    private double _baseWidth = DefaultMaxWidth;
    private double _baseHeight = DefaultMaxHeight;
    private bool _loadPending;

    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial string Source { get; set; }
    [VeloxProperty] public partial double ScaleX { get; set; }
    [VeloxProperty] public partial double ScaleY { get; set; }
    [VeloxProperty] public partial double PixelWidth { get; set; }
    [VeloxProperty] public partial double PixelHeight { get; set; }
    [VeloxProperty] public partial bool KeepAspectRatio { get; set; }
    [VeloxProperty] public partial string SizeMode { get; set; }
    [VeloxProperty] public partial string Alignment { get; set; }
    [VeloxProperty] public partial IImage? ImageSource { get; set; }
    [VeloxProperty] public partial bool IsLoading { get; set; }

    public IReadOnlyList<string> SizeModes { get; } = ["Scale", "Pixels"];
    public IReadOnlyList<string> Alignments { get; } = ["Left", "Center", "Right"];
    public bool IsScaleMode => string.Equals(SizeMode, "Scale", StringComparison.Ordinal);
    public bool IsPixelMode => string.Equals(SizeMode, "Pixels", StringComparison.Ordinal);
    public bool HasImage => ImageSource is not null;
    public bool HasNoImage => !IsLoading && ImageSource is null;

    public ImageProvider()
    {
        Source = string.Empty;
        ScaleX = 1.0;
        ScaleY = 1.0;
        PixelWidth = DefaultMaxWidth;
        PixelHeight = DefaultMaxHeight * 0.75;
        KeepAspectRatio = true;
        SizeMode = "Scale";
        Alignment = "Center";
    }

    partial void OnSourceChanged(string oldValue, string newValue)
    {
        // Avoid kicking off HTTP/file IO while a JSON document is being
        // deserialized; the view (or document) will request a load once
        // hydration completes via EnsureLoadedAsync.
        if (HydrationScope.IsActive)
        {
            _loadPending = true;
            return;
        }

        _ = LoadBitmapAsync(newValue);
    }

    /// <summary>
    /// Triggers a deferred bitmap load if one was skipped during hydration.
    /// </summary>
    public Task EnsureLoadedAsync()
    {
        if (!_loadPending)
            return Task.CompletedTask;

        _loadPending = false;
        return LoadBitmapAsync(Source);
    }

    partial void OnImageSourceChanged(IImage? oldValue, IImage? newValue)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(HasNoImage));
    }

    partial void OnIsLoadingChanged(bool oldValue, bool newValue)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(HasNoImage));
    }

    partial void OnSizeModeChanged(string oldValue, string newValue)
    {
        OnPropertyChanged(nameof(IsScaleMode));
        OnPropertyChanged(nameof(IsPixelMode));

        if (_syncingSize)
            return;

        if (IsScaleMode)
        {
            var ratioX = _baseWidth > 0 ? PixelWidth / _baseWidth : 1.0;
            var ratioY = _baseHeight > 0 ? PixelHeight / _baseHeight : 1.0;
            SetScaleCore(ratioX > 0 ? ratioX : 1.0, ratioY > 0 ? ratioY : 1.0);
            SyncPixelSizeFromScale();
        }
        else
        {
            SyncPixelSizeFromScale();
        }
    }

    partial void OnScaleXChanged(double oldValue, double newValue)
    {
        if (_syncingSize || !IsScaleMode)
            return;

        if (KeepAspectRatio)
        {
            _syncingSize = true;
            ScaleY = newValue;
            _syncingSize = false;
        }

        SyncPixelSizeFromScale();
    }

    partial void OnScaleYChanged(double oldValue, double newValue)
    {
        if (_syncingSize || !IsScaleMode)
            return;

        if (KeepAspectRatio)
        {
            _syncingSize = true;
            ScaleX = newValue;
            _syncingSize = false;
        }

        SyncPixelSizeFromScale();
    }

    partial void OnPixelWidthChanged(double oldValue, double newValue)
    {
        if (_syncingSize)
            return;

        if (KeepAspectRatio && _baseWidth > 0 && _baseHeight > 0)
        {
            _syncingSize = true;
            PixelHeight = Math.Round(newValue * (_baseHeight / _baseWidth), 2);
            _syncingSize = false;
        }

        if (IsScaleMode && _baseWidth > 0)
            SetScaleCore(newValue / _baseWidth, KeepAspectRatio && _baseWidth > 0 ? newValue / _baseWidth : ScaleY);
    }

    partial void OnPixelHeightChanged(double oldValue, double newValue)
    {
        if (_syncingSize)
            return;

        if (KeepAspectRatio && _baseWidth > 0 && _baseHeight > 0)
        {
            _syncingSize = true;
            PixelWidth = Math.Round(newValue * (_baseWidth / _baseHeight), 2);
            _syncingSize = false;
        }

        if (IsScaleMode && _baseHeight > 0)
            SetScaleCore(KeepAspectRatio && _baseHeight > 0 ? newValue / _baseHeight : ScaleX, newValue / _baseHeight);
    }

    private async Task LoadBitmapAsync(string source)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cts = _loadCts;

        ImageSource = null;
        if (string.IsNullOrWhiteSpace(source))
            return;

        IsLoading = true;
        try
        {
            IImage? image;

            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var response = await _http.GetAsync(source, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);

                if (!cts.IsCancellationRequested)
                {
                    using var imageStream = new MemoryStream(bytes, writable: false);
                    image = LoadImage(imageStream);
                }
                else
                {
                    return;
                }
            }
            else
            {
                var path = source.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(source).LocalPath
                    : source;

                if (!File.Exists(path))
                    return;

                using var fileStream = File.OpenRead(path);
                image = LoadImage(fileStream);
            }

            if (!cts.IsCancellationRequested)
            {
                var imageSize = image?.Size ?? default;
                var baseSize = CalculateBaseSize(imageSize.Width, imageSize.Height);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _baseWidth = baseSize.Width;
                    _baseHeight = baseSize.Height;
                    SetScaleCore(1.0, 1.0);
                    SetPixelSizeCore(baseSize.Width, baseSize.Height);
                    ImageSource = image;
                });
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            _baseWidth = DefaultMaxWidth;
            _baseHeight = DefaultMaxHeight;
            SetScaleCore(1.0, 1.0);
            SetPixelSizeCore(DefaultMaxWidth, DefaultMaxHeight * 0.75);
        }
        finally
        {
            if (!cts.IsCancellationRequested)
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    [VeloxCommand]
    private async Task Browse(object? parameter)
    {
        var storage = parameter switch
        {
            IStorageProvider provider => provider,
            Visual visual => TopLevel.GetTopLevel(visual)?.StorageProvider,
            _ => null
        };
        if (storage is null)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.svg"]
                }
            ]
        }).ConfigureAwait(true);

        if (files.Count == 0)
            return;

        Source = files[0].Path?.AbsoluteUri ?? files[0].Name;
    }

    public void IncreaseScale()
    {
        SetScaleCore(Math.Min(5.0, ScaleX + 0.05), Math.Min(5.0, ScaleY + 0.05));
        SyncPixelSizeFromScale();
    }

    public void DecreaseScale()
    {
        SetScaleCore(Math.Max(0.1, ScaleX - 0.05), Math.Max(0.1, ScaleY - 0.05));
        SyncPixelSizeFromScale();
    }

    public double GetScaledWidth() => PixelWidth;
    public double GetScaledHeight() => PixelHeight;

    public HorizontalAlignment GetHorizontalAlignment() => Alignment switch
    {
        "Left" => HorizontalAlignment.Left,
        "Right" => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center
    };

    public void ResizeByFactor(double factor)
    {
        factor = Math.Max(0.1, factor);

        if (IsScaleMode)
        {
            SetScaleCore(double.Clamp(ScaleX * factor, 0.1, 5.0), double.Clamp(ScaleY * factor, 0.1, 5.0));
            SyncPixelSizeFromScale();
            return;
        }

        var newWidth = Math.Max(16, PixelWidth * factor);
        var newHeight = Math.Max(16, KeepAspectRatio ? PixelHeight * factor : PixelHeight * factor);
        SetPixelSizeCore(newWidth, newHeight);
    }

    private static IImage LoadImage(Stream stream)
    {
        if (IsSvgFile(stream))
        {
            return new SvgImage
            {
                Source = SvgSource.LoadFromStream(stream)
            };
        }

        return new Bitmap(stream);
    }

    private static Size CalculateBaseSize(double width, double height)
    {
        if (width <= 0 || height <= 0)
            return new Size(DefaultMaxWidth, DefaultMaxHeight * 0.75);

        var scale = Math.Min(DefaultMaxWidth / width, DefaultMaxHeight / height);
        if (scale > 1)
            scale = 1;

        return new Size(width * scale, height * scale);
    }

    private void SyncPixelSizeFromScale()
    {
        SetPixelSizeCore(_baseWidth * ScaleX, _baseHeight * ScaleY);
    }

    private void SetPixelSizeCore(double width, double height)
    {
        _syncingSize = true;
        PixelWidth = Math.Round(Math.Max(16, width), 2);
        PixelHeight = Math.Round(Math.Max(16, height), 2);
        _syncingSize = false;
    }

    private void SetScaleCore(double valueX, double valueY)
    {
        _syncingSize = true;
        ScaleX = double.Clamp(valueX, 0.1, 5.0);
        ScaleY = double.Clamp(valueY, 0.1, 5.0);
        _syncingSize = false;
    }

    private static bool IsSvgFile(Stream stream)
    {
        if (stream.Length == 0)
            return false;

        try
        {
            const int bufferSize = 512;
            var length = (int)Math.Min(bufferSize, stream.Length);
            var buffer = new byte[length];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var header = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return header.Contains("<svg", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            stream.Position = 0;
        }
    }
}
