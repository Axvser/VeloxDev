using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace VeloxDev.TransitionSystem
{
    public static class TransitionEx
    {
        public static StateSnapshot<T> Snapshot<T>(this T target, params Expression<Func<T, object?>>[] expressions)
            where T : class
        {
            var snapshot = new StateSnapshot<T>();
            TransitionSnapshotHelper.CaptureSpecific(target, snapshot.GetState(), expressions);
            return snapshot;
        }

        public static StateSnapshot<T> SnapshotAll<T>(this T target, params Expression<Func<T, object?>>[] extraExpressions)
            where T : class
        {
            var snapshot = new StateSnapshot<T>();
            TransitionSnapshotHelper.CaptureAll(target, snapshot.GetState(), static type => Interpolator.TryGetInterpolator(type, out _), extraExpressions);
            return snapshot;
        }

        public static StateSnapshot<T> SnapshotExcept<T>(this T target, params Expression<Func<T, object?>>[] excludedExpressions)
            where T : class
        {
            var snapshot = new StateSnapshot<T>();
            TransitionSnapshotHelper.CaptureAllExcept(target, snapshot.GetState(), static type => Interpolator.TryGetInterpolator(type, out _), excludedExpressions);
            return snapshot;
        }
    }

    public class Transition : TransitionCore
    {

    }

    public class Transition<T> : TransitionCore<T, StateSnapshot<T>>
    {

    }

    public class StateSnapshot<T> : StateSnapshotCore<
        T,
        State,
        TransitionEffect,
        Interpolator,
        UIThreadInspector,
        TransitionInterpreter,
        DispatcherPriority>
    {
        public StateSnapshot<T> Effect(Action<TransitionEffect> effectSetter)
        {
            return CoreEffect<StateSnapshot<T>, TransitionEffect>(effectSetter);
        }
        public StateSnapshot<T> Effect(TransitionEffect effect)
        {
            return CoreEffect<StateSnapshot<T>, TransitionEffect>(effect);
        }
        public StateSnapshot<T> Property<TValue>(Expression<Func<T, TValue>> propertyLambda, TValue newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        public StateSnapshot<T> Property(Expression<Func<T, ITransform?>> propertyLambda, ICollection<Transform> newValue, object? interpolationOptions = null)
        {
            if (newValue is { Count: 1 })
            {
                Transform? single = null;
                foreach (var item in newValue) { single = item; break; }
                state.SetValue(propertyLambda, single);
            }
            else
            {
                var transformGroup = new TransformGroup()
                {
                    Children = [.. newValue]
                };
                state.SetValue(propertyLambda, transformGroup);
            }
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, IBrush?>> propertyLambda, IBrush? value, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, value);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, ITransform?>> propertyLambda, ITransform? value, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, value);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, Thickness>> propertyLambda, Thickness value, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, value);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, Point>> propertyLambda, Point newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, Size>> propertyLambda, Size newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, PixelPoint>> propertyLambda, PixelPoint newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, PixelSize>> propertyLambda, PixelSize newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, PixelRect>> propertyLambda, PixelRect newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, RelativePoint>> propertyLambda, RelativePoint newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, RelativeRect>> propertyLambda, RelativeRect newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, Color>> propertyLambda, Color newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, BoxShadows>> propertyLambda, BoxShadows newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        public StateSnapshot<T> Property(Expression<Func<T, int>> propertyLambda, int newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, double>> propertyLambda, double newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, float>> propertyLambda, float newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, decimal>> propertyLambda, decimal newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Drawing.Point>> propertyLambda, System.Drawing.Point newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Drawing.PointF>> propertyLambda, System.Drawing.PointF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Drawing.Size>> propertyLambda, System.Drawing.Size newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Drawing.SizeF>> propertyLambda, System.Drawing.SizeF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Drawing.Color>> propertyLambda, System.Drawing.Color newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Drawing.Rectangle>> propertyLambda, System.Drawing.Rectangle newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Drawing.RectangleF>> propertyLambda, System.Drawing.RectangleF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

#if !NETSTANDARD2_0
        public StateSnapshot<T> Property(Expression<Func<T, System.Numerics.Vector2>> propertyLambda, System.Numerics.Vector2 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Numerics.Vector3>> propertyLambda, System.Numerics.Vector3 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Numerics.Vector4>> propertyLambda, System.Numerics.Vector4 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public StateSnapshot<T> Property(Expression<Func<T, System.Numerics.Quaternion>> propertyLambda, System.Numerics.Quaternion newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
#endif
    }
}
