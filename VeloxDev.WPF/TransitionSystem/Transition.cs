using System.Linq.Expressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.TransitionSystem
{
    public class Transition<T> : TransitionCore<T, Transition<T>.StateSnapshot>
        where T : class
    {
        public class StateSnapshot : StateSnapshotCore<
            T,
            State,
            TransitionEffect,
            Interpolator,
            UIThreadInspector,
            TransitionInterpreter,
            DispatcherPriority>
        {
            public StateSnapshot Property(Expression<Func<T, double>> propertyLambda, double newValue)
            {
                state.SetValue<T, double>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Brush>> propertyLambda, Brush newValue)
            {
                state.SetValue<T, Brush>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Transform>> propertyLambda, ICollection<Transform> newValue)
            {
                var transformGroup = new TransformGroup()
                {
                    Children = [.. newValue]
                };
                state.SetValue<T, Transform>(propertyLambda, transformGroup);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Point>> propertyLambda, Point newValue)
            {
                state.SetValue<T, Point>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue)
            {
                state.SetValue<T, CornerRadius>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Thickness>> propertyLambda, Thickness newValue)
            {
                state.SetValue<T, Thickness>(propertyLambda, newValue);
                return this;
            }
        }
    }
}
