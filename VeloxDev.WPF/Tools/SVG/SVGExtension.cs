using System.IO;
using System.Windows;
using System.Windows.Media;
using VeloxDev.WPF.Tools.String;

namespace VeloxDev.WPF.Tools.SVG
{
    public static class SVGExtension
    {
        public static Geometry FromFile(string filePath)
        {
            return Geometry.Parse(ReadDataFromPath(filePath));
        }
        public static Geometry Adapt(this Geometry geometry, FrameworkElement target)
        {
            if (geometry == null || geometry.Bounds.IsEmpty)
                return Geometry.Empty;

            // 创建原始几何的深拷贝
            Geometry clonedGeometry = geometry.Clone();

            // 计算原始边界
            Rect bounds = geometry.Bounds;

            // 计算缩放比例（保持宽高比）
            double scale = Math.Min(
                target.ActualWidth / bounds.Width,
                target.ActualHeight / bounds.Height
            );

            // 创建变换组合
            TransformGroup transform = new();

            // 第一步：将图形移动到原点
            transform.Children.Add(new TranslateTransform(-bounds.Left, -bounds.Top));

            // 第二步：应用缩放
            transform.Children.Add(new ScaleTransform(scale, scale));

            // 第三步：计算居中偏移
            Vector centerOffset = new(
                (target.ActualWidth - bounds.Width * scale) / 2,
                (target.ActualHeight - bounds.Height * scale) / 2
            );
            transform.Children.Add(new TranslateTransform(centerOffset.X, centerOffset.Y));

            // 应用变换到拷贝对象
            clonedGeometry.Transform = transform;

            // 冻结对象提升性能（可选）
            if (clonedGeometry.CanFreeze)
                clonedGeometry.Freeze();

            return clonedGeometry;
        }

        private static string ReadDataFromPath(string filePath)
        {
            var result = string.Empty;

            if (File.Exists(filePath))
            {
                try
                {

                    var parsed = StringCatcher.Between(File.ReadAllText(filePath), " d=\"", "\"");
                    result = string.Join(" ", parsed);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading SVG path: {ex.Message}");
                    return string.Empty;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Error fiel path: {filePath}");
            }

            return result;
        }
    }
}
