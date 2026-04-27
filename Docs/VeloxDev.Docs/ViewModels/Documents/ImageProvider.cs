using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class ImageProvider : IWikiElement
{
    [VeloxProperty] public partial IWikiElement? Parent { get; set; }
    [VeloxProperty] public partial string Source { get; set; }
    [VeloxProperty] public partial double Scale { get; set; }

    public ImageProvider()
    {
        Source = string.Empty;
        Scale = 1.0;
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

    public void IncreaseScale() => Scale = Math.Min(3.0, Scale + 0.05);
    public void DecreaseScale() => Scale = Math.Max(0.1, Scale - 0.05);
}
