using System.Linq.Expressions;
using Jalium.UI;
using Jalium.UI.Media;
using Jalium.UI.Threading;

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
            TransitionInterpreter,
            DispatcherPriority>
        {
            public StateSnapshot Effect(Action<TransitionEffect> effectSetter)
            {
                return CoreEffect<StateSnapshot, TransitionEffect>(effectSetter);
            }

            public StateSnapshot Effect(TransitionEffect effect)
            {
                return CoreEffect<StateSnapshot, TransitionEffect>(effect);
            }

            // Anchor / Size / Offset (all IInterpolable).
            public StateSnapshot Property(Expression<Func<T, IInterpolable?>> propertyLambda, IInterpolable? newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, Brush?>> propertyLambda, Brush? newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, Transform?>> propertyLambda, ICollection<Transform> newValue, object? interpolationOptions = null)
            {
                // A single transform is assigned directly to preserve its runtime type — wrapping it in a
                // TransformGroup would change the runtime type, breaking nested property paths (e.g.
                // ((TranslateTransform)RenderTransform).X depends on the intermediate being a TranslateTransform).
                if (newValue is { Count: 1 })
                {
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

            public StateSnapshot Property(Expression<Func<T, Jalium.UI.Media.Media3D.Transform3D?>> propertyLambda, Jalium.UI.Media.Media3D.Transform3D? newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, Point>> propertyLambda, Point newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, Rect>> propertyLambda, Rect newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, Thickness>> propertyLambda, Thickness newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, CornerRadius>> propertyLambda, CornerRadius newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, Size>> propertyLambda, Size newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, Color>> propertyLambda, Color newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, int>> propertyLambda, int newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, double>> propertyLambda, double newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }

            public StateSnapshot Property(Expression<Func<T, float>> propertyLambda, float newValue, object? interpolationOptions = null)
            {
                state.SetValue(propertyLambda, newValue);
                if (interpolationOptions != null) state.SetOptions(propertyLambda, interpolationOptions);
                return this;
            }
        }
    }
}
