using Avalonia.Controls;
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

            // 网格单位不同就无法插值，保持起始值。
            if (g1.GridUnitType != g2.GridUnitType)
            {
                property.SetValue(target, g1);
                return;
            }

            var delta = g2.Value - g1.Value;

            property.SetValue(target, new GridLength(
                Math.Max(0, g1.Value + delta * t),
                g1.GridUnitType
            ));
        }
    }
}