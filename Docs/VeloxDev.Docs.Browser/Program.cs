using Avalonia;
using Avalonia.Browser;
using System;
using System.Threading.Tasks;
using VeloxDev.Docs;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithSystemFontSource(new Uri("avares://VeloxDev.Docs/Assets/Fonts/msyh.ttf#Microsoft YaHei"))
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}