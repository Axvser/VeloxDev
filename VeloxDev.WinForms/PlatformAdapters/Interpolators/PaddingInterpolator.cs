using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WinForms.PlatformAdapters.Interpolators
{
    public class PaddingInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var padding1 = (Padding)(start ?? new Padding(0));
            var padding2 = (Padding)(end ?? padding1);
            if (steps == 1)
            {
                return [padding2];
            }

            List<object?> result = new(steps);

            for (var i = 0; i < steps; i++)
            {
                var t = (double)(i + 1) / steps;
                var left = padding1.Left + (int)(t * (padding2.Left - padding1.Left));
                var top = padding1.Top + (int)(t * (padding2.Top - padding1.Top));
                var right = padding1.Right + (int)(t * (padding2.Right - padding1.Right));
                var bottom = padding1.Bottom + (int)(t * (padding2.Bottom - padding1.Bottom));
                result.Add(new Padding(left, top, right, bottom));
            }
            result[0] = start;
            result[steps - 1] = end;

            return result;
        }
    }
}
