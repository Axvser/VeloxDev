using System;
using Avalonia.Threading;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : TransitionHostBase<DispatcherPriority>
    {
        public override ThreadRef ThreadFor(object target) => ThreadRef.From(Dispatcher.UIThread);

        protected override bool IsCurrentThread(ThreadRef thread)
            => thread.TryGet<Dispatcher>(out var dispatcher) && dispatcher.CheckAccess();

        protected override DispatcherPriority InternalPriority => DispatcherPriority.Send;

        protected override bool PostCore(object target, ThreadRef thread, Action action, DispatcherPriority priority)
        {
            if (!thread.TryGet<Dispatcher>(out var dispatcher)) return false;

            dispatcher.InvokeAsync(action, priority);
            return true;
        }
    }
}
