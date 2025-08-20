using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using VeloxDev.Core.WorkflowSystem;

namespace WpfApp2.Decorators
{
    public class ConnectionShape : Shape
    {
        static ConnectionShape()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ConnectionShape),
                new FrameworkPropertyMetadata(typeof(ConnectionShape)));
        }

        #region 依赖属性 (保留原有所有属性)

        public static readonly DependencyProperty StartAnchorProperty =
            DependencyProperty.Register(
                "StartAnchor",
                typeof(Anchor),
                typeof(ConnectionShape),
                new FrameworkPropertyMetadata(
                    new Anchor(),
                    FrameworkPropertyMetadataOptions.AffectsRender |
                    FrameworkPropertyMetadataOptions.AffectsArrange,
                    OnAnchorChanged));

        public static readonly DependencyProperty EndAnchorProperty =
            DependencyProperty.Register(
                "EndAnchor",
                typeof(Anchor),
                typeof(ConnectionShape),
                new FrameworkPropertyMetadata(
                    new Anchor(),
                    FrameworkPropertyMetadataOptions.AffectsRender |
                    FrameworkPropertyMetadataOptions.AffectsArrange,
                    OnAnchorChanged));

        public static readonly DependencyProperty CanRenderProperty =
            DependencyProperty.Register(
                "CanRender",
                typeof(bool),
                typeof(ConnectionShape),
                new FrameworkPropertyMetadata(
                    true,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnCanRenderChanged));

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register(
                "IsSelected",
                typeof(bool),
                typeof(ConnectionShape),
                new FrameworkPropertyMetadata(
                    false,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnIsSelectedChanged));

        #endregion

        #region 属性访问器

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
            get => (bool)GetValue(CanRenderProperty);
            set => SetValue(CanRenderProperty, value);
        }

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        #endregion

        private readonly StreamGeometry _geometry;
        private readonly StreamGeometry _arrowGeometry;

        public ConnectionShape()
        {
            // 初始化默认样式 (保持原有外观)
            Stroke = Brushes.Cyan;
            StrokeThickness = 2;
            Fill = Brushes.Violet;

            // 初始化几何对象
            _geometry = new StreamGeometry();
            _arrowGeometry = new StreamGeometry();

            // 确保不可见时不影响布局
            Visibility = CanRender ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override Geometry DefiningGeometry
        {
            get
            {
                if (!CanRender)
                    return Geometry.Empty;

                var group = new GeometryGroup();
                group.Children.Add(_geometry);
                group.Children.Add(_arrowGeometry);
                return group;
            }
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
        {
            if (!CanRender)
                return System.Windows.Size.Empty;

            double width = Math.Max(StartAnchor.Left, EndAnchor.Left);
            double height = Math.Max(StartAnchor.Top, EndAnchor.Top);
            return new System.Windows.Size(width, height);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!CanRender)
                return;

            // 根据选中状态更新外观
            if (IsSelected)
            {
                Stroke = Brushes.Red;
                StrokeThickness = 3;
            }
            else
            {
                Stroke = Brushes.Cyan;
                StrokeThickness = 2;
            }

            base.OnRender(drawingContext);
        }

        #region 属性变更处理 (保留原有逻辑)

        private static void OnAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var shape = (ConnectionShape)d;
            shape.UpdateGeometry();
        }

        private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var shape = (ConnectionShape)d;
            shape.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            shape.InvalidateVisual();
        }

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ConnectionShape)d).InvalidateVisual();
        }

        #endregion

        private void UpdateGeometry()
        {
            if (!CanRender)
                return;

            // 计算控制点 (保持原有算法)
            var diffx = EndAnchor.Left - StartAnchor.Left;
            var diffy = EndAnchor.Top - StartAnchor.Top;

            var cp1 = new Point(
                StartAnchor.Left + diffx * 0.618,
                StartAnchor.Top + diffy * 0.1);

            var cp2 = new Point(
                EndAnchor.Left - diffx * 0.618,
                EndAnchor.Top - diffy * 0.1);

            // 更新主连接线几何
            using (var ctx = _geometry.Open())
            {
                ctx.BeginFigure(new Point(StartAnchor.Left, StartAnchor.Top), false, false);
                ctx.BezierTo(cp1, cp2, new Point(EndAnchor.Left, EndAnchor.Top), true, false);
            }

            // 计算箭头几何 (保持原有算法)
            var tangentX = 3 * (EndAnchor.Left - cp2.X);
            var tangentY = 3 * (EndAnchor.Top - cp2.Y);

            var tangentLength = Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
            if (tangentLength > 0)
            {
                tangentX /= tangentLength;
                tangentY /= tangentLength;
            }

            var perpendicularX = -tangentY;
            var perpendicularY = tangentX;

            double arrowSize = 20;
            var arrowTip = new Point(EndAnchor.Left, EndAnchor.Top);
            var arrowLeft = new Point(
                EndAnchor.Left - arrowSize * tangentX + arrowSize * 0.5 * perpendicularX,
                EndAnchor.Top - arrowSize * tangentY + arrowSize * 0.5 * perpendicularY);
            var arrowRight = new Point(
                EndAnchor.Left - arrowSize * tangentX - arrowSize * 0.5 * perpendicularX,
                EndAnchor.Top - arrowSize * tangentY - arrowSize * 0.5 * perpendicularY);

            // 更新箭头几何
            using (var ctx = _arrowGeometry.Open())
            {
                ctx.BeginFigure(arrowTip, true, true);
                ctx.LineTo(arrowLeft, true, false);
                ctx.LineTo(arrowRight, true, false);
            }

            InvalidateVisual();
            InvalidateMeasure();
        }

        #region 命中测试 (新增功能)

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
        {
            if (!CanRender || !IsHitTestVisible)
                return null;

            var point = hitTestParameters.HitPoint;
            if (IsPointNearConnection(point))
                return new PointHitTestResult(this, point);

            return null;
        }

        private bool IsPointNearConnection(Point point)
        {
            // 简化的命中测试 - 实际项目中应实现更精确的几何测试
            const double hitTestTolerance = 10.0;

            // 计算点到线段的距离
            return DistanceToSegment(point,
                new Point(StartAnchor.Left, StartAnchor.Top),
                new Point(EndAnchor.Left, EndAnchor.Top)) < hitTestTolerance;
        }

        private static double DistanceToSegment(Point pt, Point p1, Point p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;

            if (dx == 0 && dy == 0)
                return Math.Sqrt((pt.X - p1.X) * (pt.X - p1.X) + (pt.Y - p1.Y) * (pt.Y - p1.Y));

            double t = ((pt.X - p1.X) * dx + (pt.Y - p1.Y) * dy) / (dx * dx + dy * dy);

            if (t < 0)
            {
                dx = pt.X - p1.X;
                dy = pt.Y - p1.Y;
            }
            else if (t > 1)
            {
                dx = pt.X - p2.X;
                dy = pt.Y - p2.Y;
            }
            else
            {
                dx = pt.X - (p1.X + t * dx);
                dy = pt.Y - (p1.Y + t * dy);
            }

            return Math.Sqrt(dx * dx + dy * dy);
        }

        #endregion
    }
}