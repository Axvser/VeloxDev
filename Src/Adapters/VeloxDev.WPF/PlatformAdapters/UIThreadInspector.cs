using System.Windows;
using System.Windows.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : UIThreadInspectorCore<DispatcherPriority>
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread() => Application.Current?.Dispatcher?.CheckAccess() ?? default;

        /// <summary>
        /// 目标对象（<see cref="DispatcherObject"/>)优先：UI 元素携带它所属的
        /// <see cref="Dispatcher"/>，从任意线程可直接编组；否则回退到 <see cref="Application.Current"/>。
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
            dispatcher.InvokeAsync(action, priority);
        }
    }
}
