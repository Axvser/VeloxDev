using System.Windows;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class UIThreadInspector() : IUIThreadInspector
    {
        public bool IsUIThread() => Application.Current.Dispatcher.CheckAccess();
    }
}
