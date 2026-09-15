using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : TransitionHostBase<DispatcherPriority>
    {
        public override ThreadRef ThreadFor(object target)
        {
            try
            {
                var dispatcher = target is DispatcherObject dispatcherObject
                    ? dispatcherObject.Dispatcher
                    : System.Windows.Application.Current?.Dispatcher ?? Dispatcher.FromThread(Thread.CurrentThread);
                return ThreadRef.From(dispatcher);
            }
            catch (Exception)
            {
                // 每帧每属性被调用，抛异常与动画失败无法区分。None 只让这个目标失去 pacer。
                return ThreadRef.None;
            }
        }

        protected override bool IsCurrentThread(ThreadRef thread)
            => thread.TryGet<Dispatcher>(out var dispatcher) && dispatcher.CheckAccess();

        protected override DispatcherPriority InternalPriority => DispatcherPriority.Send;

        protected override bool PostCore(object target, ThreadRef thread, Action action, DispatcherPriority priority)
        {
            if (!thread.TryGet<Dispatcher>(out var dispatcher)) return false;
            if (dispatcher.HasShutdownStarted) return false;

            dispatcher.InvokeAsync(action, priority);
            return true;
        }
    }
}
