using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters
{
    public class Interpolator : InterpolatorCore<InterpolatorOutput, DispatcherPriority>
    {
        static Interpolator()
        {
            RegisterInterpolator(typeof(IBrush), new NativeInterpolators.BrushInterpolator());
            RegisterInterpolator(typeof(Thickness), new NativeInterpolators.ThicknessInterpolator());
            RegisterInterpolator(typeof(Point), new NativeInterpolators.PointInterpolator());
            RegisterInterpolator(typeof(CornerRadius), new NativeInterpolators.CornerRadiusInterpolator());
            RegisterInterpolator(typeof(ITransform), new NativeInterpolators.TransformInterpolator());
        }
    }
}
