using System.Windows.Media;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class TransformSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            // 精确端点而非区间：管线每趟最后一帧正好给 1（反向给 0），调用方自己的实例必须活到最后 ——
            // 嵌套路径如 ((TranslateTransform)x.RenderTransform).X 依赖声明时的运行时类型，插值用的 scratch 会替换它；越过端点则落到下一分支。
            if (t == 0d) { property.SetValue(target, start); return; }
            if (t == 1d) { property.SetValue(target, end); return; }

            var direction = options is RotationDirection d ? d : RotationDirection.Auto;

            if (start is Transform startT && end is Transform endT
                && startT.GetType() == endT.GetType()
                && startT.GetType() != typeof(TransformGroup))
            {
                // 每帧零分配：复用同一个 scratch 变换，每帧从原始 start/end 重算。
                if (working is not Transform wt || wt.GetType() != startT.GetType())
                {
                    wt = CloneTransform(startT);
                    working = wt;
                }
                MutateInPlace(wt, startT, endT, direction, t);
                property.SetValue(target, wt);
                return;
            }

            // 组/类型不匹配/null：配对与 scratch 只在起止对象换了时建一次，此后每帧原地改写（绝不改动 start/end）。
            if (working is not SlowPlan plan || !ReferenceEquals(plan.Start, start) || !ReferenceEquals(plan.End, end))
            {
                plan = SlowPlan.Build(start, end);
                working = plan;
            }
            plan.Apply(direction, t);
            property.SetValue(target, plan.Result);
        }

        // 慢路径的每帧复用状态：配好的起止对与写回用的 scratch 变换。
        private sealed class SlowPlan
        {
            public object? Start;
            public object? End;
            public Transform Result = null!;
            private Transform[] _starts = null!;
            private Transform[] _ends = null!;
            private Transform[] _scratch = null!;

            public static SlowPlan Build(object? start, object? end)
            {
                var pairs = CreateTransformPairs(ParseTransforms(NormalizeInput(start)), ParseTransforms(NormalizeInput(end)));
                var plan = new SlowPlan { Start = start, End = end };
                var starts = new List<Transform>(pairs.Count);
                var ends = new List<Transform>(pairs.Count);
                var scratch = new List<Transform>(pairs.Count);
                foreach (var pair in pairs)
                {
                    var s = pair.Start ?? GetDefaultTransform(pair.End);
                    var e = pair.End ?? GetDefaultTransform(s);
                    var sameType = s.GetType() == e.GetType();
                    // 同类型但不在五种已知变换里的，旧实现直接丢弃，这里保持一致。
                    if (sameType && !IsKnown(s)) continue;
                    // 类型不一致按矩阵插值，scratch 也就是矩阵。
                    var w = sameType ? CloneTransform(s) : new MatrixTransform(s.Value);
                    starts.Add(s);
                    ends.Add(e);
                    scratch.Add(w);
                }
                plan._starts = [.. starts];
                plan._ends = [.. ends];
                plan._scratch = [.. scratch];
                plan.Result = scratch.Count switch
                {
                    0 => new TransformGroup(),
                    1 => scratch[0],
                    _ => new TransformGroup { Children = [.. scratch] }
                };
                return plan;
            }

            public void Apply(RotationDirection direction, double t)
            {
                for (var i = 0; i < _scratch.Length; i++)
                {
                    var s = _starts[i];
                    var e = _ends[i];
                    if (s.GetType() == e.GetType())
                        MutateInPlace(_scratch[i], s, e, direction, t);
                    else
                        ((MatrixTransform)_scratch[i]).Matrix = LerpMatrix(s.Value, e.Value, t);
                }
            }
        }

        // 取默认变换（保证从「无」平滑过渡）。
        private static Transform GetDefaultTransform(Transform? transform) => transform switch
        {
            TranslateTransform _ => new TranslateTransform(0, 0),
            RotateTransform _ => new RotateTransform(0, 0, 0),
            ScaleTransform _ => new ScaleTransform(1, 1, 0, 0),
            SkewTransform _ => new SkewTransform(0, 0, 0, 0),
            _ => new MatrixTransform(Matrix.Identity)
        };

        private static Transform CloneTransform(Transform source) => source switch
        {
            TranslateTransform t => new TranslateTransform(t.X, t.Y),
            RotateTransform t => new RotateTransform(t.Angle, t.CenterX, t.CenterY),
            ScaleTransform t => new ScaleTransform(t.ScaleX, t.ScaleY, t.CenterX, t.CenterY),
            SkewTransform t => new SkewTransform(t.AngleX, t.AngleY, t.CenterX, t.CenterY),
            MatrixTransform t => new MatrixTransform(t.Matrix),
            _ => (Transform)source.Clone(), // rare fallback: other Freezable transform subclasses
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

        private static Transform NormalizeInput(object? input)
        {
            // 把 null/Identity 归一化成空 TransformGroup。
            if (input == null || (input is Transform transform && transform == Transform.Identity))
                return new TransformGroup();
            return (Transform)input;
        }

        private static List<Transform> ParseTransforms(Transform transform)
        {
            var transforms = new List<Transform>();

            if (transform is TransformGroup group && group.Children.Count > 0)
            {
                // 每类保留最后一个变换。
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

        private static bool IsKnown(Transform t) =>
            t is TranslateTransform or RotateTransform or ScaleTransform or SkewTransform or MatrixTransform;

        private static double LerpAngle(double start, double end, double t, RotationDirection direction)
        {
            bool reverse = direction.HasFlag(RotationDirection.CounterClockWise)
                        || direction.HasFlag(RotationDirection.CounterClockWiseZ);
            bool forceClockWise = direction.HasFlag(RotationDirection.ClockWise)
                               || direction.HasFlag(RotationDirection.ClockWiseZ);
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
                Lerp(m1.OffsetX, m2.OffsetX, t),
                Lerp(m1.OffsetY, m2.OffsetY, t));
        }

        private static double LerpDirectionalAngle(double start, double end, double t, bool reverse)
        {
            var delta = (end - start) % 360d;
            if (reverse)
            {
                if (delta > 0d) delta -= 360d;
            }
            else if (delta < 0d)
            {
                delta += 360d;
            }

            return start + delta * t;
        }

        private sealed class TransformPair
        {
            public Transform? Start { get; set; }
            public Transform? End { get; set; }
        }
    }
}
