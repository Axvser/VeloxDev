using Avalonia.Controls;
using System;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class GridLengthSampler : ISampleable, ISampler
    {
        public ISampler Normalize(object? start, object? end, object? options) => this;

        public void Update(object target, ITransitionProperty property, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var g1 = (GridLength)(start ?? new GridLength(0));
            var g2 = (GridLength)(end ?? g1);

            // If grid units differ, interpolation is impossible; hold the start value.
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