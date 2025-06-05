using System.Linq.Expressions;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using VeloxDev.WPF.FrameworkSupport;
using VeloxDev.WPF.StructuralDesign.Animator;
using VeloxDev.WPF.TransitionSystem.Basic;

namespace VeloxDev.WPF.TransitionSystem
{
    public sealed class TransitionBoard<T> : ITransitionMeta, IMergeableTransition, IConvertibleTransitionMeta, IExecutableTransition, ITransitionWithTarget, ICompilableTransition where T : class
    {
        private WeakReference<object>? _target = null;
        private TransitionParams _params = new();
        private State _propertyState = new() { StateName = Transition.TempName };
        private List<List<Tuple<PropertyInfo, List<object?>>>> _frameSequence = [];

        internal TransitionBoard() { }
        public bool IsPreloaded { get; internal set; } = false;

        public List<List<Tuple<PropertyInfo, List<object?>>>> FrameSequence
        {
            get
            {
                if (!IsPreloaded)
                {
                    _frameSequence = TransitionApplied == null ? [] : LinearInterpolation.ComputingFrames(TransitionApplied.GetType(), PropertyState, TransitionApplied, XMath.Clamp((int)TransitionParams.FrameCount, 2, int.MaxValue));
                    return _frameSequence;
                }
                return _frameSequence;
            }
        }
        public WeakReference<object>? TransitionApplied
        {
            get => _target;
            set
            {
                if (value != _target)
                {
                    _target = value;
                    IsPreloaded = false;
                }
            }
        }
        public TransitionParams TransitionParams
        {
            get => _params;
            set
            {
                if (value.FrameRate != _params.FrameRate)
                {
                    IsPreloaded = false;
                }
                _params = value;
            }
        }
        public State PropertyState
        {
            get => _propertyState;
            set
            {
                IsPreloaded = false;
                _propertyState = value;
            }
        }
        public TransitionScheduler TransitionScheduler => TransitionApplied == null ? throw new ArgumentNullException(nameof(TransitionApplied), "The metadata is missing the target instance for this transition effect") : TransitionScheduler.CreateUniqueUnit(TransitionApplied);
        public Task Start(object? target = null)
        {
            if (target == null)
            {
                if (TransitionApplied?.TryGetTarget(out var value) ?? false)
                {
                    var Machine = TransitionScheduler.CreateUniqueUnit(value);
                    Machine.Dispose();
                    PropertyState.StateName = Transition.TempName + Machine.States.BoardSuffix;
                    Machine.States.Add(PropertyState);
                    Machine.Transition(PropertyState.StateName, TransitionParams, IsPreloaded ? FrameSequence : null);
                }
            }
            else
            {
                TransitionApplied = new WeakReference<object>(target);
                var Machine = TransitionScheduler.CreateUniqueUnit(target);
                Machine.Dispose();
                PropertyState.StateName = Transition.TempName + Machine.States.BoardSuffix;
                Machine.States.Add(PropertyState);
                Machine.Transition(PropertyState.StateName, TransitionParams, IsPreloaded ? FrameSequence : null);
            }
            return Task.CompletedTask;
        }
        public void Stop()
        {
            TransitionScheduler.Dispose();
        }
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
        public State ToState()
        {
            return PropertyState;
        }
        public TransitionBoard<T1> ToTransitionBoard<T1>() where T1 : class
        {
            TransitionMeta transitionMeta = new(this)
            {
                TransitionApplied = TransitionApplied
            };
            return transitionMeta.ToTransitionBoard<T1>();
        }
        public TransitionMeta ToTransitionMeta()
        {
            TransitionMeta transitionMeta = new(this)
            {
                TransitionApplied = TransitionApplied
            };
            return transitionMeta;
        }
        public IExecutableTransition Compile()
        {
            var meta = new TransitionMeta()
            {
                TransitionApplied = TransitionApplied,
                TransitionParams = TransitionParams.DeepCopy(),
                PropertyState = PropertyState.DeepCopy()
            };
            return meta;
        }

        public TransitionScheduler StartIndependently(object? target = null)
        {
            if (target == null)
            {
                if (TransitionApplied == null)
                {
                    throw new ArgumentNullException(nameof(target), "The metadata is missing the target instance for this transition effect");
                }
                else
                {
                    var Machine = TransitionScheduler.CreateIndependentUnit(TransitionApplied);
                    Machine.Dispose();
                    PropertyState.StateName = Transition.TempName + Machine.States.BoardSuffix;
                    Machine.States.Add(PropertyState);
                    Machine.Transition(PropertyState.StateName, TransitionParams, IsPreloaded ? FrameSequence : null);
                    return Machine;
                }
            }
            else
            {
                TransitionApplied = new WeakReference<object>(target);
                var Machine = TransitionScheduler.CreateIndependentUnit(target);
                Machine.Dispose();
                PropertyState.StateName = Transition.TempName + Machine.States.BoardSuffix;
                Machine.States.Add(PropertyState);
                Machine.Transition(PropertyState.StateName, TransitionParams, IsPreloaded ? FrameSequence : null);
                return Machine;
            }
        }

        public TransitionBoard<T> SetProperty(Expression<Func<T, double>> propertyLambda, double newValue)
        {
            if (propertyLambda.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite || property.PropertyType != typeof(double))
                {
                    return this;
                }
                PropertyState.AddProperty(property.Name, (object?)newValue);
            }
            return this;
        }
        public TransitionBoard<T> SetProperty(Expression<Func<T, Brush>> propertyLambda, Brush newValue)
        {
            if (propertyLambda.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite || property.PropertyType != typeof(Brush))
                {
                    return this;
                }
                PropertyState.AddProperty(property.Name, (object?)newValue);
            }
            return this;
        }
        public TransitionBoard<T> SetProperty(Expression<Func<T, Transform>> propertyLambda, ICollection<Transform> newValue)
        {
            if (propertyLambda.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite || property.PropertyType != typeof(Transform))
                {
                    return this;
                }
                newValue ??= [Transform.Identity];
                var value = newValue.Select(t => t.Value).Aggregate(Matrix.Identity, (acc, matrix) => acc * matrix);
                var interpolatedMatrixStr = $"{value.M11},{value.M12},{value.M21},{value.M22},{value.OffsetX},{value.OffsetY}";
                var result = Transform.Parse(interpolatedMatrixStr);
                PropertyState.AddProperty(property.Name, (object?)result);
            }
            return this;
        }
        public TransitionBoard<T> SetProperty(Expression<Func<T, Point>> propertyLambda, Point newValue)
        {
            if (propertyLambda.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite || property.PropertyType != typeof(Point))
                {
                    return this;
                }
                PropertyState.AddProperty(property.Name, (object?)newValue);
            }
            return this;
        }
        public TransitionBoard<T> SetProperty(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue)
        {
            if (propertyLambda.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite || property.PropertyType != typeof(CornerRadius))
                {
                    return this;
                }
                PropertyState.AddProperty(property.Name, (object?)newValue);
            }
            return this;
        }
        public TransitionBoard<T> SetProperty(Expression<Func<T, Thickness>> propertyLambda, Thickness newValue)
        {
            if (propertyLambda.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite || property.PropertyType != typeof(Thickness))
                {
                    return this;
                }
                PropertyState.AddProperty(property.Name, (object?)newValue);
            }
            return this;
        }
        public TransitionBoard<T> SetProperty(Expression<Func<T, IInterpolable>> propertyLambda, IInterpolable newValue)
        {
            if (propertyLambda.Body is MemberExpression propertyExpr)
            {
                var property = propertyExpr.Member as PropertyInfo;
                if (property == null || !property.CanRead || !property.CanWrite || !typeof(IInterpolable).IsAssignableFrom(property.PropertyType))
                {
                    return this;
                }
                PropertyState.AddProperty(property.Name, (object?)newValue);
            }
            return this;
        }

        public TransitionBoard<T> SetCalculator(string propertyName, InterpolationHandler calculator)
        {
            PropertyState.AddCalculator(propertyName, calculator);
            return this;
        }

        public TransitionBoard<T> SetParams(Action<TransitionParams> modifyParams)
        {
            var temp = new TransitionParams();
            modifyParams(temp);
            TransitionParams = temp;
            return this;
        }
        public TransitionBoard<T> SetParams(TransitionParams newParams)
        {
            TransitionParams = newParams;
            return this;
        }
    }
}
