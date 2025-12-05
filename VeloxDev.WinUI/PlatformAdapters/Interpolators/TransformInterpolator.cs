using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Linq;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters.Interpolators
{
    public class TransformInterpolator : IValueInterpolator
    {
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static readonly TransformGroup Identity = new();

        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var s = Normalize(start);
            var e = Normalize(end);
            if (steps <= 1) return [e];

            var startList = ExtractTransforms(s);
            var endList = ExtractTransforms(e);
            var pairs = MatchPairs(startList, endList);

            List<object?> result = new(steps);
            for (var i = 0; i < steps; i++)
            {
                var t = (double)i / (steps - 1);
                result.Add(CombineTransforms(pairs, t));
            }

            result[0] = s;
            result[^1] = e;
            return result;
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

        private static Transform CombineTransforms(List<(Transform? s, Transform? e)> pairs, double t)
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

        private static Transform? InterpolateSingle(Transform? s, Transform? e, double t)
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
                    new RotateTransform { Angle = Lerp(st.Angle, et.Angle, t) },

                SkewTransform st when e is SkewTransform et =>
                    new SkewTransform { AngleX = Lerp(st.AngleX, et.AngleX, t), AngleY = Lerp(st.AngleY, et.AngleY, t) },

                MatrixTransform st when e is MatrixTransform et =>
                    new MatrixTransform { Matrix = LerpMatrix(st.Matrix, et.Matrix, t) },

                _ => null
            };
        }

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
    }
}
