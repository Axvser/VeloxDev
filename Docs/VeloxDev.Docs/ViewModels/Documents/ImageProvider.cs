using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class ImageProvider : IWikiElement
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _loadCts;

    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial string Source { get; set; }
    [VeloxProperty] public partial double Scale { get; set; }
    [VeloxProperty] public partial Bitmap? Bitmap { get; set; }
    [VeloxProperty] public partial bool IsLoading { get; set; }

    public ImageProvider()
    {
        Source = string.Empty;
        Scale = 1.0;
    }

    partial void OnSourceChanged(string oldValue, string newValue) => _ = LoadBitmapAsync(newValue);

    private async Task LoadBitmapAsync(string source)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cts = _loadCts;

        Bitmap = null;
        if (string.IsNullOrWhiteSpace(source))
            return;

        IsLoading = true;
        try
        {
            Bitmap? bitmap;

            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var response = await _http.GetAsync(source, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);

                if (!cts.IsCancellationRequested)
                {
                    using var imageStream = new MemoryStream(bytes, writable: false);
                    bitmap = new Bitmap(imageStream);
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

                bitmap = new Bitmap(path);
            }

            if (!cts.IsCancellationRequested)
                await Dispatcher.UIThread.InvokeAsync(() => Bitmap = bitmap);
        }
        catch (OperationCanceledException) { }
        catch { /* 加载失败时保持 Bitmap = null */ }
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
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]
                }
            ]
        }).ConfigureAwait(true);

        if (files.Count == 0)
            return;

        Source = files[0].Path?.AbsoluteUri ?? files[0].Name;
    }

    public void IncreaseScale() => Scale = Math.Min(3.0, Scale + 0.05);
    public void DecreaseScale() => Scale = Math.Max(0.1, Scale - 0.05);
}
