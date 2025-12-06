using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Text;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters.Interpolators
{
    public class BrushInterpolator : IValueInterpolator
    {
        public List<object?> Interpolate(object? start, object? end, int steps)
        {
            var endBrush = end as IBrush ?? Brushes.Transparent;
            var startBrush = AdaptStartBrush(start);

            var result = new List<object?>();

            if (steps <= 1)
            {
                result.Add(endBrush);
                return result;
            }

            // 确保精确的起始和结束值
            result.Add(startBrush);

            if (steps > 2)
            {
                for (int i = 1; i < steps - 1; i++)
                {
                    double t = (double)i / (steps - 1);
                    result.Add(InterpolateBrush(startBrush, endBrush, t));
                }
            }

            result.Add(endBrush);

            return result;
        }

        private static IBrush AdaptStartBrush(object? start)
        {
            if (start == null)
            {
                return Brushes.Transparent;
            }

            return (IBrush)start;
        }

        private static IBrush InterpolateBrush(IBrush start, IBrush end, double t)
        {
            if (start is ISolidColorBrush startSolid && end is ISolidColorBrush endSolid)
            {
                return InterpolateSolidColor(startSolid, endSolid, t);
            }
            else
            {
                return CrossFadeBrushes(start, end, t);
            }
        }

        private static ISolidColorBrush InterpolateSolidColor(ISolidColorBrush start, ISolidColorBrush end, double t)
        {
            return new SolidColorBrush(
                Color.FromArgb(
                    (byte)(start.Color.A + (end.Color.A - start.Color.A) * t),
                    (byte)(start.Color.R + (end.Color.R - start.Color.R) * t),
                    (byte)(start.Color.G + (end.Color.G - start.Color.G) * t),
                    (byte)(start.Color.B + (end.Color.B - start.Color.B) * t)))
            {
                Opacity = start.Opacity + (end.Opacity - start.Opacity) * t
            };
        }

        private static IBrush CrossFadeBrushes(IBrush start, IBrush end, double t)
        {
            if (t <= 0.0) return start;
            if (t >= 1.0) return end;

            // 使用RenderTargetBitmap实现精确混合
            return CreateBlendedBrush(start, end, t);
        }

        private static IBrush CreateBlendedBrush(IBrush start, IBrush end, double t)
        {
            const int renderSize = 100;
            var bmp = new RenderTargetBitmap(new PixelSize(renderSize, renderSize));

            using (var ctx = bmp.CreateDrawingContext())
            {
                // 绘制底层画刷
                using (ctx.PushOpacity(1 - t))
                    ctx.DrawRectangle(start, null, new Rect(0, 0, renderSize, renderSize));

                // 绘制上层画刷
                using (ctx.PushOpacity(t))
                    ctx.DrawRectangle(end, null, new Rect(0, 0, renderSize, renderSize));
            }

            return new ImageBrush(bmp)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
        }
    }
}
