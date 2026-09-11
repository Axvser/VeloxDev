using Microsoft.Maui.Controls.Shapes;
using System.Linq.Expressions;

namespace VeloxDev.TransitionSystem
{
    public class Transition : TransitionCore
    {

    }

    public class Transition<T> : TransitionCore<
        T,
        State,
        TransitionEffect,
        Interpolator,
        UIThreadInspector,
        TransitionInterpreter,
        NonPriority>
        where T : class
    {
        public static Transition<T> Create() => TransitionCore.Create<Transition<T>>();

        public Transition<T> Effect(Action<TransitionEffect> effectSetter)
        {
            return CoreEffect<Transition<T>, TransitionEffect>(effectSetter);
        }
        public Transition<T> Effect(TransitionEffect effect)
        {
            return CoreEffect<Transition<T>, TransitionEffect>(effect);
        }
        public Transition<T> Property<TValue>(Expression<Func<T, TValue>> propertyLambda, TValue newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        public Transition<T> Property(Expression<Func<T, Brush?>> propertyLambda, Brush? newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, Transform?>> propertyLambda, ICollection<Transform> newValue)
        {
            if (newValue is { Count: 1 })
            {
                // 单个 transform 直接赋值以保留运行时类型：包成 TransformGroup 会改变运行时类型，
                // 破坏 ((TranslateTransform)x.RenderTransform).X 这类嵌套路径。仅多个才包。
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
            return this;
        }
        public Transition<T> Property(Expression<Func<T, Point>> propertyLambda, Point newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, PointF>> propertyLambda, PointF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, Thickness>> propertyLambda, Thickness newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, Color?>> propertyLambda, Color? newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, Size>> propertyLambda, Size newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, SizeF>> propertyLambda, SizeF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, Rect>> propertyLambda, Rect newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, RectF>> propertyLambda, RectF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, Shadow?>> propertyLambda, Shadow? newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        public Transition<T> Property(Expression<Func<T, int>> propertyLambda, int newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, double>> propertyLambda, double newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, float>> propertyLambda, float newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, decimal>> propertyLambda, decimal newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Drawing.Point>> propertyLambda, System.Drawing.Point newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Drawing.PointF>> propertyLambda, System.Drawing.PointF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Drawing.Size>> propertyLambda, System.Drawing.Size newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Drawing.SizeF>> propertyLambda, System.Drawing.SizeF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Drawing.Color>> propertyLambda, System.Drawing.Color newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Drawing.Rectangle>> propertyLambda, System.Drawing.Rectangle newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Drawing.RectangleF>> propertyLambda, System.Drawing.RectangleF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

#if !NETSTANDARD2_0
        public Transition<T> Property(Expression<Func<T, System.Numerics.Vector2>> propertyLambda, System.Numerics.Vector2 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Numerics.Vector3>> propertyLambda, System.Numerics.Vector3 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Numerics.Vector4>> propertyLambda, System.Numerics.Vector4 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        public Transition<T> Property(Expression<Func<T, System.Numerics.Quaternion>> propertyLambda, System.Numerics.Quaternion newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
#endif
    }
}
