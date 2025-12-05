using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Core.TransitionSystem;
using Windows.Foundation;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public static class TransitionEx
    {
        public static Transition<T>.StateSnapshot Snapshot<T>(this T target, params Expression<Func<T, object?>>[] expressions)
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
                                                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

                foreach (var property in instanceProperties)
                {
                    var currentValue = property.GetValue(target);
                    snapshot.state.SetValue(property, currentValue);
                }
            }

            return snapshot;
        }

        public static Transition<T>.StateSnapshot SnapshotExcept<T>(this T target, params Expression<Func<T, object?>>[] excludedExpressions)
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
                                        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !excludedProperties.Contains(p));

            foreach (var property in allProperties)
            {
                var currentValue = property.GetValue(target);
                snapshot.state.SetValue(property, currentValue);
            }

            return snapshot;
        }

        private static bool TryGetPropertyFromExpression<T>(Expression<Func<T, object?>> expression, out PropertyInfo? property)
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

            return property != null &&
                Interpolator.TryGetInterpolator(property.PropertyType, out _) &&
                property.GetIndexParameters().Length == 0;
        }
    }

    public class Transition : TransitionCore
    {

    }

    public class Transition<T> : TransitionCore<T, Transition<T>.StateSnapshot>
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
            public StateSnapshot Effect(Action<TransitionEffect> effectSetter)
            {
                return CoreEffect<StateSnapshot, TransitionEffect>(effectSetter);
            }
            public StateSnapshot Effect(TransitionEffect effect)
            {
                return CoreEffect<StateSnapshot, TransitionEffect>(effect);
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
            public StateSnapshot Property(Expression<Func<T, Windows.Foundation.Point>> propertyLambda, Windows.Foundation.Point newValue)
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
            public StateSnapshot Property(Expression<Func<T, Windows.Foundation.Size>> propertyLambda, Windows.Foundation.Size newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Rect>> propertyLambda, Rect newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, GridLength>> propertyLambda, GridLength newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Windows.UI.Color>> propertyLambda, Windows.UI.Color newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, double>> propertyLambda, double newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, float>> propertyLambda, float newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, decimal>> propertyLambda, decimal newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Drawing.Point>> propertyLambda, System.Drawing.Point newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, PointF>> propertyLambda, PointF newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Drawing.Size>> propertyLambda, System.Drawing.Size newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, SizeF>> propertyLambda, SizeF newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Drawing.Color>> propertyLambda, System.Drawing.Color newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Rectangle>> propertyLambda, Rectangle newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, RectangleF>> propertyLambda, RectangleF newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }

#if NETCOREAPP || NETFRAMEWORK || NET
            public StateSnapshot Property(Expression<Func<T, Vector2>> propertyLambda, Vector2 newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Vector3>> propertyLambda, Vector3 newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Vector4>> propertyLambda, Vector4 newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Numerics.Quaternion>> propertyLambda, System.Numerics.Quaternion newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
#endif
        }
    }
}
