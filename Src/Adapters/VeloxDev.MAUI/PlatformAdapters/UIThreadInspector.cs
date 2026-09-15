using Microsoft.Maui.Dispatching;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : UIThreadInspectorCore, IUIThreadAffinity
    {
        public override bool IsAppAlive() => Application.Current?.Windows?.Count > 0;

        public override bool IsUIThread() => Application.Current?.Dispatcher?.IsDispatchRequired == false;

        public object? ThreadFor(object target) => DispatcherFor(target);

        internal static IDispatcher? ApplicationDispatcher
        {
            get
            {
                try
                {
                    return Application.Current?.Dispatcher;
                }
                catch (Exception)
                {
                    // The application exists but its dispatcher is not built yet.
                    return null;
                }
            }
        }

        private static IDispatcher? DispatcherFor(object target)
        {
            if (target is BindableObject bindableObject)
            {
                try
                {
                    if (bindableObject.Dispatcher is { } dispatcher) return dispatcher;
                }
                catch (Exception)
                {
                    // Not attached to a handler yet.
                }
            }

            return ApplicationDispatcher;
        }

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            var dispatcher = DispatcherFor(target);
            if (dispatcher is null)
            {
                return IsUIThread() ? property.GetValue(target) : default;
            }

            if (!dispatcher.IsDispatchRequired)
            {
                return property.GetValue(target);
            }

            var tcs = new TaskCompletionSource<object?>();
            if (!dispatcher.Dispatch(() =>
            {
                try
                {
                    var value = property.GetValue(target);
                    tcs.SetResult(value);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
            {
                return default;
            }

            return tcs.Task.GetAwaiter().GetResult();
        }

        public override bool ProtectedInvoke(object target, Action action)
        {
            var dispatcher = DispatcherFor(target);
            if (dispatcher is null)
            {
                if (!IsUIThread()) return false;
                action.Invoke();
                return true;
            }

            if (!dispatcher.IsDispatchRequired)
            {
                action.Invoke();
                return true;
            }

            return dispatcher.Dispatch(action);
        }
    }
}
