using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Lays a bullet control beside a main child, with the bullet vertically centred.
/// </summary>
/// <remarks>
/// Both parts are ordinary controls. A bullet declared directly in XAML becomes the first child; the next child is
/// the decorated content. Assign <see cref="Bullet"/> and <see cref="Child"/> when the two need to be addressed
/// explicitly.
/// </remarks>
public class BulletDecorator : Control
{
    /// <summary>The control drawn beside the main child, vertically centred.</summary>
    public static readonly DependencyProperty BulletProperty = DependencyProperty.Register(
        nameof(Bullet),
        typeof(System.Windows.Forms.Control),
        typeof(BulletDecorator),
        new PropertyMetadata(null, OnPartPropertyChanged));

    /// <summary>The main child, which fills the remaining width.</summary>
    public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        nameof(Child),
        typeof(System.Windows.Forms.Control),
        typeof(BulletDecorator),
        new PropertyMetadata(null, OnPartPropertyChanged));

    private const int BulletSpacing = 6;

    private System.Windows.Forms.Control? _bullet;
    private System.Windows.Forms.Control? _child;

    /// <summary>Creates the decorator with double buffering.</summary>
    public BulletDecorator()
    {
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the control drawn beside the main child.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? Bullet
    {
        get => (System.Windows.Forms.Control?)GetValue(BulletProperty)
               ?? (Controls.Count > 1 ? Controls[0] : null);
        set => SetValue(BulletProperty, value);
    }

    /// <summary>Gets or sets the main child.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.Control? Child
    {
        get => (System.Windows.Forms.Control?)GetValue(ChildProperty) ?? DirectChild();
        set => SetValue(ChildProperty, value);
    }

    /// <inheritdoc />
    public override Size GetPreferredSize(Size proposedSize)
    {
        var padding = Padding;
        var bullet = EffectiveBullet();
        var child = EffectiveChild();

        var bulletSize = bullet is null ? Size.Empty : XamlLayout.Measure(bullet, new Size(0, proposedSize.Height));
        var childSize = child is null ? Size.Empty : XamlLayout.Measure(child, proposedSize);

        var width = Math.Max(0, bulletSize.Width) + (bullet is not null && child is not null ? BulletSpacing : 0) + childSize.Width;
        var height = Math.Max(bulletSize.Height, childSize.Height);
        return new Size(
            width + (int)Math.Ceiling(padding.Horizontal),
            height + (int)Math.Ceiling(padding.Vertical));
    }

    /// <inheritdoc />
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);

        var content = XamlLayout.ContentBounds(this);
        var bullet = EffectiveBullet();
        var child = EffectiveChild();

        if (bullet is null)
        {
            if (child is not null && child.Bounds != content)
            {
                child.Bounds = content;
            }

            return;
        }

        var measuredBullet = XamlLayout.Measure(bullet, new Size(0, content.Height));
        var bulletWidth = bullet.Width > 0 ? bullet.Width : measuredBullet.Width;
        var bulletHeight = bullet.Height > 0 ? bullet.Height : measuredBullet.Height;
        var bulletBounds = new Rectangle(
            content.Left,
            content.Top + Math.Max(0, (content.Height - bulletHeight) / 2),
            Math.Max(0, bulletWidth),
            Math.Max(0, bulletHeight));
        if (bullet.Bounds != bulletBounds)
        {
            bullet.Bounds = bulletBounds;
        }

        if (child is null)
        {
            return;
        }

        var childLeft = content.Left + Math.Max(0, bulletWidth) + BulletSpacing;
        var childBounds = new Rectangle(
            childLeft,
            content.Top,
            Math.Max(0, content.Right - childLeft),
            content.Height);
        if (child.Bounds != childBounds)
        {
            child.Bounds = childBounds;
        }
    }

    private static void OnPartPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is BulletDecorator decorator && !decorator.IsDisposed)
        {
            decorator.ApplyParts();
        }
    }

    private void ApplyParts()
    {
        var nextBullet = GetValue(BulletProperty) as System.Windows.Forms.Control;
        var nextChild = GetValue(ChildProperty) as System.Windows.Forms.Control;
        var previousBullet = _bullet;
        var previousChild = _child;

        _bullet = nextBullet;
        _child = nextChild;

        // DP 是作者面，Controls 是窗口面：被替换掉的宿主部件要离开，其余直接声明的子控件不动。
        foreach (var stale in new[] { previousBullet, previousChild })
        {
            if (stale is null
                || ReferenceEquals(stale, nextBullet)
                || ReferenceEquals(stale, nextChild))
            {
                continue;
            }

            if (Controls.Contains(stale))
            {
                Controls.Remove(stale);
            }
        }

        foreach (var part in new[] { nextBullet, nextChild })
        {
            if (part is not null && !Controls.Contains(part))
            {
                Controls.Add(part);
            }
        }

        PerformLayout();
        Invalidate();
    }

    private System.Windows.Forms.Control? DirectChild()
    {
        for (var index = Controls.Count - 1; index >= 0; index--)
        {
            var current = Controls[index];
            if (!ReferenceEquals(current, EffectiveBullet()))
            {
                return current;
            }
        }

        return null;
    }

    // 两个直接子控件按「项目符号 + 内容」解释；只有一个时它是内容，避免把正文误当成项目符号。
    private System.Windows.Forms.Control? EffectiveBullet()
        => _bullet ?? (Controls.Count > 1 ? Controls[0] : null);

    private System.Windows.Forms.Control? EffectiveChild()
    {
        if (_child is not null)
        {
            return _child;
        }

        return _bullet is null && Controls.Count == 1 ? Controls[0] : DirectChild();
    }
}
