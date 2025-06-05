using System.Reflection;
using VeloxDev.WPF.FrameworkSupport;
using VeloxDev.WPF.StructuralDesign.Animator;
using VeloxDev.WPF.TransitionSystem.Basic;

namespace VeloxDev.WPF.TransitionSystem
{
    public sealed class TransitionMeta : IMergeableTransition, ITransitionMeta, IConvertibleTransitionMeta, IExecutableTransition, ITransitionWithTarget, ICompilableTransition
    {
        internal TransitionMeta() { }
        public TransitionMeta(TransitionParams transitionParams, State propertyState)
        {
            TransitionParams = transitionParams;
            PropertyState = propertyState;
        }
        public TransitionMeta(TransitionParams transitionParams, List<List<Tuple<PropertyInfo, List<object?>>>> tuples)
        {
            TransitionParams = transitionParams;
            foreach (var tuple in tuples)
            {
                foreach (var value in tuple)
                {
                    PropertyState.AddProperty(value.Item1.Name, value.Item2.LastOrDefault());
                }
            }
        }
        public TransitionMeta(ITransitionMeta transitionMeta)
        {
            TransitionParams = transitionMeta.TransitionParams;
            PropertyState = transitionMeta.PropertyState;
        }
        public TransitionMeta(params TransitionMeta[] transitionMetas)
        {
            Merge(transitionMetas);
        }

        public WeakReference<object>? TransitionApplied { get; set; }
        public TransitionParams TransitionParams { get; set; } = new();
        public State PropertyState { get; set; } = new State() { StateName = Transition.TempName };
        public TransitionScheduler TransitionScheduler => TransitionApplied == null ? throw new ArgumentNullException(nameof(TransitionApplied), "The metadata is missing the target instance for this transition effect") : TransitionScheduler.CreateUniqueUnit(TransitionApplied);
        public List<List<Tuple<PropertyInfo, List<object?>>>> FrameSequence => TransitionApplied == null ? [] : LinearInterpolation.ComputingFrames(TransitionApplied.GetType(), PropertyState, TransitionApplied, XMath.Clamp((int)TransitionParams.FrameCount, 2, int.MaxValue));

        public ITransitionMeta Merge(ICollection<ITransitionMeta> metas)
        {
#if NET
            var result = IMergeableTransition.MergeMetas(metas);
#endif
#if NETFRAMEWORK
            var result = MetaMergeExtension.MergeMetas(metas);
#endif
            PropertyState = result.PropertyState;
            return result;
        }

        public TransitionMeta ToTransitionMeta()
        {
            return this;
        }
        public State ToState()
        {
            return PropertyState;
        }
        public TransitionBoard<T> ToTransitionBoard<T>() where T : class
        {
            var result = new TransitionBoard<T>
            {
                PropertyState = ToState(),
                TransitionApplied = TransitionApplied,
                TransitionParams = TransitionParams,
            };
            return result;
        }
        public Task Start(object? target = null)
        {
            TransitionApplied = target is null ? null : new WeakReference<object>(target);
            if (TransitionApplied == null) return Task.CompletedTask;
            PropertyState.StateName = Transition.TempName + TransitionScheduler.States.BoardSuffix;
            TransitionApplied.BeginTransition(ToState(), TransitionParams);
            return Task.CompletedTask;
        }
        public void Stop()
        {
            TransitionScheduler.Dispose();
        }
        public IExecutableTransition Compile()
        {
            var copy = new TransitionMeta()
            {
                TransitionApplied = TransitionApplied,
                TransitionParams = TransitionParams.DeepCopy(),
                PropertyState = PropertyState.DeepCopy(),
            };
            return copy;
        }
    }
}
