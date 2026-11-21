using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Something that paints itself into a rectangle, such as a geometry with its brush and pen.</summary>
public abstract class Drawing
{
    /// <summary>Paints this drawing across <paramref name="bounds"/>.</summary>
    /// <param name="graphics">The target surface.</param>
    /// <param name="bounds">The space the drawing may use.</param>
    public abstract void Draw(Graphics graphics, RectangleF bounds);
}

/// <summary>A filled and/or stroked <see cref="Geometry"/>.</summary>
public sealed class GeometryDrawing : Drawing
{
    /// <summary>The geometry to paint.</summary>
    public Geometry? Geometry { get; set; }

    /// <summary>The interior brush; a null brush leaves the interior empty.</summary>
    public Brush? Brush { get; set; }

    /// <summary>The outline pen; a null pen draws no outline.</summary>
    public Pen? Pen { get; set; }

    /// <inheritdoc />
    public override void Draw(Graphics graphics, RectangleF bounds)
    {
        var geometry = Geometry;
        if (geometry is null)
        {
            return;
        }

        using var path = geometry.ToPath(bounds);
        if (path.PointCount == 0)
        {
            return;
        }

        Brush?.FillPath(graphics, path);
        Pen?.Draw(graphics, path);
    }
}

/// <summary>Several drawings painted one after another.</summary>
public sealed class DrawingGroup : Drawing
{
    /// <summary>The child drawings, painted in declaration order.</summary>
    public IList<Drawing> Children { get; } = new List<Drawing>();

    /// <inheritdoc />
    public override void Draw(Graphics graphics, RectangleF bounds)
    {
        foreach (var child in Children)
        {
            child?.Draw(graphics, bounds);
        }
    }
}

/// <summary>
/// A bitmap-shaped resource an <see cref="ImageBrush"/> or image control can paint.
/// </summary>
public abstract class ImageSource
{
    /// <summary>Returns the image to paint, or <see langword="null"/> when the source has nothing to show.</summary>
    /// <remarks>
    /// The returned image stays owned by the source; callers must not dispose it.
    /// </remarks>
    public abstract System.Drawing.Image? Load();
}

/// <summary>An image source backed by a file path, a URI or an already loaded image.</summary>
/// <remarks>
/// A file or URI is read once and cached; the source owns that loaded image and disposes it. An image assigned to
/// <see cref="Image"/> is used as-is and is not disposed by this source.
/// </remarks>
public sealed class BitmapImage : ImageSource, IDisposable
{
    private System.Drawing.Image? _image;
    private System.Drawing.Image? _loaded;
    private string? _path;
    private Uri? _uri;

    /// <summary>A file path to load.</summary>
    public string? Path
    {
        get => _path;
        set
        {
            if (!string.Equals(_path, value, StringComparison.Ordinal))
            {
                ReleaseLoaded();
            }

            _path = value;
        }
    }

    /// <summary>A URI to load; only file URIs are read.</summary>
    public Uri? Uri
    {
        get => _uri;
        set
        {
            if (!Equals(_uri, value))
            {
                ReleaseLoaded();
            }

            _uri = value;
        }
    }

    /// <summary>An already loaded image; assigning it does not transfer ownership.</summary>
    public System.Drawing.Image? Image
    {
        get => _image;
        set
        {
            if (!ReferenceEquals(_image, value))
            {
                ReleaseLoaded();
            }

            _image = value;
        }
    }

    /// <inheritdoc />
    public override System.Drawing.Image? Load()
    {
        if (Image is not null)
        {
            return Image;
        }

        if (_loaded is not null)
        {
            return _loaded;
        }

        var path = Path;
        if (string.IsNullOrWhiteSpace(path) && Uri is { IsFile: true } fileUri)
        {
            path = fileUri.LocalPath;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            _loaded = System.Drawing.Image.FromFile(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            _loaded = null;
        }

        return _loaded;
    }

    /// <inheritdoc />
    public void Dispose() => ReleaseLoaded();

    private void ReleaseLoaded()
    {
        _loaded?.Dispose();
        _loaded = null;
    }
}

/// <summary>An image source that renders a <see cref="Drawing"/> into a bitmap of <see cref="Width"/> by <see cref="Height"/>.</summary>
/// <remarks>A drawing has no intrinsic size, so the source supplies one; the rendered bitmap is cached per drawing and size.</remarks>
public sealed class DrawingImage : ImageSource, IDisposable
{
    private Drawing? _drawing;
    private System.Drawing.Bitmap? _bitmap;
    private int _width = 64;
    private int _height = 64;

    /// <summary>The drawing to render.</summary>
    public Drawing? Drawing
    {
        get => _drawing;
        set
        {
            if (!ReferenceEquals(_drawing, value))
            {
                ReleaseBitmap();
            }

            _drawing = value;
        }
    }

    /// <summary>The rendered width in pixels; 64 by default.</summary>
    public int Width
    {
        get => _width;
        set
        {
            if (_width != value)
            {
                ReleaseBitmap();
            }

            _width = value;
        }
    }

    /// <summary>The rendered height in pixels; 64 by default.</summary>
    public int Height
    {
        get => _height;
        set
        {
            if (_height != value)
            {
                ReleaseBitmap();
            }

            _height = value;
        }
    }

    /// <inheritdoc />
    public override System.Drawing.Image? Load()
    {
        if (_bitmap is not null)
        {
            return _bitmap;
        }

        var drawing = Drawing;
        if (drawing is null || Width <= 0 || Height <= 0)
        {
            return null;
        }

        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            drawing.Draw(graphics, new RectangleF(0f, 0f, Width, Height));
        }

        _bitmap = bitmap;
        return _bitmap;
    }

    /// <inheritdoc />
    public void Dispose() => ReleaseBitmap();

    private void ReleaseBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
