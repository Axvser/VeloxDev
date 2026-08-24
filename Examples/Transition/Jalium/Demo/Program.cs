using Jalium.UI;

namespace Demo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
        {
            Args = args,
            DisableDefaults = true,
        });

        builder.ConfigureApplication(app =>
        {
            app.MainWindow = new MainWindow();
        });

        using var host = builder.Build();
        return host.Run();
    }
}
