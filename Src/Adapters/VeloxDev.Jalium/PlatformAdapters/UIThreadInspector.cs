using System.Threading;
using Jalium.UI;
using Jalium.UI.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : UIThreadInspectorCore<DispatcherPriority>, IUIThreadAffinity
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread()
            => Application.Current?.Dispatcher?.CheckAccess() ?? Dispatcher.MainDispatcher?.CheckAccess() ?? false;

        public object? ThreadFor(object target)
            => target is DispatcherObject dispatcherObject ? dispatcherObject.Dispatcher
               : Application.Current?.Dispatcher
                 ?? Dispatcher.FromThread(Thread.CurrentThread)
                 ?? Dispatcher.MainDispatcher;

        private Dispatcher? DispatcherFor(object target) => (Dispatcher?)ThreadFor(target);

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            var dispatcher = DispatcherFor(target);
            if (dispatcher == null) return IsUIThread() ? property.GetValue(target) : default;
            if (dispatcher.CheckAccess()) return property.GetValue(target);
            if (dispatcher.HasShutdownStarted) return default;
            return dispatcher.Invoke(() => property.GetValue(target));
        }

        public override bool ProtectedInvoke(object target, Action action, DispatcherPriority priority)
        {
            var dispatcher = DispatcherFor(target);
            if (dispatcher == null)
            {
                if (!IsUIThread()) return false;
                action();
                return true;
            }

            if (dispatcher.CheckAccess()) { action(); return true; }
            if (dispatcher.HasShutdownStarted) return false;
            dispatcher.BeginInvoke(priority, action);
            return true;
        }
    }
}
