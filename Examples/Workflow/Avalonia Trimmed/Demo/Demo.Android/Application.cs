using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using VeloxDev.TimeLine;

namespace Demo.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
            ViewModels.Workflow.AOTReflection.Init();
            VeloxDev.WorkflowSystem.AOTReflection.Init();
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
