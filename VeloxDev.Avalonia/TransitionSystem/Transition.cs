using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
{
    public static class Transition
    {
        public static StateSnapshot<T> Create<T>()
            where T : class
        {
            return new StateSnapshot<T>();
        }
        public static StateSnapshot<T> Create<T>(T target)
            where T : class
        {
            return new StateSnapshot<T>() { targetref = new(target) };
        }

        public class StateSnapshot<T> : State
            where T : class
        {
            internal WeakReference<T>? targetref = null;

            public StateSnapshot<T> SetProperty(Expression<Func<T, Brush>> propertyLambda, Brush newValue)
            {
                SetValue<T, Brush>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> SetProperty(Expression<Func<T, Transform>> propertyLambda, ICollection<Transform> newValue)
            {
                var transformGroup = new TransformGroup()
                {
                    Children = [.. newValue]
                };
                SetValue<T, Transform>(propertyLambda, transformGroup);
                return this;
            }
            public StateSnapshot<T> SetProperty(Expression<Func<T, Point>> propertyLambda, Point newValue)
            {
                SetValue<T, Point>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> SetProperty(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue)
            {
                SetValue<T, CornerRadius>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> SetProperty(Expression<Func<T, Thickness>> propertyLambda, Thickness newValue)
            {
                SetValue<T, Thickness>(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot<T> SetProperty<TInterpolable>(Expression<Func<T, TInterpolable>> propertyLambda, TInterpolable newValue)
                where TInterpolable : IInterpolable
            {
                SetValue<T, TInterpolable>(propertyLambda, newValue);
                return this;
            }
        }
    }
}
