using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class TransformSampler : ISampler
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static readonly TransformGroup Identity = new();

        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var s = Normalize(start);
            var e = Normalize(end);

            if (s.GetType() == e.GetType() && s is not TransformGroup)
            {
                // Zero per-frame allocation: reuse a scratch transform, recomputing from the pristine start/end.
                if (working is not Transform wt || wt.GetType() != s.GetType())
                {
                    wt = CloneTransform(s);
                    working = wt;
                }
                MutateInPlace(wt, s, e, t);
                property.SetValue(target, wt);
                return;
            }

            var startList = ExtractTransforms(s);
            var endList = ExtractTransforms(e);
            var pairs = MatchPairs(startList, endList);

            property.SetValue(target, CombineTransforms(pairs, t));
        }

        private static Transform CloneTransform(Transform source) => source switch
        {
            TranslateTransform t => new TranslateTransform { X = t.X, Y = t.Y },
            ScaleTransform t => new ScaleTransform { ScaleX = t.ScaleX, ScaleY = t.ScaleY },
            RotateTransform t => new RotateTransform { Angle = t.Angle, CenterX = t.CenterX, CenterY = t.CenterY },
            SkewTransform t => new SkewTransform { AngleX = t.AngleX, AngleY = t.AngleY },
            MatrixTransform t => new MatrixTransform { Matrix = t.Matrix },
            _ => throw new InvalidOperationException($"Unsupported transform type {source.GetType().Name}."),
        };

        private void MutateInPlace(Transform working, Transform start, Transform end, double t)
        {
            switch (working)
            {
                case TranslateTransform st when start is TranslateTransform s && end is TranslateTransform e:
                    st.X = Lerp(s.X, e.X, t);
                    st.Y = Lerp(s.Y, e.Y, t);
                    break;
                case ScaleTransform st when start is ScaleTransform s && end is ScaleTransform e:
                    st.ScaleX = Lerp(s.ScaleX, e.ScaleX, t);
                    st.ScaleY = Lerp(s.ScaleY, e.ScaleY, t);
                    break;
                case RotateTransform st when start is RotateTransform s && end is RotateTransform e:
                    st.Angle = LerpAngle(s.Angle, e.Angle, t);
                    st.CenterX = Lerp(s.CenterX, e.CenterX, t);
                    st.CenterY = Lerp(s.CenterY, e.CenterY, t);
                    break;
                case SkewTransform st when start is SkewTransform s && end is SkewTransform e:
                    st.AngleX = Lerp(s.AngleX, e.AngleX, t);
                    st.AngleY = Lerp(s.AngleY, e.AngleY, t);
                    break;
                case MatrixTransform st when start is MatrixTransform s && end is MatrixTransform e:
                    st.Matrix = LerpMatrix(s.Matrix, e.Matrix, t);
                    break;
            }
        }

        private static Transform Normalize(object? obj) => obj switch
        {
            Transform t => t,
            _ => new TransformGroup()
        };

        private static List<Transform> ExtractTransforms(Transform t)
        {
            return t is TransformGroup g ? g.Children.ToList() : [t];
        }

        private static List<(Transform? s, Transform? e)> MatchPairs(List<Transform> s, List<Transform> e)
        {
            var types = s.Select(t => t.GetType()).Union(e.Select(t => t.GetType())).Distinct();
            return [.. types.Select(t => (s.LastOrDefault(x => x.GetType() == t), e.LastOrDefault(x => x.GetType() == t)))];
        }

        protected virtual Transform CombineTransforms(List<(Transform? s, Transform? e)> pairs, double t)
        {
            var list = new List<Transform>();
            foreach (var (s, e) in pairs)
            {
                var interpolated = InterpolateSingle(s, e, t);
                if (interpolated != null)
                    list.Add(interpolated);
            }

            switch (list.Count)
            {
                case 0:
                    return new TransformGroup();
                case 1:
                    return list[0];
            }

            var g = new TransformGroup();
            foreach (var tr in list)
                g.Children.Add(tr);

            return g;
        }

        protected virtual Transform? InterpolateSingle(Transform? s, Transform? e, double t)
        {
            static Transform Default(Transform? t) => t switch
            {
                TranslateTransform => new TranslateTransform(),
                ScaleTransform => new ScaleTransform(),
                RotateTransform => new RotateTransform(),
                SkewTransform => new SkewTransform(),
                _ => new MatrixTransform()
            };

            s ??= Default(e);
            e ??= Default(s);

            if (s.GetType() != e.GetType())
                return e; // fallback to end transform

            return s switch
            {
                TranslateTransform st when e is TranslateTransform et =>
                    new TranslateTransform { X = Lerp(st.X, et.X, t), Y = Lerp(st.Y, et.Y, t) },

                ScaleTransform st when e is ScaleTransform et =>
                    new ScaleTransform { ScaleX = Lerp(st.ScaleX, et.ScaleX, t), ScaleY = Lerp(st.ScaleY, et.ScaleY, t) },

                RotateTransform st when e is RotateTransform et =>
                    new RotateTransform { Angle = LerpAngle(st.Angle, et.Angle, t) },

                SkewTransform st when e is SkewTransform et =>
                    new SkewTransform { AngleX = Lerp(st.AngleX, et.AngleX, t), AngleY = Lerp(st.AngleY, et.AngleY, t) },

                MatrixTransform st when e is MatrixTransform et =>
                    new MatrixTransform { Matrix = LerpMatrix(st.Matrix, et.Matrix, t) },

                _ => null
            };
        }

        protected virtual double LerpAngle(double start, double end, double t) => Lerp(start, end, t);

        private static Matrix LerpMatrix(Matrix m1, Matrix m2, double t)
        {
            return new Matrix(
                Lerp(m1.M11, m2.M11, t),
                Lerp(m1.M12, m2.M12, t),
                Lerp(m1.M21, m2.M21, t),
                Lerp(m1.M22, m2.M22, t),
                Lerp(m1.OffsetX, m2.OffsetX, t),
                Lerp(m1.OffsetY, m2.OffsetY, t)
            );
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
