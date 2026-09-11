using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using Windows.Foundation;
using Windows.UI;

namespace Demo
{
    /// <summary>
    /// 采样器演示台上的被写对象：一个真正在视觉树里的控件，每条采样器一条**依赖属性**，类型与产物完全一致。
    /// </summary>
    /// <remarks>
    /// 换成控件而不是一个私有的 scratch 类，是为了让"采样器把值写到哪"这件事可被验证：属性是框架属性系统的真成员
    /// （依赖属性），采样器写它时走的是真实的属性通道，验收再从这个属性读回来 —— 于是断言依据的是**界面上那个
    /// 控件实际持有的值**，而不是一个屏幕外的对象。每一格只被它那条采样器写，<see cref="Kind"/> 告诉这一格该把
    /// 哪条属性画出来。
    /// <para>
    /// <b>WinUI 没有 OnRender。</b> WPF 那版继承 <c>FrameworkElement</c> 再在 <c>OnRender</c> 里画，是因为 WPF 的
    /// <c>UIElement</c> 是保留式绘图：重绘由框架在 <c>AffectsRender</c> 之后回调给你。WinUI 3 是合成式的
    /// （composition-based），<c>UIElement</c> 上根本没有 <c>OnRender</c> 这个重写点 —— 能"画"的只有视觉树里的
    /// 子元素，或合成层里的视觉。所以这一版换了一条路：本控件（一个 <see cref="Grid"/>）**自己组子元素** ——
    /// 一个 <see cref="Border"/> 作被画物、里面套一个两列的 <see cref="Grid"/> 承载栅格长度那一列 —— 依赖属性的
    /// 回调把值写到**子元素**的属性上。写子元素的属性必然重绘，因为那正是 XAML 自己的属性通道，不需要谁记得去
    /// 调一次重绘。
    /// </para>
    /// <para>
    /// 另一条路是 <c>Microsoft.UI.Composition</c> 的形状（<c>CompositionSpriteShape</c> 之流）。它同样能画，但
    /// 采样器的产物是框架对象（<see cref="Brush"/> / <see cref="Transform"/> / <see cref="PlaneProjection"/>），
    /// 要落到合成层就得先把它们翻译成合成类型 —— 而组 XAML 子元素能让采样器写下的**那一个实例**直接成为屏幕上的
    /// 东西，这正是这块台子要证明的事。
    /// </para>
    /// <para>
    /// <b>绘制是有标尺的。</b>位移类端点跑到 220，格子只有 96×62，按原值画会一步跨出格子被裁掉 —— 那看上去
    /// 和"没动"一模一样。所以位置与尺寸在**绘制反应里**乘一个固定缩放，而属性本身持有的仍是原值：载荷读的是
    /// 属性，于是断言的是原值，缩放只影响"怎么画"。栅格长度是尺寸类的，同样按标尺画：端点 10 → 110 像素，原值会把
    /// 内列撑得比格子还宽。
    /// </para>
    /// <para>
    /// 与 WPF 版的框架差异还有两处：WinUI 的 <see cref="Border"/> 没有 <c>Effect</c>（阴影），而本适配器也不发布
    /// 阴影采样器；投影是 WinUI 真实存在的 <see cref="UIElement.Projection"/>，那条采样器因此能真画出来。而
    /// <c>Projection</c> 与 <c>RenderTransform</c>（Scale）在同一个元素上互斥，所以反应里写其中一个之前，先把
    /// 另一个清掉。
    /// </para>
    /// </remarks>
    internal sealed class SamplerSubject : Grid
    {
        /// <summary>
        /// 位移与尺寸的像素缩放。
        /// </summary>
        /// <remarks>
        /// 这一组端点里最大的位置分量是 220（Point 与 Transform 的 Y；投影的 GlobalOffsetY 同样是 220），而格子除去
        /// 被写对象本身、再除去左上角基准只剩 <c>62 − 6 − 18 = 38</c> 像素的行程。演出用的 Back.Out 峰值约 1.1，
        /// 所以端点最多会被推到 <c>220 × 1.1 = 242</c>；<c>38 / 242 ≈ 0.157</c>，取 <c>0.15</c> 让<b>过冲的峰值也
        /// 留在格内</b>：<c>6 + 242 × 0.15 + 18 = 60.3 ≤ 62</c>。
        /// <para>
        /// 基准取左上角而不是格心：行程只朝正方向走，从格心起步会把后半段挤出格底、被裁掉 —— 那看上去反倒像
        /// "没动"，正是这块台子要避免的错觉。
        /// </para>
        /// </remarks>
        internal const double DrawScale = 0.15d;

        private const double BaseLeft = 6d;
        private const double BaseTop = 6d;
        private const double BaseWidth = 26d;
        private const double BaseHeight = 18d;

        /// <summary>格里那一列的底色：栅格长度采样器靠它把"列在变宽"显出来。</summary>
        private static readonly Color BarColor = Color.FromArgb(0xFF, 0xFF, 0x9A, 0x4D);

        /// <summary>没被任何采样器写过时的底色。</summary>
        private static readonly Color RestColor = Color.FromArgb(0xFF, 0x80, 0x80, 0x80);

        /// <summary>这一格显示哪一条采样器的产物。构造时定一次，此后不变。</summary>
        internal required string Kind { get; init; }

        public static readonly DependencyProperty FillProperty =
            Register(nameof(Fill), typeof(Brush), null);

        public static readonly DependencyProperty TintProperty =
            Register(nameof(Tint), typeof(Color), RestColor);

        public static readonly DependencyProperty CornersProperty =
            Register(nameof(Corners), typeof(CornerRadius), new CornerRadius(2));

        /// <summary>栅格长度。名字不叫 <c>Column</c>：<see cref="Grid"/> 已经用那个名字占了附加属性 <c>ColumnProperty</c>。</summary>
        public static readonly DependencyProperty LengthProperty =
            Register(nameof(Length), typeof(GridLength), new GridLength(0, GridUnitType.Pixel));

        public static readonly DependencyProperty AnchorProperty =
            Register(nameof(Anchor), typeof(Point), new Point(0, 0));

        public static readonly DependencyProperty BoundsProperty =
            Register(nameof(Bounds), typeof(Rect), new Rect(0, 0, 0, 0));

        public static readonly DependencyProperty ExtentProperty =
            Register(nameof(Extent), typeof(Size), new Size(0, 0));

        /// <summary>厚度。名字不叫 <c>Margin</c>：<see cref="FrameworkElement"/> 已经占用了那个名字。</summary>
        public static readonly DependencyProperty InsetProperty =
            Register(nameof(Inset), typeof(Thickness), new Thickness(0));

        /// <summary>投影。名字不叫 <c>Projection</c>：<see cref="UIElement"/> 已经占用了那个名字。</summary>
        public static readonly DependencyProperty TiltProperty =
            Register(nameof(Tilt), typeof(PlaneProjection), null);

        /// <summary>变换。名字不叫 <c>RenderTransform</c>：<see cref="UIElement"/> 已经占用了那个名字。</summary>
        public static readonly DependencyProperty RenderProperty =
            Register(nameof(Render), typeof(Transform), null);

        public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

        public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }

        public CornerRadius Corners { get => (CornerRadius)GetValue(CornersProperty); set => SetValue(CornersProperty, value); }

        public GridLength Length { get => (GridLength)GetValue(LengthProperty); set => SetValue(LengthProperty, value); }

        public Point Anchor { get => (Point)GetValue(AnchorProperty); set => SetValue(AnchorProperty, value); }

        public Rect Bounds { get => (Rect)GetValue(BoundsProperty); set => SetValue(BoundsProperty, value); }

        public Size Extent { get => (Size)GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

        public Thickness Inset { get => (Thickness)GetValue(InsetProperty); set => SetValue(InsetProperty, value); }

        public PlaneProjection? Tilt { get => (PlaneProjection?)GetValue(TiltProperty); set => SetValue(TiltProperty, value); }

        public Transform? Render { get => (Transform?)GetValue(RenderProperty); set => SetValue(RenderProperty, value); }

        /// <summary>被画的那个子元素：底色、圆角、变换、投影都落在它身上。</summary>
        private readonly Border _shape;

        /// <summary>栅格长度要撑的那一列。</summary>
        private readonly ColumnDefinition _bar;

        public SamplerSubject()
        {
            Canvas.SetLeft(this, BaseLeft);
            Canvas.SetTop(this, BaseTop);
            Width = BaseWidth;
            Height = BaseHeight;

            // 栅格长度那一列：左边这一列就是采样器写出来的 GridLength，右边把余下宽度吃掉。
            _bar = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Pixel) };
            var rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            var barFill = new Rectangle
            {
                Fill = new SolidColorBrush(BarColor),
                RadiusX = 1,
                RadiusY = 1,
            };

            var inner = new Grid();
            inner.ColumnDefinitions.Add(_bar);
            inner.ColumnDefinitions.Add(rest);
            Grid.SetColumn(barFill, 0);
            inner.Children.Add(barFill);

            _shape = new Border
            {
                Background = new SolidColorBrush(RestColor),
                CornerRadius = new CornerRadius(2),
                Child = inner,
            };

            Children.Add(_shape);
        }

        /// <summary>
        /// 采样器写进来之后，把这个值**换算成格子里画得下的样子**。
        /// </summary>
        /// <remarks>
        /// 这是"反应"，不是"值"：属性持有的是采样器写下的原值（载荷读的就是它），这里只把它落到子元素的属性上。
        /// 三处框架约束在这里落地：投影与渲染变换互斥，所以先清一个再写另一个；<c>Projection</c> 必须抄新实例，
        /// 采样器把同一个投影就地改、由属性引用着，直接改它的位移就等于改了采样器写出来的那个值。
        /// </remarks>
        internal void Reposition()
        {
            // 依赖属性回调可能在字段赋值之前到达（构造途中、或基类初始化写属性），那时还没有可写的子元素。
            if (_shape is null) return;

            var (x, y) = Kind switch
            {
                SamplerProbe.Kinds.PointSampler => (Anchor.X, Anchor.Y),
                SamplerProbe.Kinds.RectSampler => (Bounds.X, Bounds.Y),
                _ => (0d, 0d),
            };

            Canvas.SetLeft(this, BaseLeft + x * DrawScale);
            Canvas.SetTop(this, BaseTop + y * DrawScale);

            var (width, height) = Kind switch
            {
                SamplerProbe.Kinds.SizeSampler => (Extent.Width * DrawScale, Extent.Height * DrawScale),
                SamplerProbe.Kinds.RectSampler => (Bounds.Width * DrawScale, Bounds.Height * DrawScale),
                _ => (BaseWidth, BaseHeight),
            };

            // 画不出 0 宽的东西；"停在下界"这件事本身已经由载荷里的数字如实报告了。
            Width = Math.Max(1d, width);
            Height = Math.Max(1d, height);

            // 同样按标尺画：边距的端点也跑到了 330/440，原值会把被写对象整个推出格子。
            Margin = new Thickness(Inset.Left * DrawScale, Inset.Top * DrawScale, Inset.Right * DrawScale, Inset.Bottom * DrawScale);

            _shape.Background = Fill ?? new SolidColorBrush(Tint);
            _shape.CornerRadius = Corners;

            _bar.Width = Length.IsAbsolute
                ? new GridLength(Math.Max(0d, Length.Value * DrawScale), GridUnitType.Pixel)
                : Length;

            // Projection 与 RenderTransform 在同一个元素上互斥：写其中一个之前必须先把另一个清掉，否则抛异常。
            _shape.Projection = null;
            _shape.RenderTransform = null;

            if (Kind == SamplerProbe.Kinds.ProjectionSampler && Tilt is not null)
            {
                // 投影是真实存在的属性，所以这一条真画：三个旋转角按原值（度数没有格子装不下的问题），
                // 三个全局位移是位移量，按标尺缩。
                _shape.Projection = ScaleProjection(Tilt);
                return;
            }

            _shape.RenderTransform = Render switch
            {
                TranslateTransform translate => new TranslateTransform
                {
                    X = translate.X * DrawScale,
                    Y = translate.Y * DrawScale,
                },
                Transform transform => transform,
                _ => null,
            };
        }

        /// <summary>
        /// 抄一份投影，只把三个全局位移按标尺缩小。
        /// </summary>
        /// <remarks>
        /// 必须抄新的实例：采样器把同一个 <see cref="PlaneProjection"/> 就地改、由属性引用着，直接改了它的位移
        /// 就等于改了采样器写出来的那个值 —— 而"载荷报的就是屏幕上画的"这条性质正是靠两边读同一个返回值维持的。
        /// </remarks>
        private static PlaneProjection ScaleProjection(PlaneProjection source)
        {
            return new PlaneProjection
            {
                RotationX = source.RotationX,
                RotationY = source.RotationY,
                RotationZ = source.RotationZ,
                CenterOfRotationX = source.CenterOfRotationX,
                CenterOfRotationY = source.CenterOfRotationY,
                CenterOfRotationZ = source.CenterOfRotationZ,
                GlobalOffsetX = source.GlobalOffsetX * DrawScale,
                GlobalOffsetY = source.GlobalOffsetY * DrawScale,
                GlobalOffsetZ = source.GlobalOffsetZ * DrawScale,
            };
        }

        /// <summary>
        /// 注册一条依赖属性。每条属性写入都重算这一格的显示 —— 不只是位置类的那些：厚度、列宽、投影、变换也是在
        /// <see cref="Reposition"/> 里落到子元素上的。重算是幂等的，多算一次不花钱。
        /// </summary>
        /// <remarks>
        /// 这里的回调就是 WPF 那版 <c>AffectsRender</c> 的替身：WinUI 没有"安排重绘"这个选项，改为把值写进子元素的
        /// 真实属性 —— 重绘是属性系统给的保证，不是我们记得去调。
        /// </remarks>
        private static DependencyProperty Register(string name, Type type, object? defaultValue)
            => DependencyProperty.Register(
                name,
                type,
                typeof(SamplerSubject),
                new PropertyMetadata(defaultValue, static (target, _) => ((SamplerSubject)target).Reposition()));
    }
}
