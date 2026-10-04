using System.Linq.Expressions;
using Jalium.UI;
using Jalium.UI.Media;
using Jalium.UI.Threading;

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
        DispatcherPriority>
        where T : class
    {
        /// <summary>Creates an empty transition for <typeparamref name="T"/>.</summary>
        public static Transition<T> Create() => TransitionCore.Create<Transition<T>>();

        /// <summary>Configures the transition effect through <paramref name="effectSetter"/>.</summary>
        public Transition<T> Effect(Action<TransitionEffect> effectSetter)
        {
            return CoreEffect<Transition<T>, TransitionEffect>(effectSetter);
        }

        /// <summary>Uses <paramref name="effect"/> as the transition effect.</summary>
        public Transition<T> Effect(TransitionEffect effect)
        {
            return CoreEffect<Transition<T>, TransitionEffect>(effect);
        }

        /// <summary>Sets the property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property<TValue>(Expression<Func<T, TValue>> propertyLambda, TValue newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>Brush?</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Brush?>> propertyLambda, Brush? newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>Transform?</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Transform?>> propertyLambda, ICollection<Transform> newValue, object? interpolationOptions = null)
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
                var transformGroup = new TransformGroup() { Children = [.. newValue] };
                state.SetValue(propertyLambda, transformGroup);
            }

            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>Jalium.UI.Media.Media3D.Transform3D?</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Jalium.UI.Media.Media3D.Transform3D?>> propertyLambda, Jalium.UI.Media.Media3D.Transform3D? newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>Point</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Point>> propertyLambda, Point newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>Rect</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Rect>> propertyLambda, Rect newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>Thickness</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Thickness>> propertyLambda, Thickness newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>CornerRadius</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>Size</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Size>> propertyLambda, Size newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>Color</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Color>> propertyLambda, Color newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>int</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, int>> propertyLambda, int newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>double</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, double>> propertyLambda, double newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

        /// <summary>Sets the <c>float</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, float>> propertyLambda, float newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
    }
}
