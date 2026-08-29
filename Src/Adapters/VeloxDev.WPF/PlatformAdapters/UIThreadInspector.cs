using System.Windows;
using System.Windows.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : UIThreadInspectorCore<DispatcherPriority>
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread() => Application.Current?.Dispatcher?.CheckAccess() ?? default;

        /// <summary>
        /// The target object (<see cref="DispatcherObject"/>) takes priority: a UI element carries its owning
        /// <see cref="Dispatcher"/>, so it can be marshaled directly from any thread; otherwise fall back to <see cref="Application.Current"/>.
        /// </summary>
        private static Dispatcher? DispatcherFor(object target)
            => target is DispatcherObject dispatcherObject ? dispatcherObject.Dispatcher : Application.Current?.Dispatcher;

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            var dispatcher = DispatcherFor(target);
            if (dispatcher == null) return IsUIThread() ? property.GetValue(target) : default;
            if (dispatcher.CheckAccess()) return property.GetValue(target);
            return dispatcher.Invoke(() => property.GetValue(target));
        }

        public override void ProtectedInvoke(object target, Action action, DispatcherPriority priority)
        {
            var dispatcher = DispatcherFor(target);
            if (dispatcher == null)
            {
                if (IsUIThread()) action();
                return;
            }
            if (dispatcher.CheckAccess()) { action(); return; }
            dispatcher.InvokeAsync(action, priority);
        }
    }
}
