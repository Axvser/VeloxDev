using Avalonia.Threading;
using System.Windows;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
{
    public class UIThreadInspector() : IUIThreadInspector
    {
        public bool IsUIThread() => Dispatcher.UIThread.CheckAccess();
    }
}
