using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.MAUI.PlatformAdapters
{
    public class UIThreadInspector() : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => Application.Current?.Windows?.Count > 0;

        public override bool IsUIThread() => Application.Current?.Dispatcher?.IsDispatchRequired == false;

        public override object? ProtectedGetValue(bool isUIThread, object target, ITransitionProperty property)
        {
            if (isUIThread)
                return property.GetValue(target);

            var tcs = new TaskCompletionSource<object?>();
            Application.Current?.Dispatcher?.Dispatch(() =>
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
                var tcs = new TaskCompletionSource<object?>();
                if (Application.Current?.Dispatcher?.Dispatch(() =>
                {
                    try
                    {
                        action.Invoke();
                        tcs.SetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }) != true)
                {
                    throw new InvalidOperationException("Failed to dispatch work to the MAUI UI thread.");
                }

                tcs.Task.GetAwaiter().GetResult();
            }
        }
    }
}
