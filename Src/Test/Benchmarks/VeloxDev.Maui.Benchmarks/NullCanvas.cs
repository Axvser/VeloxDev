using Microsoft.Maui.Graphics;

namespace VeloxDev.Maui.Benchmarks;

// 什么都不画、什么都不分配的 ICanvas：基准只量适配器自己的几何与颜色分配。
internal sealed class NullCanvas : ICanvas
{
    public float DisplayScale { get; set; } = 1f;
    public float StrokeSize { set { } }
    public float MiterLimit { set { } }
    public Color StrokeColor { set { } }
    public LineCap StrokeLineCap { set { } }
    public LineJoin StrokeLineJoin { set { } }
    public float[] StrokeDashPattern { set { } }
    public float StrokeDashOffset { set { } }
    public Color FillColor { set { } }
    public Color FontColor { set { } }
    public IFont Font { set { } }
    public float FontSize { set { } }
    public float Alpha { set { } }
    public bool Antialias { set { } }
    public BlendMode BlendMode { set { } }
    public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) { }
    public void ClipRectangle(float x, float y, float width, float height) { }
    public void ConcatenateTransform(System.Numerics.Matrix3x2 transform) { }
    public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise, bool closed) { }
    public void DrawEllipse(float x, float y, float width, float height) { }
    public void DrawImage(Microsoft.Maui.Graphics.IImage image, float x, float y, float width, float height) { }
    public void DrawLine(float x1, float y1, float x2, float y2) { }
    public void DrawPath(PathF path) { }
    public void DrawRectangle(float x, float y, float width, float height) { }
    public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) { }
    public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment) { }
    public void DrawString(string value, float x, float y, float width, float height, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment, TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0) { }
    public void DrawText(Microsoft.Maui.Graphics.Text.IAttributedText value, float x, float y, float width, float height) { }
    public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise) { }
    public void FillEllipse(float x, float y, float width, float height) { }
    public void FillPath(PathF path, WindingMode windingMode) { }
    public void FillRectangle(float x, float y, float width, float height) { }
    public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) { }
    public void ResetState() { }
    public void SaveState() { }
    public void Rotate(float degrees, float x, float y) { }
    public void Rotate(float degrees) { }
    public void Scale(float sx, float sy) { }
    public void SetFillPaint(Paint paint, RectF rectangle) { }
    public void SetShadow(SizeF offset, float blur, Color color) { }
    public void SubtractFromClip(float x, float y, float width, float height) { }
    public void Translate(float tx, float ty) { }
    public SizeF GetStringSize(string value, IFont font, float fontSize) => default;
    public SizeF GetStringSize(string value, IFont font, float fontSize, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) => default;
    public bool RestoreState() => true;
}
