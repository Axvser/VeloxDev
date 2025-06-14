using Avalonia.Media;

namespace VeloxDev.Avalonia.Tools.Value
{
    public static class Copier
    {
        private const string IdentityTransformString = "identity";

        public static IBrush CopyBrush(IBrush brush)
        {
            if (brush == null)
                return Brushes.Transparent;

            if (brush is ISolidColorBrush solidColorBrush)
            {
                var newBrush = new SolidColorBrush(solidColorBrush.Color)
                {
                    Opacity = solidColorBrush.Opacity,
                    TransformOrigin = solidColorBrush.TransformOrigin
                };
                if (solidColorBrush.Transform is not null) newBrush.Transform = CopyTransform(solidColorBrush.Transform);
                return newBrush;
            }
            else if (brush is ILinearGradientBrush linearGradientBrush)
            {
                var newBrush = new LinearGradientBrush()
                {
                    StartPoint = linearGradientBrush.StartPoint,
                    EndPoint = linearGradientBrush.EndPoint,
                    Opacity = linearGradientBrush.Opacity,
                    SpreadMethod = linearGradientBrush.SpreadMethod,
                    TransformOrigin = linearGradientBrush.TransformOrigin
                };
                if (linearGradientBrush.Transform is not null) newBrush.Transform = CopyTransform(linearGradientBrush.Transform);
                foreach (var stop in linearGradientBrush.GradientStops)
                {
                    newBrush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
                }

                return newBrush;
            }
            else if (brush is IRadialGradientBrush radialGradientBrush)
            {
                var newBrush = new RadialGradientBrush()
                {
                    Center = radialGradientBrush.Center,
                    GradientOrigin = radialGradientBrush.GradientOrigin,
                    RadiusX = radialGradientBrush.RadiusX,
                    RadiusY = radialGradientBrush.RadiusY,
                    Opacity = radialGradientBrush.Opacity,
                    SpreadMethod = radialGradientBrush.SpreadMethod,
                    TransformOrigin = radialGradientBrush.TransformOrigin
                };
                if (radialGradientBrush.Transform is not null) newBrush.Transform = CopyTransform(radialGradientBrush.Transform);
                foreach (var stop in radialGradientBrush.GradientStops)
                {
                    newBrush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
                }

                return newBrush;
            }
            else if (brush is IConicGradientBrush conicGradientBrush)
            {
                var newBrush = new ConicGradientBrush()
                {
                    Center = conicGradientBrush.Center,
                    Angle = conicGradientBrush.Angle,
                    Opacity = conicGradientBrush.Opacity,
                    SpreadMethod = conicGradientBrush.SpreadMethod,
                    TransformOrigin = conicGradientBrush.TransformOrigin
                };
                if (conicGradientBrush.Transform is not null) newBrush.Transform = CopyTransform(conicGradientBrush.Transform);
                foreach (var stop in conicGradientBrush.GradientStops)
                {
                    newBrush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
                }

                return newBrush;
            }
            else if (brush is IImageBrush imageBrush)
            {
                if (imageBrush.Source is not null)
                {
                    var newBrush = new ImageBrush(imageBrush.Source)
                    {
                        AlignmentX = imageBrush.AlignmentX,
                        AlignmentY = imageBrush.AlignmentY,
                        DestinationRect = imageBrush.DestinationRect,
                        Opacity = imageBrush.Opacity,
                        SourceRect = imageBrush.SourceRect,
                        Stretch = imageBrush.Stretch,
                        TileMode = imageBrush.TileMode,
                        TransformOrigin = imageBrush.TransformOrigin
                    };
                    if (imageBrush.Transform is not null) newBrush.Transform = CopyTransform(imageBrush.Transform);
                    return newBrush;
                }
            }
            else if (brush is VisualBrush visualBrush)
            {
                if (visualBrush.Visual is not null)
                {
                    var newBrush = new VisualBrush(visualBrush.Visual)
                    {
                        AlignmentX = visualBrush.AlignmentX,
                        AlignmentY = visualBrush.AlignmentY,
                        DestinationRect = visualBrush.DestinationRect,
                        Opacity = visualBrush.Opacity,
                        SourceRect = visualBrush.SourceRect,
                        Stretch = visualBrush.Stretch,
                        TileMode = visualBrush.TileMode,
                        TransformOrigin = visualBrush.TransformOrigin
                    };
                    if (visualBrush.Transform is not null) newBrush.Transform = CopyTransform(visualBrush.Transform);
                    return newBrush;
                }
            }

            // Fallback for unknown brush types
            return Brushes.Transparent;
        }

        public static ITransform CopyTransform(ITransform transform)
        {
            return Transform.Parse(transform.Value.ToString() ?? IdentityTransformString);
        }
    }
}
