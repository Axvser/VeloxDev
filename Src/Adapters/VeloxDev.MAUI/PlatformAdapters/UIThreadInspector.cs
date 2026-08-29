namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : UIThreadInspectorCore
    {
        public override bool IsAppAlive() => Application.Current?.Windows?.Count > 0;

        public override bool IsUIThread() => Application.Current?.Dispatcher?.IsDispatchRequired == false;

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            if (IsUIThread())
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

        public override void ProtectedInvoke(object target, Action action)
        {
            if (IsUIThread())
            {
                action.Invoke();
                return;
            }

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
