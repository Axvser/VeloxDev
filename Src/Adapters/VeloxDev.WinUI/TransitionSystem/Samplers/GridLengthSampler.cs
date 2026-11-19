using Microsoft.UI.Xaml;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class GridLengthSampler : ISampler
    {
        /// <inheritdoc />
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        /// <inheritdoc />
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <inheritdoc />
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {

            var g1 = (GridLength)(start ?? new GridLength(0));
            var g2 = (GridLength)(end ?? g1);

            // 处理不同类型的 GridLength。
            if (g1.GridUnitType != g2.GridUnitType)
            {
                // 单位类型不同就直接切到目标值。
                property.SetValue(target, t >= 1 ? g2 : g1);
                return;
            }

            // 单位类型相同才插值。
            // 长度在 0 处钳住：GridLength 只收非负值，连构造函数都会 Validate —— 越界是抛异常，不是截断。
            // 与 Avalonia 的同一个采样器一致（那里也是 Math.Max(0, ...)）。
            var delta = g2.Value - g1.Value;
            var value = g1.Value + delta * t;
            property.SetValue(target, new GridLength(Math.Max(0d, value), g1.GridUnitType));
        }
    }
}
