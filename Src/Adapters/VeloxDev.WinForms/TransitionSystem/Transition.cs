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

        /// <summary>Sets the <c>Padding</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, Padding>> propertyLambda, Padding newValue, object? interpolationOptions = null)
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
        /// <summary>Sets the <c>decimal</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, decimal>> propertyLambda, decimal newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Drawing.Point</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Drawing.Point>> propertyLambda, System.Drawing.Point newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Drawing.PointF</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Drawing.PointF>> propertyLambda, System.Drawing.PointF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Drawing.Size</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Drawing.Size>> propertyLambda, System.Drawing.Size newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Drawing.SizeF</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Drawing.SizeF>> propertyLambda, System.Drawing.SizeF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Drawing.Color</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Drawing.Color>> propertyLambda, System.Drawing.Color newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Drawing.Rectangle</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Drawing.Rectangle>> propertyLambda, System.Drawing.Rectangle newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Drawing.RectangleF</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Drawing.RectangleF>> propertyLambda, System.Drawing.RectangleF newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }

#if !NETSTANDARD2_0
        /// <summary>Sets the <c>System.Numerics.Vector2</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Numerics.Vector2>> propertyLambda, System.Numerics.Vector2 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Numerics.Vector3</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Numerics.Vector3>> propertyLambda, System.Numerics.Vector3 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Numerics.Vector4</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Numerics.Vector4>> propertyLambda, System.Numerics.Vector4 newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
        /// <summary>Sets the <c>System.Numerics.Quaternion</c> property selected by <paramref name="propertyLambda"/> to <paramref name="newValue"/>.</summary>
        public Transition<T> Property(Expression<Func<T, System.Numerics.Quaternion>> propertyLambda, System.Numerics.Quaternion newValue, object? interpolationOptions = null)
        {
            state.SetValue(propertyLambda, newValue);
            if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
            return this;
        }
#endif
    }
}
