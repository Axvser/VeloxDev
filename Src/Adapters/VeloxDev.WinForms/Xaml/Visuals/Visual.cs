using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>
/// A retained, lightweight visual node: bounds, transform, opacity, clip, effect and children, painted without an
/// HWND.
/// </summary>
/// <remarks>
/// <para>
/// This is the opt-in lightweight layer of the WinForms XAML runtime. It is intended for non-interactive content such
/// as decoration, text, shapes and connector batches; native controls and <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> instances
/// stay real child windows. A tree of visuals is hosted by <see cref="VeloxDev.WinForms.Xaml.Controls.VisualHost"/> and painted in one pass.
/// </para>
/// <para>
/// <see cref="Bounds"/> is expressed in the parent's coordinates; content is painted in the visual's own local
/// coordinates <c>0..Width/Height</c>. <see cref="Transform"/> is applied in that local coordinate space before the
/// bounds translation, <see cref="Clip"/> is applied in local coordinates, and <see cref="Opacity"/> and
/// <see cref="Effect"/> compose the whole subtree through a temporary bitmap.
/// </para>
/// </remarks>
public abstract class Visual : WinformsObject
{
    /// <summary>The rectangle this visual occupies in its parent's coordinates.</summary>
    public RectangleF Bounds { get; set; }

    /// <summary>A local transform applied after <see cref="Bounds"/> is translated to the origin.</summary>
    public Matrix Transform { get; set; } = new();

    /// <summary>The opacity of this visual and its subtree, in the range 0..1.</summary>
    public double Opacity { get; set; } = 1d;

    /// <summary>An optional clip rectangle in the visual's local coordinates.</summary>
    public RectangleF? Clip { get; set; }

    /// <summary>An optional effect applied to the rendered subtree.</summary>
    public VisualEffect? Effect { get; set; }

    /// <summary>Whether this visual and its subtree are painted and hit-tested.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Whether the subtree is cached in a bitmap between frames.</summary>
    /// <remarks>
    /// Use it for static groups over which only cheap changes happen: moving a cached visual does not re-render its
    /// content. A dependency-property change inside a <see cref="VisualElement"/> subtree marks the nearest cache
    /// dirty automatically; a custom visual must call <see cref="InvalidateCache"/> after changing the pixels it
    /// draws. An <see cref="Effect"/> disables the cache for that visual.
    /// </remarks>
    public bool CacheAsBitmap { get; set; }

    /// <summary>The container that owns this visual, or <see langword="null"/> for a root.</summary>
    public ContainerVisual? Parent { get; internal set; }

    private Bitmap? _cache;
    private bool _cacheDirty = true;
    private CacheKey _cacheKey;

    /// <summary>Marks this visual's bitmap cache and every enclosing cache dirty.</summary>
    public void InvalidateCache()
    {
        _cacheDirty = true;
        Parent?.InvalidateCache();
    }

    /// <summary>Disposes this visual's cached bitmap, if any.</summary>
    internal void DisposeCache()
    {
        _cache?.Dispose();
        _cache = null;
        _cacheDirty = true;
    }

    /// <summary>Paints this visual and its children into <paramref name="graphics"/>.</summary>
    /// <param name="graphics">The target surface, already in the parent's coordinates.</param>
    public void Render(Graphics graphics)
    {
        if (graphics is null) throw new ArgumentNullException(nameof(graphics));
        if (!IsVisible || Opacity <= 0d || Bounds.Width <= 0f || Bounds.Height <= 0f)
        {
            return;
        }

        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform(Bounds.X, Bounds.Y);
            if (!Transform.IsIdentity)
            {
                // The transform belongs to the visual's own coordinate space: prepending it keeps the Bounds
                // translation (and every ancestor translation) unscaled. Appending would scale the card's
                // position as well, which is what made a Viewbox's child drift at any zoom other than 1.
                graphics.MultiplyTransform(Transform, MatrixOrder.Prepend);
            }

            if (Clip is { } clip)
            {
                graphics.SetClip(clip, CombineMode.Intersect);
            }

            if (CacheAsBitmap && Effect is null)
            {
                RenderCached(graphics);
                return;
            }

            if (Opacity < 0.999d || Effect is not null)
            {
                RenderComposited(graphics);
                return;
            }

            RenderContent(graphics);
            RenderChildren(graphics);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    /// <summary>Returns whether <paramref name="point"/>, in the parent's coordinates, hits this visual.</summary>
    /// <param name="point">The point to test.</param>
    public bool HitTest(PointF point)
    {
        if (!IsVisible || Opacity <= 0d || Bounds.Width <= 0f || Bounds.Height <= 0f)
        {
            return false;
        }

        var local = point;
        local.X -= Bounds.X;
        local.Y -= Bounds.Y;

        if (!Transform.IsIdentity)
        {
            using var inverse = Transform.Clone();
            inverse.Invert();
            var points = new[] { local };
            inverse.TransformPoints(points);
            local = points[0];
        }

        if (local.X < 0f || local.Y < 0f || local.X > Bounds.Width || local.Y > Bounds.Height)
        {
            return false;
        }

        if (Clip is { } clip && !clip.Contains(local))
        {
            return false;
        }

        return HitTestContent(local) || HitTestChildren(local);
    }

    /// <summary>Finds the deepest visible visual under <paramref name="point"/>.</summary>
    /// <param name="point">The point to test, in the parent's coordinates.</param>
    /// <returns>The hit visual, or <see langword="null"/>.</returns>
    public Visual? HitTestVisual(PointF point)
    {
        if (!IsVisible || Opacity <= 0d)
        {
            return null;
        }

        var local = ToLocal(point);
        if (local is null)
        {
            return null;
        }

        if (HitTestChildren(local.Value, out var child))
        {
            return child;
        }

        return HitTestContent(local.Value) ? this : null;
    }

    /// <summary>Paints the content of this visual in local coordinates.</summary>
    /// <param name="graphics">The target surface.</param>
    protected virtual void RenderContent(Graphics graphics)
    {
    }

    /// <summary>Paints the children of this visual in local coordinates.</summary>
    /// <param name="graphics">The target surface.</param>
    protected virtual void RenderChildren(Graphics graphics)
    {
    }

    /// <summary>Returns whether the local point hits this visual's own content.</summary>
    /// <param name="point">A point in local coordinates.</param>
    protected virtual bool HitTestContent(PointF point) => true;

    /// <summary>Returns whether a child is hit at the local point.</summary>
    /// <param name="point">A point in local coordinates.</param>
    protected virtual bool HitTestChildren(PointF point) => false;

    /// <summary>Finds the child hit at the local point.</summary>
    /// <param name="point">A point in local coordinates.</param>
    /// <param name="visual">The deepest hit visual.</param>
    protected virtual bool HitTestChildren(PointF point, out Visual? visual)
    {
        visual = null;
        return false;
    }

    private PointF? ToLocal(PointF point)
    {
        if (Bounds.Width <= 0f || Bounds.Height <= 0f)
        {
            return null;
        }

        var local = point;
        local.X -= Bounds.X;
        local.Y -= Bounds.Y;
        if (!Transform.IsIdentity)
        {
            using var inverse = Transform.Clone();
            inverse.Invert();
            var points = new[] { local };
            inverse.TransformPoints(points);
            local = points[0];
        }

        if (local.X < 0f || local.Y < 0f || local.X > Bounds.Width || local.Y > Bounds.Height)
        {
            return null;
        }

        if (Clip is { } clip && !clip.Contains(local))
        {
            return null;
        }

        return local;
    }

    // 缓存位图只装本地内容；Bounds 的平移、Transform 与 Clip 仍由外层 Render 应用，移动缓存节点不需要重画子树。
    private void RenderCached(Graphics target)
    {
        var width = (int)Math.Ceiling(Bounds.Width);
        var height = (int)Math.Ceiling(Bounds.Height);
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
        {
            RenderContent(target);
            RenderChildren(target);
            return;
        }

        var key = new CacheKey(width, height, Transform, Clip);
        if (_cache is null
            || _cache.Width != width
            || _cache.Height != height
            || _cacheDirty
            || !_cacheKey.Equals(key))
        {
            _cache?.Dispose();
            _cache = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(_cache))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RenderContent(graphics);
                RenderChildren(graphics);
            }

            _cacheDirty = false;
            _cacheKey = key;
        }

        DrawBitmap(target, _cache, PointF.Empty, Opacity);
    }

    private readonly struct CacheKey
    {
        private readonly int _width;
        private readonly int _height;
        private readonly float[] _transform;
        private readonly bool _hasClip;
        private readonly RectangleF _clip;

        internal CacheKey(int width, int height, Matrix transform, RectangleF? clip)
        {
            _width = width;
            _height = height;
            _transform = transform.IsIdentity ? Array.Empty<float>() : transform.Elements;
            _hasClip = clip is not null;
            _clip = clip ?? RectangleF.Empty;
        }

        public bool Equals(CacheKey other)
            => _width == other._width
               && _height == other._height
               && _hasClip == other._hasClip
               && _clip.Equals(other._clip)
               && _transform.SequenceEqual(other._transform);

        public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode()
            => _width ^ (_height << 16) ^ _clip.GetHashCode() ^ (_hasClip ? 1 : 0);
    }

    // 透明度/效果需要把整棵子树先落到一张位图，再按目标 alpha 合成；否则重叠子项会各自透，组透明度就不对。
    private void RenderComposited(Graphics target)
    {
        var padding = Effect?.Padding ?? 0f;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width + padding * 2f));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height + padding * 2f));
        if (width > 8192 || height > 8192)
        {
            // 超大视觉不为此分配数百 MB；退化为不合成。调用方应把效果放在较小的组上。
            RenderContent(target);
            RenderChildren(target);
            return;
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(padding, padding);
            RenderContent(graphics);
            RenderChildren(graphics);
        }

        var destination = new PointF(-padding, -padding);
        if (Effect is { } effect)
        {
            effect.Render(bitmap, target, destination, Opacity);
        }
        else
        {
            DrawBitmap(target, bitmap, destination, Opacity);
        }
    }

    internal static void DrawBitmap(Graphics target, Bitmap bitmap, PointF destination, double opacity)
    {
        if (opacity >= 0.999d)
        {
            target.DrawImage(bitmap, destination);
            return;
        }

        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = (float)XamlMath.Clamp(opacity, 0d, 1d) };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        target.DrawImage(
            bitmap,
            new Rectangle((int)Math.Round(destination.X), (int)Math.Round(destination.Y), bitmap.Width, bitmap.Height),
            0f,
            0f,
            bitmap.Width,
            bitmap.Height,
            GraphicsUnit.Pixel,
            attributes);
    }
}

/// <summary>A visual that owns an ordered collection of child visuals and an optional background.</summary>
public class ContainerVisual : Visual
{
    private readonly VisualCollection _children;

    /// <summary>Creates an empty container.</summary>
    public ContainerVisual()
    {
        _children = new VisualCollection(this);
    }

    /// <summary>The child visuals, painted in declaration order.</summary>
    public IList<Visual> Children => _children;

    /// <summary>An optional background brush painted over the local bounds.</summary>
    public Brush? Background { get; set; }

    /// <summary>The background corner radius in pixels.</summary>
    public float CornerRadius { get; set; }

    /// <summary>Adds a child and sets its <see cref="Visual.Parent"/>.</summary>
    /// <param name="visual">The child to add.</param>
    public void Add(Visual visual) => _children.Add(visual);

    /// <summary>Removes a child.</summary>
    /// <param name="visual">The child to remove.</param>
    /// <returns><see langword="true"/> when the child was present.</returns>
    public bool Remove(Visual visual) => _children.Remove(visual);

    /// <inheritdoc />
    protected override void RenderContent(Graphics graphics)
    {
        if (Background is not { } background || Bounds.Width <= 0f || Bounds.Height <= 0f)
        {
            return;
        }

        using var path = VeloxDev.WinForms.Xaml.Controls.XamlDrawing.RoundedRectangle(
            new RectangleF(0f, 0f, Bounds.Width, Bounds.Height),
            CornerRadius);
        background.FillPath(graphics, path);
    }

    /// <inheritdoc />
    protected override void RenderChildren(Graphics graphics)
    {
        foreach (var child in _children)
        {
            child?.Render(graphics);
        }
    }

    /// <inheritdoc />
    protected override bool HitTestContent(PointF point)
        => Background is { } background && background.ApproximateColor.A > 0;

    /// <inheritdoc />
    protected override bool HitTestChildren(PointF point) => HitTestChildren(point, out _);

    /// <inheritdoc />
    protected override bool HitTestChildren(PointF point, out Visual? visual)
    {
        for (var index = _children.Count - 1; index >= 0; index--)
        {
            var child = _children[index];
            if (child is null)
            {
                continue;
            }

            var hit = child.HitTestVisual(point);
            if (hit is not null)
            {
                visual = hit;
                return true;
            }
        }

        visual = null;
        return false;
    }

    private sealed class VisualCollection(ContainerVisual owner) : System.Collections.ObjectModel.Collection<Visual>
    {
        protected override void InsertItem(int index, Visual item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            item.Parent = owner;
            base.InsertItem(index, item);
        }

        protected override void SetItem(int index, Visual item)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            item.Parent = owner;
            base.SetItem(index, item);
        }

        protected override void RemoveItem(int index)
        {
            this[index].Parent = null;
            base.RemoveItem(index);
        }

        protected override void ClearItems()
        {
            foreach (var item in this)
            {
                item.Parent = null;
            }

            base.ClearItems();
        }
    }
}

/// <summary>A visual effect composed over a rendered subtree.</summary>
public abstract class VisualEffect
{
    /// <summary>The extra space the effect needs around the visual's bounds, in pixels.</summary>
    public abstract float Padding { get; }

    /// <summary>Draws <paramref name="source"/> with this effect.</summary>
    /// <param name="source">The rendered subtree.</param>
    /// <param name="target">The destination surface, already in the visual's local coordinates.</param>
    /// <param name="destination">Where the source's top-left corner should be drawn.</param>
    /// <param name="opacity">The visual's opacity in the range 0..1.</param>
    public abstract void Render(Bitmap source, Graphics target, PointF destination, double opacity);
}

/// <summary>A soft drop shadow painted behind the visual's rendered subtree.</summary>
public sealed class DropShadowEffect : VisualEffect
{
    /// <summary>The shadow offset from the source's top-left corner, in pixels.</summary>
    public PointF Offset { get; set; } = new(4f, 6f);

    /// <summary>A softness radius in pixels; zero draws a crisp offset copy.</summary>
    public float Blur { get; set; } = 6f;

    /// <summary>The shadow colour.</summary>
    public Color Color { get; set; } = Color.Black;

    /// <summary>The shadow opacity in the range 0..1.</summary>
    public double Opacity { get; set; } = 0.45d;

    /// <inheritdoc />
    public override float Padding => Math.Max(0f, Math.Max(Math.Abs(Offset.X), Math.Abs(Offset.Y)) + Blur);

    /// <inheritdoc />
    public override void Render(Bitmap source, Graphics target, PointF destination, double opacity)
    {
        var shadowOpacity = XamlMath.Clamp(Opacity * opacity, 0d, 1d);
        var blur = Math.Max(0f, Blur);

        // 用若干次低 alpha 偏移绘制近似柔化；GDI+ 没有可用的模糊原语，这样足够小且不分配像素缓冲。
        var taps = blur <= 0f ? 1 : 5;
        for (var tap = 0; tap < taps; tap++)
        {
            var angle = Math.PI * 2d * tap / taps;
            var dx = blur <= 0f ? Offset.X : Offset.X + (float)Math.Cos(angle) * blur * 0.5f;
            var dy = blur <= 0f ? Offset.Y : Offset.Y + (float)Math.Sin(angle) * blur * 0.5f;
            DrawTinted(target, source, new PointF(destination.X + dx, destination.Y + dy), shadowOpacity / taps);
        }

        Visual.DrawBitmap(target, source, destination, opacity);
    }

    private void DrawTinted(Graphics target, Bitmap source, PointF destination, double opacity)
    {
        using var attributes = new ImageAttributes();
        var color = Color;
        var matrix = new ColorMatrix
        {
            Matrix00 = 0f,
            Matrix11 = 0f,
            Matrix22 = 0f,
            Matrix33 = (float)XamlMath.Clamp(opacity, 0d, 1d),
            Matrix40 = color.R / 255f,
            Matrix41 = color.G / 255f,
            Matrix42 = color.B / 255f,
        };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        target.DrawImage(
            source,
            new Rectangle((int)Math.Round(destination.X), (int)Math.Round(destination.Y), source.Width, source.Height),
            0f,
            0f,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
    }
}
