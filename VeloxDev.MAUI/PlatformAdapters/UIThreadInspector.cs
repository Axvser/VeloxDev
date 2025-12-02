using System.Reflection;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters
{
    public class UIThreadInspector() : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread()
        {
            return Application.Current?.Dispatcher?.IsDispatchRequired == false;
        }

        public override object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
        {
            if (isUIThread)
                return propertyInfo?.GetValue(target);

            var tcs = new TaskCompletionSource<object?>();
            Application.Current?.Dispatcher?.Dispatch(() =>
            {
                try
                {
                    var value = propertyInfo.GetValue(target);
                    tcs.SetResult(value);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task.GetAwaiter().GetResult();
        }

        public override List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate)
        {
            if (isUIThread)
                return interpolate();

            var tcs = new TaskCompletionSource<List<object?>>();
            Application.Current?.Dispatcher?.Dispatch(() =>
            {
                try
                {
                    tcs.SetResult(interpolate());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task.GetAwaiter().GetResult() ?? [];
        }

        public override void ProtectedInvoke(bool isUIThread, Action action)
        {
            if (isUIThread)
            {
                action.Invoke();
            }
            else
            {
                Application.Current?.Dispatcher?.Dispatch(action);
            }
        }
    }
}
