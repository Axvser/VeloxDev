using System.Linq.Expressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters
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
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Brush>> propertyLambda, Brush newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Transform>> propertyLambda, ICollection<Transform> newValue)
            {
                var transformGroup = new TransformGroup()
                {
                    Children = [.. newValue]
                };
                state.SetValue(propertyLambda, transformGroup);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Point>> propertyLambda, Point newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Thickness>> propertyLambda, Thickness newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
        }
    }
}
