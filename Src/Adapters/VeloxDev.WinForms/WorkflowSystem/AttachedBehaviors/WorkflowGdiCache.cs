using System;
using System.Drawing;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

// 按颜色缓存的 GDI+ 画刷：颜色变化时改写属性，避免每次重绘创建/释放原生句柄。
internal sealed class CachedSolidBrush : IDisposable
{
    private SolidBrush? _brush;

    public SolidBrush Get(Color color)
    {
        if (_brush is null)
        {
            _brush = new SolidBrush(color);
        }
        else if (_brush.Color != color)
        {
            _brush.Color = color;
        }

        return _brush;
    }

    public void Dispose()
    {
        _brush?.Dispose();
        _brush = null;
    }
}

// 按颜色缓存的固定线宽 GDI+ 画笔。
internal sealed class CachedPen : IDisposable
{
    private readonly float _width;
    private Pen? _pen;

    public CachedPen(float width) => _width = width;

    public Pen Get(Color color) => Get(color, _width);

    public Pen Get(Color color, float width)
    {
        if (_pen is null)
        {
            _pen = new Pen(color, width);
            return _pen;
        }

        if (_pen.Color != color) _pen.Color = color;
        if (_pen.Width != width) _pen.Width = width;
        return _pen;
    }

    public void Dispose()
    {
        _pen?.Dispose();
        _pen = null;
    }
}
