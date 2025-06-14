using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public class UIThreadInspector() : IUIThreadInspector
    {
        public bool IsUIThread()
        {
            return Dispatcher.GetForCurrentThread()?.IsDispatchRequired == false;
        }
    }
}
