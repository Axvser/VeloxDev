using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.WPF.PlatformAdapters;

namespace WpfApp2.Decorators
{
    public partial class ConnectionDecorator : Control
    {
        public ConnectionDecorator()
        {
            IsHitTestVisible = false;
            Panel.SetZIndex(this, -100);
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
        }

        partial void OnRender(DrawingContext dc, Anchor start, Anchor end);
        partial void OnStartAnchorChanged(Anchor oldValue, Anchor newValue);
        partial void OnEndAnchorChanged(Anchor oldValue, Anchor newValue);
    }
}
