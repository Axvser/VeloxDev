using Microsoft.UI.Xaml;

namespace VeloxDev.Adapters.NativeSamplers
{
    public class GridLengthSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var g1 = (GridLength)(start ?? new GridLength(0));
            var g2 = (GridLength)(end ?? g1);

            // Handle GridLength values of different types.
            if (g1.GridUnitType != g2.GridUnitType)
            {
                // If the unit types differ, switch directly to the target value.
                property.SetValue(target, t >= 1 ? g2 : g1);
                return;
            }

            // With the same unit type, interpolate.
            var delta = g2.Value - g1.Value;
            var value = g1.Value + delta * t;
            property.SetValue(target, new GridLength(value, g1.GridUnitType));
        }
    }
}
