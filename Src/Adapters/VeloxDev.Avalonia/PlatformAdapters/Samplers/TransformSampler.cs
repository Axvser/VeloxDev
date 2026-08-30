using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class TransformSampler : ISampler
    {
        private static readonly Transform Identity = new TransformGroup();

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var direction = options is RotationDirection d ? d : RotationDirection.Auto;
            var startTransform = NormalizeInput(start);
            var endTransform = NormalizeInput(end);

            if (startTransform.GetType() == endTransform.GetType() && startTransform is not TransformGroup)
            {
                // Zero per-frame allocation: reuse a scratch transform, recomputing from the pristine start/end.
                if (working is not Transform wt || wt.GetType() != startTransform.GetType())
                {
                    wt = CloneTransform(startTransform);
                    working = wt;
                }
                MutateInPlace(wt, startTransform, endTransform, direction, t);
                property.SetValue(target, wt);
                return;
            }

            // 1. Unified preprocessing
            // 2. Parse effective transforms
            var startTransforms = ParseTransforms(startTransform);
            var endTransforms = ParseTransforms(endTransform);

            // 3. Create matched pairs
            var transformPairs = CreateTransformPairs(startTransforms, endTransforms);

            // 4. Interpolate at time t
            property.SetValue(target, InterpolateTransformPairs(transformPairs, t, direction));
        }

        private static Transform CloneTransform(Transform source) => source switch
        {
            TranslateTransform t => new TranslateTransform(t.X, t.Y),
            RotateTransform t => new RotateTransform(t.Angle, t.CenterX, t.CenterY),
            ScaleTransform t => new ScaleTransform(t.ScaleX, t.ScaleY),
            SkewTransform t => new SkewTransform(t.AngleX, t.AngleY),
            Rotate3DTransform t => new Rotate3DTransform(t.AngleX, t.AngleY, t.AngleZ, t.CenterX, t.CenterY, t.CenterZ, t.Depth),
            MatrixTransform t => new MatrixTransform(t.Matrix),
            _ => throw new InvalidOperationException($"Unsupported transform type {source.GetType().Name}."),
        };

        private void MutateInPlace(Transform working, Transform start, Transform end, RotationDirection direction, double t)
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
                    break;
                case SkewTransform st when start is SkewTransform s && end is SkewTransform e:
                    st.AngleX = Lerp(s.AngleX, e.AngleX, t);
                    st.AngleY = Lerp(s.AngleY, e.AngleY, t);
                    break;
                case Rotate3DTransform st when start is Rotate3DTransform s && end is Rotate3DTransform e:
                    st.AngleX = LerpAngle(s.AngleX, e.AngleX, t, direction, axis: 'X');
                    st.AngleY = LerpAngle(s.AngleY, e.AngleY, t, direction, axis: 'Y');
                    st.AngleZ = LerpAngle(s.AngleZ, e.AngleZ, t, direction, axis: 'Z');
                    st.CenterX = Lerp(s.CenterX, e.CenterX, t);
                    st.CenterY = Lerp(s.CenterY, e.CenterY, t);
                    st.CenterZ = Lerp(s.CenterZ, e.CenterZ, t);
                    st.Depth = Lerp(s.Depth, e.Depth, t);
                    break;
                case MatrixTransform st when start is MatrixTransform s && end is MatrixTransform e:
                    st.Matrix = LerpMatrix(s.Matrix, e.Matrix, t);
                    break;
            }
        }

        private static Transform NormalizeInput(object? input)
        {
            if (input == null || (input is Transform transform && transform == Identity))
                return new TransformGroup();
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
                    if (child != Identity)
                        lastOfType[child.GetType()] = child;
                }
                transforms.AddRange(lastOfType.Values);
            }
            else if (transform != null && transform != Identity)
            {
                transforms.Add(transform);
            }

            return transforms;
        }

        private static List<(Transform? start, Transform? end)> CreateTransformPairs(
            List<Transform> startTransforms, List<Transform> endTransforms)
        {
            var allTypes = startTransforms.Select(t => t.GetType())
                             .Union(endTransforms.Select(t => t.GetType()))
                             .Distinct()
                             .ToList();

            var pairs = new List<(Transform?, Transform?)>();
            foreach (var type in allTypes)
            {
                var start = startTransforms.LastOrDefault(t => t.GetType() == type);
                var end = endTransforms.LastOrDefault(t => t.GetType() == type);
                pairs.Add((start, end));
            }
            return pairs;
        }

        protected virtual Transform InterpolateTransformPairs(
            List<(Transform? start, Transform? end)> pairs, double t, RotationDirection direction)
        {
            var interpolatedTransforms = new List<Transform>();
            foreach (var (start, end) in pairs)
            {
                var interpolated = InterpolateSingleTransformPair(start, end, t, direction);
                if (interpolated != null)
                    interpolatedTransforms.Add(interpolated);
            }

            return interpolatedTransforms.Count switch
            {
                0 => new TransformGroup(),
                1 => interpolatedTransforms[0],
                _ => new TransformGroup { Children = [.. interpolatedTransforms] }
            };
        }

        protected virtual Transform? InterpolateSingleTransformPair(Transform? start, Transform? end, double t, RotationDirection direction)
        {
            static Transform GetDefaultTransform(Transform? transform) => transform switch
            {
                TranslateTransform _ => new TranslateTransform(0, 0),
                RotateTransform _ => new RotateTransform(0, 0, 0),
                ScaleTransform _ => new ScaleTransform(1, 1),
                SkewTransform _ => new SkewTransform(0, 0),
                Rotate3DTransform _ => new Rotate3DTransform(0, 0, 0, 0, 0, 0, 0),
                _ => new MatrixTransform(Matrix.Identity)
            };

            start ??= GetDefaultTransform(end);
            end ??= GetDefaultTransform(start);

            if (start.GetType() != end.GetType())
            {
                return new MatrixTransform(
                    LerpMatrix(start.Value, end.Value, t));
            }

            return start switch
            {
                TranslateTransform st when end is TranslateTransform et =>
                    new TranslateTransform(
                        Lerp(st.X, et.X, t),
                        Lerp(st.Y, et.Y, t)),

                RotateTransform st when end is RotateTransform et =>
                    new RotateTransform(
                        LerpAngle(st.Angle, et.Angle, t, direction),
                        Lerp(st.CenterX, et.CenterX, t),
                        Lerp(st.CenterY, et.CenterY, t)),

                ScaleTransform st when end is ScaleTransform et =>
                    new ScaleTransform(
                        Lerp(st.ScaleX, et.ScaleX, t),
                        Lerp(st.ScaleY, et.ScaleY, t)),

                SkewTransform st when end is SkewTransform et =>
                    new SkewTransform(
                        Lerp(st.AngleX, et.AngleX, t),
                        Lerp(st.AngleY, et.AngleY, t)),

                Rotate3DTransform st when end is Rotate3DTransform et =>
                    new Rotate3DTransform(
                        LerpAngle(st.AngleX, et.AngleX, t, direction, axis: 'X'),
                        LerpAngle(st.AngleY, et.AngleY, t, direction, axis: 'Y'),
                        LerpAngle(st.AngleZ, et.AngleZ, t, direction, axis: 'Z'),
                        Lerp(st.CenterX, et.CenterX, t),
                        Lerp(st.CenterY, et.CenterY, t),
                        Lerp(st.CenterZ, et.CenterZ, t),
                        Lerp(st.Depth, et.Depth, t)),

                MatrixTransform st when end is MatrixTransform et =>
                    new MatrixTransform(
                        LerpMatrix(st.Matrix, et.Matrix, t)),

                _ => null
            };
        }

        private enum Axis { None, X, Y, Z }

        protected virtual double LerpAngle(double start, double end, double t, RotationDirection direction, char axis = '\0')
        {
            bool reverse = axis switch
            {
                'X' => direction.HasFlag(RotationDirection.CounterClockWiseX),
                'Y' => direction.HasFlag(RotationDirection.CounterClockWiseY),
                'Z' => direction.HasFlag(RotationDirection.CounterClockWiseZ),
                _ => false
            } || direction.HasFlag(RotationDirection.CounterClockWise);

            bool forceClockWise = axis switch
            {
                'X' => direction.HasFlag(RotationDirection.ClockWiseX),
                'Y' => direction.HasFlag(RotationDirection.ClockWiseY),
                'Z' => direction.HasFlag(RotationDirection.ClockWiseZ),
                _ => false
            } || direction.HasFlag(RotationDirection.ClockWise);
            if (reverse)
                return LerpDirectionalAngle(start, end, t, reverse: true);
            if (forceClockWise)
                return LerpDirectionalAngle(start, end, t, reverse: false);
            return Lerp(start, end, t);
        }

        private static double Lerp(double a, double b, double t) => a + t * (b - a);
        private static Matrix LerpMatrix(Matrix m1, Matrix m2, double t)
        {
            return new Matrix(
                Lerp(m1.M11, m2.M11, t),
                Lerp(m1.M12, m2.M12, t),
                Lerp(m1.M21, m2.M21, t),
                Lerp(m1.M22, m2.M22, t),
                Lerp(m1.M31, m2.M31, t),
                Lerp(m1.M32, m2.M32, t));
        }

        protected static double LerpDirectionalAngle(double start, double end, double t, bool reverse)
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