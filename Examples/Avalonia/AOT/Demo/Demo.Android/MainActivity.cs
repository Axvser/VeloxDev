using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace Demo.Android;

[Activity(
    Label = "Demo.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        /* 保留 Demo 项目中的反射上下文以支持在 AOT 后仍可使用反射 */
        AOTReflection.Init();

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}