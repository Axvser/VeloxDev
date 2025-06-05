using VeloxDev.WPF.StructuralDesign.Animator;

namespace VeloxDev.WPF.TransitionSystem
{
    public static class TransitionExtension
    {
        public static TransitionBoard<T> Transition<T>(this T element) where T : class
        {
            TransitionBoard<T> tempStoryBoard = new()
            {
                TransitionApplied = element is null ? null : new WeakReference<object>(element),
            };
            return tempStoryBoard;
        }
        public static TransitionScheduler[] BeginTransitions<T1, T2>(this T1 source, params TransitionBoard<T2>[] transitions) where T1 : class where T2 : class
        {
            return [.. transitions.Select(t => t.StartIndependently(source))];
        }
        public static IExecutableTransition BeginTransition<T>(this T source, IExecutableTransition executable) where T : class
        {
            executable.Start(source);
            return executable;
        }
        public static IExecutableTransition BeginTransition<T1, T2>(this T1 source, T2 transfer) where T1 : class where T2 : class, ITransitionMeta
        {
            var result = TransitionSystem.Transition.Compile([transfer], transfer.TransitionParams, source);
            result.Start();
            return result;
        }
        public static IExecutableTransition BeginTransition<T1, T2>(this T1 source, T2 state, Action<TransitionParams> set) where T1 : class where T2 : class, ITransitionMeta
        {
            TransitionSystem.Transition.Dispose(source);
            var param = new TransitionParams();
            set.Invoke(param);
            var result = TransitionSystem.Transition.Compile([state], param, source);
            result.Start();
            return result;

        }
        public static IExecutableTransition BeginTransition<T1, T2>(this T1 source, T2 state, TransitionParams param) where T1 : class where T2 : class, ITransitionMeta
        {
            TransitionSystem.Transition.Dispose(source);
            var result = TransitionSystem.Transition.Compile([state], param, source);
            result.Start();
            return result;
        }
        public static TransitionScheduler? FindTransitionScheduler<T>(this T source) where T : class
        {
            if (TransitionScheduler.TryGetScheduler(source, out var Scheduler))
            {
                return Scheduler;
            }
            return null;
        }
    }
}
