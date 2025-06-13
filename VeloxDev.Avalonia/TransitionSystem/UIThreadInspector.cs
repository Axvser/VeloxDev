using Avalonia.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
{
    public class UIThreadInspector() : IUIThreadInspector
    {
        public bool IsUIThread() => Dispatcher.UIThread.CheckAccess();
    }
}
