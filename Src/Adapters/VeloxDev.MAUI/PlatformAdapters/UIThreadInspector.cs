using Microsoft.Maui.Dispatching;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : TransitionHostBase<NonPriority>
    {
        public override bool IsAlive => Microsoft.Maui.Controls.Application.Current?.Windows?.Count > 0;

        public override ThreadRef ThreadFor(object target) => ThreadRef.From(DispatcherFor(target));

        internal static IDispatcher? ApplicationDispatcher
        {
            get
            {
                try
                {
                    return Microsoft.Maui.Controls.Application.Current?.Dispatcher;
                }
                catch (Exception)
                {
                    // 应用存在但它的 dispatcher 还没建起来。
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
                    // 还没挂到 handler 上。
                }
            }

            return ApplicationDispatcher;
        }

        protected override bool IsCurrentThread(ThreadRef thread)
            => thread.TryGet<IDispatcher>(out var dispatcher) && !dispatcher.IsDispatchRequired;

        protected override bool PostCore(object target, ThreadRef thread, Action action, NonPriority priority)
        {
            if (!thread.TryGet<IDispatcher>(out var dispatcher)) return false;

            // Dispatch 的返回值本身就是"有没有被接受"。
            return dispatcher.Dispatch(action);
        }
    }
}
