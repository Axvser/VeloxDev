// VeloxDev customization: 悬停高亮 —— 本 demo 自己的一层，叠在共享连线层的上面。
// 适配器的 overlay 只画静息线；这一层订指针事件，拿连线**发布的那条曲线**（ILinkHitTestable.Curve）画光 ——
// 与另外六家「在自己的视图里画」是同一条路，只是这家的线由一层画完，所以效果单独占一层。
using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

/// <summary>The demo's hover highlight: one viewport-sized layer above the shared link layer.</summary>
public sealed class LinkHighlightLayer : GraphicsView, IDrawable
{
    // 与 LinkView relay 同一组偏移：曲线是 canvas-local 的，这一层是视口大小的，所以要过同一次平移。
    public static readonly BindableProperty TreeProperty = BindableProperty.Create(
        nameof(Tree), typeof(IWorkflowTreeViewModel), typeof(LinkHighlightLayer), null, propertyChanged: OnTreeChanged);
    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(
        nameof(ScrollOffsetX), typeof(double), typeof(LinkHighlightLayer), 0d);
    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(
        nameof(ScrollOffsetY), typeof(double), typeof(LinkHighlightLayer), 0d);
    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(
        nameof(ContentOffsetX), typeof(double), typeof(LinkHighlightLayer), 0d);
    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(
        nameof(ContentOffsetY), typeof(double), typeof(LinkHighlightLayer), 0d);
    public static readonly BindableProperty RulerThicknessProperty = BindableProperty.Create(
        nameof(RulerThickness), typeof(double), typeof(LinkHighlightLayer), 0d);
    public static readonly BindableProperty GlowColorProperty = BindableProperty.Create(
        nameof(GlowColor), typeof(Color), typeof(LinkHighlightLayer), Color.FromArgb("#FFFFFFFF"));

    private IWorkflowInputEvents? _input;
    private IWorkflowTreeViewModelHelper? _treeHelper;
    private IWorkflowLinkViewModel? _lit;

    public LinkHighlightLayer()
    {
        // 只画，不吃指针：画布手势照旧归表面。
        InputTransparent = true;
        Drawable = this;
    }

    /// <summary>Gets or sets the tree whose pointer events drive the highlight.</summary>
    public IWorkflowTreeViewModel? Tree { get => (IWorkflowTreeViewModel?)GetValue(TreeProperty); set => SetValue(TreeProperty, value); }

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double RulerThickness { get => (double)GetValue(RulerThicknessProperty); set => SetValue(RulerThicknessProperty, value); }

    /// <summary>Gets or sets the glow's colour. A soft light reads as "lit"; a hue reads as an alarm.</summary>
    public Color GlowColor { get => (Color)GetValue(GlowColorProperty); set => SetValue(GlowColorProperty, value); }

    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // 「谁被指着」由路由裁决；曲线是那条线**自己发布的**那一条，所以这里画的和命中的是同一条。
        if (_lit?.HitTarget()?.Curve is not { Count: > 0 } curve) return;

        // canvas-local → 视口：一条纯平移，与 overlay 用的是同一个式子。
        var ruler = Math.Max(0d, RulerThickness);
        var ox = (float)(ruler + ContentOffsetX - ScrollOffsetX);
        var oy = (float)(ruler + ContentOffsetY - ScrollOffsetY);

        var path = new PathF();
        path.MoveTo((float)curve.XAt(0) + ox, (float)curve.YAt(0) + oy);
        for (var i = 1; i < curve.Count; i++)
        {
            path.LineTo((float)curve.XAt(i) + ox, (float)curve.YAt(i) + oy);
        }

        canvas.StrokeColor = GlowColor.WithAlpha(0.25f);
        canvas.StrokeSize = 9;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(path);
    }

    private static void OnTreeChanged(BindableObject bindable, object oldValue, object newValue)
        => ((LinkHighlightLayer)bindable).HookInput(newValue as IWorkflowTreeViewModel);

    // 换树就换订阅：本层比树活得短，摘钩子时也退订。
    private void HookInput(IWorkflowTreeViewModel? tree)
    {
        UnhookInput();
        if (tree?.GetHelper() is not IWorkflowInputEvents events) return;

        _input = events;
        events.Input.PointerEntered += OnPointerEntered;
        events.Input.PointerExited += OnPointerExited;

        // 线被删掉时指针还停在原地，收不到 Exited —— 这一层于是留着旧的光带像素，直到指针动一下
        //（连线本体不在此列：它在共享那层，靠集合变化就重绘了）。删线的路子不止 Delete：Undo、Agent
        // 改树都算，所以盯 LinkRemoved，不是盯那一次按键。
        _treeHelper = tree.GetHelper();
        _treeHelper.LinkRemoved += OnLinkRemoved;
    }

    private void UnhookInput()
    {
        if (_input is not null)
        {
            _input.Input.PointerEntered -= OnPointerEntered;
            _input.Input.PointerExited -= OnPointerExited;
            _input = null;
        }

        if (_treeHelper is not null)
        {
            _treeHelper.LinkRemoved -= OnLinkRemoved;
            _treeHelper = null;
        }
    }

    private void OnLinkRemoved(object? sender, IWorkflowLinkViewModel link)
    {
        if (!ReferenceEquals(_lit, link)) return;

        _lit = null;
        Invalidate();
    }

    private void OnPointerEntered(object? sender, WorkflowPointerEnteredEventArgs e)
    {
        _lit = e.Target as IWorkflowLinkViewModel;
        Invalidate();
    }

    private void OnPointerExited(object? sender, WorkflowPointerExitedEventArgs e)
    {
        _lit = null;
        Invalidate();
    }
}
