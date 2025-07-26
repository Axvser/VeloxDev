using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.Decorators
{
    public partial class ConnectionDecorator : Control
    {
        public ConnectionDecorator()
        {
            IsHitTestVisible = false;
        }

        public static readonly DependencyProperty StartAnchorProperty =
            DependencyProperty.Register(
                "StartAnchor",
                typeof(Anchor),
                typeof(ConnectionDecorator),
                new PropertyMetadata(new Anchor(), _1nner_OnStartAnchorChanged));
        public static void _1nner_OnStartAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConnectionDecorator decorator)
            {
                decorator.OnStartAnchorChanged((Anchor)e.OldValue, (Anchor)e.NewValue);
                decorator.InvalidateVisual();
            }
        }
        public static readonly DependencyProperty EndAnchorProperty =
            DependencyProperty.Register(
                "EndAnchor",
                typeof(Anchor),
                typeof(ConnectionDecorator),
                new PropertyMetadata(new Anchor(), _1nner_OnEndAnchorChanged));
        public static void _1nner_OnEndAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConnectionDecorator decorator)
            {
                decorator.OnEndAnchorChanged((Anchor)e.OldValue, (Anchor)e.NewValue);
                decorator.InvalidateVisual();
            }
        }
        public static readonly DependencyProperty CanRenderProperty =
            DependencyProperty.Register(
                "CanRender",
                typeof(bool),
                typeof(ConnectionDecorator),
                new PropertyMetadata(true));
        public static void _1nner_OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConnectionDecorator decorator)
            {
                MessageBox.Show("CanRender Changed");
                decorator.InvalidateVisual();
            }
        }
        public Anchor StartAnchor
        {
            get => (Anchor)GetValue(StartAnchorProperty);
            set => SetValue(StartAnchorProperty, value);
        }
        public Anchor EndAnchor
        {
            get => (Anchor)GetValue(EndAnchorProperty);
            set => SetValue(EndAnchorProperty, value);
        }
        public bool CanRender
        {
            get { return (bool)GetValue(CanRenderProperty); }
            set { SetValue(CanRenderProperty, value); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (!CanRender) return;
            OnRender(dc, StartAnchor, EndAnchor);

            // 计算差距
            var diffx = EndAnchor.Left - StartAnchor.Left;
            var diffy = EndAnchor.Top - StartAnchor.Top;

            // 基于点位差距计算控制点
            var cp1 = new Point(
                StartAnchor.Left + diffx * 0.618,
                StartAnchor.Top + diffy * 0.1);
            var cp2 = new Point(
                EndAnchor.Left - diffx * 0.618,
                EndAnchor.Top - diffy * 0.1);

            // 绘制连接线
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(StartAnchor.Left, StartAnchor.Top), false, false);
                ctx.BezierTo(cp1, cp2, new Point(EndAnchor.Left, EndAnchor.Top), true, false);
            }

            // 渲染连接线
            var pen = new Pen(Brushes.Cyan, 2);
            dc.DrawGeometry(null, pen, geometry);

            // 计算曲线在终点的切线方向（导数）
            // 贝塞尔曲线的导数公式：B'(t) = 3(1-t)^2(P1-P0) + 6(1-t)t(P2-P1) + 3t^2(P3-P2)
            // 在终点处 t=1，所以 B'(1) = 3(P3-P2)
            var tangentX = 3 * (EndAnchor.Left - cp2.X);
            var tangentY = 3 * (EndAnchor.Top - cp2.Y);

            // 归一化切线向量
            var tangentLength = Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
            if (tangentLength > 0)
            {
                tangentX /= tangentLength;
                tangentY /= tangentLength;
            }

            // 计算垂直于切线的向量（用于箭头两侧点）
            var perpendicularX = -tangentY;
            var perpendicularY = tangentX;

            // 箭头大小
            double arrowSize = 20;

            // 计算箭头三个点
            var arrowTip = new Point(EndAnchor.Left, EndAnchor.Top);
            var arrowLeft = new Point(
                EndAnchor.Left - arrowSize * tangentX + arrowSize * 0.5 * perpendicularX,
                EndAnchor.Top - arrowSize * tangentY + arrowSize * 0.5 * perpendicularY);
            var arrowRight = new Point(
                EndAnchor.Left - arrowSize * tangentX - arrowSize * 0.5 * perpendicularX,
                EndAnchor.Top - arrowSize * tangentY - arrowSize * 0.5 * perpendicularY);

            // 绘制箭头
            var arrowGeometry = new StreamGeometry();
            using (var ctx = arrowGeometry.Open())
            {
                ctx.BeginFigure(arrowTip, true, true);
                ctx.LineTo(arrowLeft, true, false);
                ctx.LineTo(arrowRight, true, false);
            }

            // 渲染箭头
            dc.DrawGeometry(Brushes.Red, null, arrowGeometry);
        }

        partial void OnRender(DrawingContext dc, Anchor start, Anchor end);
        partial void OnStartAnchorChanged(Anchor oldValue, Anchor newValue);
        partial void OnEndAnchorChanged(Anchor oldValue, Anchor newValue);
    }
}
