using Microsoft.Maui.Controls.Shapes;
using System.Linq.Expressions;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.TransitionSystem
{
    public static class TransitionEx
    {
        public static Transition<T>.StateSnapshot Snapshot<T>(this T target, params Expression<Func<T, object?>>[] expressions)
            where T : class
        {
            var snapshot = new Transition<T>.StateSnapshot();
            TransitionSnapshotHelper.CaptureSpecific(target, snapshot.GetState(), expressions);
            return snapshot;
        }

        public static Transition<T>.StateSnapshot SnapshotAll<T>(this T target, params Expression<Func<T, object?>>[] extraExpressions)
            where T : class
        {
            var snapshot = new Transition<T>.StateSnapshot();
            TransitionSnapshotHelper.CaptureAll(target, snapshot.GetState(), static type => Interpolator.TryGetInterpolator(type, out _), extraExpressions);
            return snapshot;
        }

        public static Transition<T>.StateSnapshot SnapshotExcept<T>(this T target, params Expression<Func<T, object?>>[] excludedExpressions)
            where T : class
        {
            var snapshot = new Transition<T>.StateSnapshot();
            TransitionSnapshotHelper.CaptureAllExcept(target, snapshot.GetState(), static type => Interpolator.TryGetInterpolator(type, out _), excludedExpressions);
            return snapshot;
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
            TransitionInterpreter>
        {
            public StateSnapshot Effect(Action<TransitionEffect> effectSetter)
            {
                return CoreEffect<StateSnapshot, TransitionEffect>(effectSetter);
            }
            public StateSnapshot Effect(TransitionEffect effect)
            {
                return CoreEffect<StateSnapshot, TransitionEffect>(effect);
            }
            public StateSnapshot Property(Expression<Func<T, IInterpolable?>> propertyLambda, IInterpolable? newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, Brush?>> propertyLambda, Brush? newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Transform?>> propertyLambda, ICollection<Transform> newValue)
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
            public StateSnapshot Property(Expression<Func<T, PointF>> propertyLambda, PointF newValue)
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
            public StateSnapshot Property(Expression<Func<T, Color?>> propertyLambda, Color? newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Size>> propertyLambda, Size newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, SizeF>> propertyLambda, SizeF newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Rect>> propertyLambda, Rect newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, RectF>> propertyLambda, RectF newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, Shadow?>> propertyLambda, Shadow? newValue)
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
            public StateSnapshot Property(Expression<Func<T, System.Drawing.PointF>> propertyLambda, System.Drawing.PointF newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Drawing.Size>> propertyLambda, System.Drawing.Size newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Drawing.SizeF>> propertyLambda, System.Drawing.SizeF newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Drawing.Color>> propertyLambda, System.Drawing.Color newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Drawing.Rectangle>> propertyLambda, System.Drawing.Rectangle newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Drawing.RectangleF>> propertyLambda, System.Drawing.RectangleF newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }

#if NETCOREAPP || NETFRAMEWORK || NET
            public StateSnapshot Property(Expression<Func<T, System.Numerics.Vector2>> propertyLambda, System.Numerics.Vector2 newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Numerics.Vector3>> propertyLambda, System.Numerics.Vector3 newValue)
            {
                state.SetValue(propertyLambda, newValue);
                return this;
            }
            public StateSnapshot Property(Expression<Func<T, System.Numerics.Vector4>> propertyLambda, System.Numerics.Vector4 newValue)
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
