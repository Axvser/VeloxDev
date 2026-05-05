using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class LinkProvider : IWikiElement
{
    [VeloxProperty] private IWikiElement? parent = null;
    [VeloxProperty] public partial string Text { get; set; }
    [VeloxProperty] public partial string Url { get; set; }

    public LinkProvider()
    {
        Text = string.Empty;
        Url = string.Empty;
    }

    [VeloxCommand]
    private async void Open(object? parameter)
    {
        if (string.IsNullOrWhiteSpace(Url))
            return;

        if (parameter is Avalonia.Visual visual && TopLevel.GetTopLevel(visual)?.Launcher is ILauncher launcher)
            await launcher.LaunchUriAsync(new Uri(Url));
    }
}
