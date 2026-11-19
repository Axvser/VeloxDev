using System;
using Avalonia.Threading;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : TransitionHostBase<DispatcherPriority>
    {
        /// <inheritdoc />
        public override ThreadRef ThreadFor(object target) => ThreadRef.From(Dispatcher.UIThread);

        /// <inheritdoc />
        protected override bool IsCurrentThread(ThreadRef thread)
            => thread.TryGet<Dispatcher>(out var dispatcher) && dispatcher.CheckAccess();

        /// <inheritdoc />
        protected override DispatcherPriority InternalPriority => DispatcherPriority.Send;

        /// <inheritdoc />
        protected override bool PostCore(object target, ThreadRef thread, Action action, DispatcherPriority priority)
        {
            if (!thread.TryGet<Dispatcher>(out var dispatcher)) return false;

            dispatcher.InvokeAsync(action, priority);
            return true;
        }
    }
}
