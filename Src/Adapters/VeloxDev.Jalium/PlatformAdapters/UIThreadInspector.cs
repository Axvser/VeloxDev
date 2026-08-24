using Jalium.UI;
using Jalium.UI.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : UIThreadInspectorCore<DispatcherPriority>
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread()
            => Dispatcher.MainDispatcher?.CheckAccess() ?? Application.Current?.Dispatcher?.CheckAccess() ?? default;

        /// <summary>The target object (a DispatcherObject like a UI element) takes priority — it owns a
        /// Dispatcher; otherwise fall back to Application.Current, then the static main dispatcher so even
        /// POCO targets marshal correctly.</summary>
        private static Dispatcher? DispatcherFor(object target)
            => target is DispatcherObject dispatcherObject ? dispatcherObject.Dispatcher
               : Application.Current?.Dispatcher ?? Dispatcher.MainDispatcher;

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            var dispatcher = DispatcherFor(target);
            if (dispatcher == null) return IsUIThread() ? property.GetValue(target) : default;
            if (dispatcher.CheckAccess()) return property.GetValue(target);
            return dispatcher.Invoke(() => property.GetValue(target));
        }

        public override List<object?> ProtectedInterpolate(object target, Func<List<object?>> interpolate)
        {
            var dispatcher = DispatcherFor(target);
            if (dispatcher == null) return IsUIThread() ? interpolate() : [];
            if (dispatcher.CheckAccess()) return interpolate();
            return dispatcher.Invoke(interpolate) ?? [];
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
            dispatcher.BeginInvoke(priority, action);
        }
    }
}
