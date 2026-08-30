using Jalium.UI.Media;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class TransformSampler : ISampler
    {
        private sealed class TransformPair
        {
            public Transform? Start { get; set; }
            public Transform? End { get; set; }
        }

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var direction = options is RotationDirection d ? d : RotationDirection.Auto;

            if (start is Transform startT && end is Transform endT
                && startT.GetType() == endT.GetType()
                && startT.GetType() != typeof(TransformGroup)
                && IsKnownTransform(startT))
            {
                // Zero per-frame allocation: reuse a scratch transform, recomputing from the pristine start/end each frame.
                if (working is not Transform wt || wt.GetType() != startT.GetType())
                {
                    wt = CloneTransform(startT);
                    working = wt;
                }
                MutateInPlace(wt, startT, endT, direction, t);
                property.SetValue(target, wt);
                return;
            }

            // Group / type mismatch / unknown / null → allocate a fresh transform (start/end are never mutated).
            property.SetValue(target, Compute(start, end, direction, t));
        }

        private static bool IsKnownTransform(Transform transform) => transform is
            TranslateTransform or RotateTransform or ScaleTransform or SkewTransform or MatrixTransform;

        private static Transform CloneTransform(Transform source) => source switch
        {
            TranslateTransform t => new TranslateTransform(t.X, t.Y),
            RotateTransform t => new RotateTransform(t.Angle, t.CenterX, t.CenterY),
            ScaleTransform t => new ScaleTransform(t.ScaleX, t.ScaleY, t.CenterX, t.CenterY),
            SkewTransform t => new SkewTransform(t.AngleX, t.AngleY, t.CenterX, t.CenterY),
            MatrixTransform t => new MatrixTransform(t.Matrix),
            _ => throw new InvalidOperationException($"Unsupported transform type {source.GetType().Name}."),
        };

        private static void MutateInPlace(Transform working, Transform start, Transform end, RotationDirection direction, double t)
        {
            switch (working)
            {
                case TranslateTransform st when start is TranslateTransform s && end is TranslateTransform e:
                    st.X = Lerp(s.X, e.X, t);
                    st.Y = Lerp(s.Y, e.Y, t);
                    break;
                case RotateTransform st when start is RotateTransform s && end is RotateTransform e:
                    st.Angle = LerpAngle(s.Angle, e.Angle, t, direction);
                    st.CenterX = Lerp(s.CenterX, e.CenterX, t);
                    st.CenterY = Lerp(s.CenterY, e.CenterY, t);
                    break;
                case ScaleTransform st when start is ScaleTransform s && end is ScaleTransform e:
                    st.ScaleX = Lerp(s.ScaleX, e.ScaleX, t);
                    st.ScaleY = Lerp(s.ScaleY, e.ScaleY, t);
                    st.CenterX = Lerp(s.CenterX, e.CenterX, t);
                    st.CenterY = Lerp(s.CenterY, e.CenterY, t);
                    break;
                case SkewTransform st when start is SkewTransform s && end is SkewTransform e:
                    st.AngleX = Lerp(s.AngleX, e.AngleX, t);
                    st.AngleY = Lerp(s.AngleY, e.AngleY, t);
                    st.CenterX = Lerp(s.CenterX, e.CenterX, t);
                    st.CenterY = Lerp(s.CenterY, e.CenterY, t);
                    break;
                case MatrixTransform st when start is MatrixTransform s && end is MatrixTransform e:
                    st.Matrix = LerpMatrix(s.Matrix, e.Matrix, t);
                    break;
            }
        }

        private static Transform Compute(object? start, object? end, RotationDirection direction, double t)
        {
            var startTransform = NormalizeInput(start);
            var endTransform = NormalizeInput(end);

            var startTransforms = ParseTransforms(startTransform);
            var endTransforms = ParseTransforms(endTransform);
            var transformPairs = CreateTransformPairs(startTransforms, endTransforms);

            return InterpolateTransformPairs(transformPairs, t, direction);
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
