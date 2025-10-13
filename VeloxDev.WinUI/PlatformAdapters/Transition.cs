using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using VeloxDev.Core.TransitionSystem;
using Windows.Foundation;

namespace VeloxDev.WinUI.PlatformAdapters
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
            DispatcherQueuePriority>
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

            public StateSnapshot Property(Expression<Func<T, Projection>> propertyLambda, Projection newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
        }
    }
}
