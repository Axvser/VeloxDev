using Avalonia;
using Avalonia.Browser;
using System;
using System.Threading.Tasks;
using VeloxDev.Docs;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithSystemFontSource(new Uri("avares://VeloxDev.Docs/Assets/Fonts/msyh.ttf"))
            .WithSystemFontSource(new Uri("avares://VeloxDev.Docs/Assets/Fonts/NotoColorEmoji.ttf"))
            .WithSystemFontSource(new Uri("avares://VeloxDev.Docs/Assets/Fonts/NotoSans-Italic-VariableFont_wdth,wght.ttf"))
            .WithInterFont()
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out", new()
            {
                RenderingMode = [BrowserRenderingMode.WebGL2]
            });

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}