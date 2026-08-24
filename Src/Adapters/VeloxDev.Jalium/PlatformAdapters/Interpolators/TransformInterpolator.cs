using Jalium.UI.Media;

namespace VeloxDev.Adapters.NativeInterpolators
{
    public class TransformInterpolator : IValueInterpolator
    {
        private sealed class TransformPair
        {
            public Transform? Start { get; set; }
            public Transform? End { get; set; }
        }

        public List<object?> Interpolate(object? start, object? end, int steps, object? options = null)
        {
            var direction = options is RotationDirection d ? d : RotationDirection.Auto;
            var startTransform = NormalizeInput(start);
            var endTransform = NormalizeInput(end);

            // Single-frame (reset, etc.) returns the raw target value.
            if (steps <= 1) return [end];

            var startTransforms = ParseTransforms(startTransform);
            var endTransforms = ParseTransforms(endTransform);
            var transformPairs = CreateTransformPairs(startTransforms, endTransforms);

            var result = new List<object?>(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                result.Add(InterpolateTransformPairs(transformPairs, t, direction));
            }

            result[0] = startTransform;
            result[steps - 1] = endTransform;
            return result;
        }

        private static Transform NormalizeInput(object? input)
        {
            if (input is null || (input is Transform transform && transform.Value.IsIdentity))
            {
                return new TransformGroup();
            }

            return (Transform)input;
        }

        private static List<Transform> ParseTransforms(Transform transform)
        {
            var transforms = new List<Transform>();

            if (transform is TransformGroup group && group.Children.Count > 0)
            {
                var lastOfType = new Dictionary<Type, Transform>();
                foreach (var child in group.Children)
                {
                    if (!child.Value.IsIdentity)
                    {
                        lastOfType[child.GetType()] = child;
                    }
                }

                transforms.AddRange(lastOfType.Values);
            }
            else if (!transform.Value.IsIdentity)
            {
                transforms.Add(transform);
            }

            return transforms;
        }

        private static List<TransformPair> CreateTransformPairs(
            List<Transform> startTransforms, List<Transform> endTransforms)
        {
            var allTypes = startTransforms.Select(t => t.GetType())
                .Union(endTransforms.Select(t => t.GetType()))
                .Distinct()
                .ToList();

            var pairs = new List<TransformPair>();
            foreach (var type in allTypes)
            {
                var start = startTransforms.LastOrDefault(t => t.GetType() == type);
                var end = endTransforms.LastOrDefault(t => t.GetType() == type);
                pairs.Add(new TransformPair { Start = start, End = end });
            }

            return pairs;
        }

        private static Transform InterpolateTransformPairs(
            List<TransformPair> pairs, double t, RotationDirection direction)
        {
            var interpolatedTransforms = new List<Transform>();
            foreach (var pair in pairs)
            {
                var interpolated = InterpolateSingleTransformPair(pair.Start, pair.End, t, direction);
                if (interpolated != null)
                {
                    interpolatedTransforms.Add(interpolated);
                }
            }

            return interpolatedTransforms.Count switch
            {
                0 => new TransformGroup(),
                1 => interpolatedTransforms[0],
                _ => new TransformGroup { Children = [.. interpolatedTransforms] },
            };
        }

        private static Transform? InterpolateSingleTransformPair(Transform? start, Transform? end, double t, RotationDirection direction)
        {
            static Transform GetDefaultTransform(Transform? transform) => transform switch
            {
                TranslateTransform _ => new TranslateTransform(0, 0),
                RotateTransform _ => new RotateTransform(0, 0, 0),
                ScaleTransform _ => new ScaleTransform(1, 1, 0, 0),
                SkewTransform _ => new SkewTransform(0, 0, 0, 0),
                _ => new MatrixTransform(Matrix.Identity),
            };

            start ??= GetDefaultTransform(end);
            end ??= GetDefaultTransform(start);

            if (start.GetType() != end.GetType())
            {
                return new MatrixTransform(LerpMatrix(start.Value, end.Value, t));
            }

            return start switch
            {
                TranslateTransform st when end is TranslateTransform et =>
                    new TranslateTransform(Lerp(st.X, et.X, t), Lerp(st.Y, et.Y, t)),

                RotateTransform st when end is RotateTransform et =>
                    new RotateTransform(
                        LerpAngle(st.Angle, et.Angle, t, direction),
                        Lerp(st.CenterX, et.CenterX, t),
                        Lerp(st.CenterY, et.CenterY, t)),

                ScaleTransform st when end is ScaleTransform et =>
                    new ScaleTransform(
                        Lerp(st.ScaleX, et.ScaleX, t),
                        Lerp(st.ScaleY, et.ScaleY, t),
                        Lerp(st.CenterX, et.CenterX, t),
                        Lerp(st.CenterY, et.CenterY, t)),

                SkewTransform st when end is SkewTransform et =>
                    new SkewTransform(
                        Lerp(st.AngleX, et.AngleX, t),
                        Lerp(st.AngleY, et.AngleY, t),
                        Lerp(st.CenterX, et.CenterX, t),
                        Lerp(st.CenterY, et.CenterY, t)),

                MatrixTransform st when end is MatrixTransform et =>
                    new MatrixTransform(LerpMatrix(st.Matrix, et.Matrix, t)),

                _ => null,
            };
        }

        private static double LerpAngle(double start, double end, double t, RotationDirection direction)
        {
            bool reverse = direction.HasFlag(RotationDirection.CounterClockWise)
                        || direction.HasFlag(RotationDirection.CounterClockWiseZ);
            bool forceClockWise = direction.HasFlag(RotationDirection.ClockWise)
                               || direction.HasFlag(RotationDirection.ClockWiseZ);
            if (reverse)
            {
                return LerpDirectionalAngle(start, end, t, reverse: true);
            }

            if (forceClockWise)
            {
                return LerpDirectionalAngle(start, end, t, reverse: false);
            }

            return Lerp(start, end, t);
        }

        private static double Lerp(double a, double b, double t) => a + t * (b - a);

        private static Matrix LerpMatrix(Matrix m1, Matrix m2, double t) => new(
            Lerp(m1.M11, m2.M11, t),
            Lerp(m1.M12, m2.M12, t),
            Lerp(m1.M21, m2.M21, t),
            Lerp(m1.M22, m2.M22, t),
            Lerp(m1.OffsetX, m2.OffsetX, t),
            Lerp(m1.OffsetY, m2.OffsetY, t));

        private static double LerpDirectionalAngle(double start, double end, double t, bool reverse)
        {
            var delta = (end - start) % 360d;
            if (reverse)
            {
                if (delta > 0d)
                {
                    delta -= 360d;
                }
            }
            else if (delta < 0d)
            {
                delta += 360d;
            }

            return start + delta * t;
        }
    }
}
