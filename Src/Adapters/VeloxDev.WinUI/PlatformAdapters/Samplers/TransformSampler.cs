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

            var s = Normalize(start);
            var e = Normalize(end);

            if (s.GetType() == e.GetType() && IsKnownTransform(s))
            {
                // 每帧零分配：复用同一个 scratch 变换，每帧从原始 start/end 重算。
                if (working is not Transform wt || wt.GetType() != s.GetType())
                {
                    wt = CloneTransform(s);
                    working = wt;
                }
                MutateInPlace(wt, s, e, t);
                property.SetValue(target, wt);
                return;
            }

            // 子类可能重写了 CombineTransforms/InterpolateSingle/LerpAngle，那时仍按旧路径逐帧调用它们。
            if (GetType() == typeof(TransformSampler))
            {
                // 配对与 scratch 只在起止对象换了时建一次，此后每帧原地改写（绝不改动 start/end）。
                if (working is not SlowPlan plan || !ReferenceEquals(plan.Start, start) || !ReferenceEquals(plan.End, end))
                {
                    plan = SlowPlan.Build(start, end, MatchPairs(ExtractTransforms(s), ExtractTransforms(e)));
                    working = plan;
                }
                plan.Apply(this, t);
                property.SetValue(target, plan.Result);
                return;
            }

            property.SetValue(target, CombineTransforms(MatchPairs(ExtractTransforms(s), ExtractTransforms(e)), t));
        }

        // 慢路径的每帧复用状态，语义与 CombineTransforms + InterpolateSingle 一致：
        // 类型不一致直接取终点变换，同类型的未知变换丢弃，旋转只插值角度。
        private sealed class SlowPlan
        {
            public object? Start;
            public object? End;
            public Transform Result = null!;
            private Transform[] _starts = null!;
            private Transform[] _ends = null!;
            private Transform[] _scratch = null!;

            public static SlowPlan Build(object? start, object? end, List<(Transform? s, Transform? e)> pairs)
            {
                var starts = new List<Transform>(pairs.Count);
                var ends = new List<Transform>(pairs.Count);
                var scratch = new List<Transform>(pairs.Count);
                foreach (var (ps, pe) in pairs)
                {
                    var s = ps ?? DefaultFor(pe);
                    var e = pe ?? DefaultFor(s);
                    Transform item;
                    if (s.GetType() != e.GetType()) item = e;
                    else if (!IsKnownTransform(s)) continue;
                    else item = s is RotateTransform ? new RotateTransform() : CloneTransform(s);
                    starts.Add(s);
                    ends.Add(e);
                    scratch.Add(item);
                }

                Transform result;
                switch (scratch.Count)
                {
                    case 0: result = new TransformGroup(); break;
                    case 1: result = scratch[0]; break;
                    default:
                        var g = new TransformGroup();
                        foreach (var tr in scratch) g.Children.Add(tr);
                        result = g;
                        break;
                }

                return new SlowPlan { Start = start, End = end, _starts = [.. starts], _ends = [.. ends], _scratch = [.. scratch], Result = result };
            }

            public void Apply(TransformSampler sampler, double t)
            {
                for (var i = 0; i < _scratch.Length; i++)
                {
                    var s = _starts[i];
                    var e = _ends[i];
                    if (s.GetType() != e.GetType()) continue;
                    if (_scratch[i] is RotateTransform rt && s is RotateTransform rs && e is RotateTransform re)
                        rt.Angle = sampler.LerpAngle(rs.Angle, re.Angle, t);
                    else
                        sampler.MutateInPlace(_scratch[i], s, e, t);
                }
            }

            private static Transform DefaultFor(Transform? t) => t switch
            {
                TranslateTransform => new TranslateTransform(),
                ScaleTransform => new ScaleTransform(),
                RotateTransform => new RotateTransform(),
                SkewTransform => new SkewTransform(),
                _ => new MatrixTransform()
            };
        }

        // 只有这几种变换既能被 CloneTransform 克隆、又有逐字段插值；其余子类必须走矩阵路径，
        // 否则会落进快路径的克隆上，而克隆恰好是唯一对它们抛异常的地方。
        private static bool IsKnownTransform(Transform transform) => transform is
            TranslateTransform or ScaleTransform or RotateTransform or SkewTransform or MatrixTransform;

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

        /// <summary>Combines the matched transform pairs into one transform at progress <paramref name="t"/>.</summary>
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

        /// <summary>Interpolates a single transform pair, falling back to the matrix path for mismatched or unknown types.</summary>
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

        /// <summary>Linearly interpolates the rotation angle by <paramref name="t"/>.</summary>
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

        /// <summary>Interpolates the angle along the shortest path, honoring <paramref name="reverse"/>.</summary>
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
