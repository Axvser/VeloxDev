using System.Threading;
using Jalium.UI;
using Jalium.UI.Threading;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : TransitionHostBase<DispatcherPriority>
    {
        public override ThreadRef ThreadFor(object target)
        {
            try
            {
                var dispatcher = target is DispatcherObject dispatcherObject
                    ? dispatcherObject.Dispatcher
                    : Jalium.UI.Application.Current?.Dispatcher
                      ?? Dispatcher.FromThread(Thread.CurrentThread)
                      ?? Dispatcher.MainDispatcher;
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

            dispatcher.BeginInvoke(priority, action);
            return true;
        }
    }
}
