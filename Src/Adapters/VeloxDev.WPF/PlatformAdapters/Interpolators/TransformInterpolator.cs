using System.Windows.Media;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters.Interpolators
{
    public class TransformInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            // 1. 统一预处理
            var startTransform = NormalizeInput(start);
            var endTransform = NormalizeInput(end);

            if (steps <= 1) return [endTransform];

            // 2. 解析有效变换
            var startTransforms = ParseTransforms(startTransform);
            var endTransforms = ParseTransforms(endTransform);

            // 3. 创建匹配对
            var transformPairs = CreateTransformPairs(startTransforms, endTransforms);

            // 4. 生成插值序列
            var result = new List<object?>(steps);
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                result.Add(InterpolateTransformPairs(transformPairs, t));
            }

            // 5. 确保首尾精确匹配
            result[0] = startTransform;
            result[steps - 1] = endTransform;
            return result;
        }

        private static Transform NormalizeInput(object? input)
        {
            // 统一将null/Identity转为空TransformGroup
            if (input == null || (input is Transform transform && transform == Transform.Identity))
                return new TransformGroup();
            return (Transform)input;
        }

        private static List<Transform> ParseTransforms(Transform transform)
        {
            var transforms = new List<Transform>();

            if (transform is TransformGroup group && group.Children.Count > 0)
            {
                // 保留每种类型的最后一个变换
                var lastOfType = new Dictionary<Type, Transform>();
                foreach (var child in group.Children)
                {
                    if (child != Transform.Identity)
                        lastOfType[child.GetType()] = child;
                }
                transforms.AddRange(lastOfType.Values);
            }
            else if (transform != null && transform != Transform.Identity)
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

        private static Transform InterpolateTransformPairs(
            List<(Transform? start, Transform? end)> pairs, double t)
        {
            var interpolatedTransforms = new List<Transform>();
            foreach (var (start, end) in pairs)
            {
                var interpolated = InterpolateSingleTransformPair(start, end, t);
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

        private static Transform? InterpolateSingleTransformPair(Transform? start, Transform? end, double t)
        {
            // 获取默认变换（确保从无到有的过渡平滑）
            static Transform GetDefaultTransform(Transform? transform) => transform switch
            {
                TranslateTransform _ => new TranslateTransform(0, 0),
                RotateTransform _ => new RotateTransform(0, 0, 0),
                ScaleTransform _ => new ScaleTransform(1, 1, 0, 0),
                SkewTransform _ => new SkewTransform(0, 0, 0, 0),
                _ => new MatrixTransform(Matrix.Identity)
            };

            start ??= GetDefaultTransform(end);
            end ??= GetDefaultTransform(start);

            // 类型不匹配时回退到矩阵插值
            if (start.GetType() != end.GetType())
            {
                return new MatrixTransform(
                    LerpMatrix(start.Value, end.Value, t));
            }

            // 具体类型插值
            return start switch
            {
                TranslateTransform st when end is TranslateTransform et =>
                    new TranslateTransform(
                        Lerp(st.X, et.X, t),
                        Lerp(st.Y, et.Y, t)),

                RotateTransform st when end is RotateTransform et =>
                    new RotateTransform(
                        Lerp(st.Angle, et.Angle, t),
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

                _ => null
            };
        }

        private static double Lerp(double a, double b, double t) => a + t * (b - a);
        private static Matrix LerpMatrix(Matrix m1, Matrix m2, double t)
        {
            return new Matrix(
                Lerp(m1.M11, m2.M11, t),
                Lerp(m1.M12, m2.M12, t),
                Lerp(m1.M21, m2.M21, t),
                Lerp(m1.M22, m2.M22, t),
                Lerp(m1.OffsetX, m2.OffsetX, t),
                Lerp(m1.OffsetY, m2.OffsetY, t));
        }
    }
}
