using Microsoft.Maui.Controls.Shapes;

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

            var m1 = start as Transform;
            var m2 = end as Transform;
            var matrix1 = m1?.Value ?? Matrix.Identity;
            var matrix2 = m2?.Value ?? matrix1;

            // 每帧零分配：复用同一个 scratch Transform，每帧从原始 start/end 重算它的 Value（结构体）。
            if (working is not Transform wt)
            {
                wt = new Transform();
                working = wt;
            }
            wt.Value = new Matrix(
                matrix1.M11 + t * (matrix2.M11 - matrix1.M11),
                matrix1.M12 + t * (matrix2.M12 - matrix1.M12),
                matrix1.M21 + t * (matrix2.M21 - matrix1.M21),
                matrix1.M22 + t * (matrix2.M22 - matrix1.M22),
                matrix1.OffsetX + t * (matrix2.OffsetX - matrix1.OffsetX),
                matrix1.OffsetY + t * (matrix2.OffsetY - matrix1.OffsetY)
            );
            property.SetValue(target, wt);
        }
    }
}
