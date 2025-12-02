using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters
{
    public static class TransitionEx
    {
        public static Transition<T>.StateSnapshot Snapshot<T>(this T target, params Expression<Func<T, object>>[] expressions)
            where T : class
        {
            var snapshot = new Transition<T>.StateSnapshot();

            if (expressions?.Length > 0)
            {
                // 拍摄指定属性
                foreach (var expression in expressions)
                {
                    if (TryGetPropertyFromExpression(expression, out var property) && property!.CanRead && property.CanWrite)
                    {
                        var currentValue = property.GetValue(target);
                        snapshot.state.SetValue(property, currentValue);
                    }
                }
            }
            else
            {
                // 拍摄所有实例属性
                var instanceProperties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                .Where(p => p.CanRead && p.CanWrite);

                foreach (var property in instanceProperties)
                {
                    var currentValue = property.GetValue(target);
                    snapshot.state.SetValue(property, currentValue);
                }
            }

            return snapshot;
        }

        public static Transition<T>.StateSnapshot SnapshotExcept<T>(this T target, params Expression<Func<T, object>>[] excludedExpressions)
            where T : class
        {
            var snapshot = new Transition<T>.StateSnapshot();
            var excludedProperties = new HashSet<PropertyInfo>();

            // 获取排除的属性
            if (excludedExpressions?.Length > 0)
            {
                foreach (var expression in excludedExpressions)
                {
                    if (TryGetPropertyFromExpression(expression, out var property))
                    {
                        excludedProperties.Add(property!);
                    }
                }
            }

            // 拍摄除排除属性外的所有属性
            var allProperties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(p => p.CanRead && p.CanWrite && !excludedProperties.Contains(p));

            foreach (var property in allProperties)
            {
                var currentValue = property.GetValue(target);
                snapshot.state.SetValue(property, currentValue);
            }

            return snapshot;
        }

        private static bool TryGetPropertyFromExpression<T>(Expression<Func<T, object>> expression, out PropertyInfo? property)
            where T : class
        {
            property = null;

            if (expression.Body is MemberExpression memberExpr)
            {
                property = memberExpr.Member as PropertyInfo;
            }
            else if (expression.Body is UnaryExpression unaryExpr &&
                     unaryExpr.Operand is MemberExpression unaryMemberExpr)
            {
                property = unaryMemberExpr.Member as PropertyInfo;
            }

            return property != null;
        }
    }

    public class Transition : TransitionCore
    {

    }

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
            public StateSnapshot Property(Expression<Func<T, IBrush?>> propertyLambda, IBrush newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, ITransform?>> propertyLambda, ICollection<Transform> newValue)
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
