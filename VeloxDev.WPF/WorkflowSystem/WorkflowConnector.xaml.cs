using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.Core.Interfaces.Workflow;

namespace VeloxDev.WPF.WorkflowSystem
{
    public partial class WorkflowConnector : UserControl, IWorkflowConnector<Point, DrawingContext>
    {
        private static Queue<WorkflowConnector> connectors = [];
        private readonly HashSet<FrameworkElement> connectionTargets = [];
        private readonly HashSet<FrameworkElement> connectionSources = [];

        public WorkflowConnector()
        {
            InitializeComponent();
            InitializeVeloxDev();
        }

        public Point Center
        {
            get { return (Point)GetValue(CenterProperty); }
            set { SetValue(CenterProperty, value); }
        }
        public static readonly DependencyProperty CenterProperty =
            DependencyProperty.Register(
                "Center",
                typeof(Point),
                typeof(WorkflowConnector),
                new PropertyMetadata(
                    default(Point),
                    _0001_OnCenterChanged));

        public Point Position
        {
            get { return (Point)GetValue(PositionProperty); }
            set { SetValue(PositionProperty, value); }
        }
        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(
                "Position",
                typeof(Point),
                typeof(WorkflowConnector),
                new PropertyMetadata(
                    default(Point),
                    _0001_OnPositionChanged));

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Center = new(ActualWidth / 2, ActualHeight / 2);
        }
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            foreach (var target in connectionTargets)
            {
                OnConnectionRender(
                    this,
                    drawingContext,
                    Center,
                    new Point(
                        (Canvas.GetLeft(target) - Canvas.GetLeft(this) + target.ActualWidth / 2),
                        (Canvas.GetTop(target) - Canvas.GetTop(this)) + target.ActualHeight / 2));
            }
        }
        public virtual void OnConnectionRender(object sender, DrawingContext renderContext, Point from, Point to)
        {
            // 计算差距
            var diffx = to.X - from.X;
            var diffy = to.Y - from.Y;

            // 基于点位差距计算控制点
            var cp1 = new Point(
                from.X + diffx * 0.618,
                from.Y + diffy * 0.1);
            var cp2 = new Point(
                to.X - diffx * 0.618,
                to.Y - diffy * 0.1);

            // 绘制连接线
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(from, false, false);
                ctx.BezierTo(cp1, cp2, to, true, false);
            }

            // 渲染连接线
            var pen = new Pen(Brushes.Cyan, 2);
            renderContext.DrawGeometry(null, pen, geometry);

            // 计算曲线在终点的切线方向（导数）
            // 贝塞尔曲线的导数公式：B'(t) = 3(1-t)^2(P1-P0) + 6(1-t)t(P2-P1) + 3t^2(P3-P2)
            // 在终点处 t=1，所以 B'(1) = 3(P3-P2)
            var tangentX = 3 * (to.X - cp2.X);
            var tangentY = 3 * (to.Y - cp2.Y);

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
            // 箭头尖端在曲线终点
            var arrowTip = to;
            // 箭头两侧点
            var arrowLeft = new Point(
                to.X - arrowSize * tangentX + arrowSize * 0.5 * perpendicularX,
                to.Y - arrowSize * tangentY + arrowSize * 0.5 * perpendicularY);
            var arrowRight = new Point(
                to.X - arrowSize * tangentX - arrowSize * 0.5 * perpendicularX,
                to.Y - arrowSize * tangentY - arrowSize * 0.5 * perpendicularY);

            // 绘制箭头
            var arrowGeometry = new StreamGeometry();
            using (var ctx = arrowGeometry.Open())
            {
                ctx.BeginFigure(arrowTip, true, true);
                ctx.LineTo(arrowLeft, true, false);
                ctx.LineTo(arrowRight, true, false);
            }

            // 渲染箭头
            renderContext.DrawGeometry(Brushes.Black, null, arrowGeometry);
        }

        public void InitializeVeloxDev()
        {
            MouseDown += _0002_MouseDown;
            MouseUp += _0002_MouseUp;
            MouseEnter += _0002_MouseEnter;
            MouseLeave += _0002_MouseLeave;
            Binding bindingCenter = new(nameof(Center))
            {
                Source = DataContextProperty,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            BindingOperations.SetBinding(this, CenterProperty, bindingCenter);
            Binding bindingPosition = new(nameof(Position))
            {
                Source = DataContextProperty,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            BindingOperations.SetBinding(this, PositionProperty, bindingPosition);
        }
        public void ReleaseVeloxDev()
        {
            MouseDown -= _0002_MouseDown;
            MouseUp -= _0002_MouseUp;
            MouseEnter -= _0002_MouseEnter;
            MouseLeave -= _0002_MouseLeave;
            BindingOperations.ClearBinding(this, DataContextProperty);
            BindingOperations.ClearBinding(this, CenterProperty);
            BindingOperations.ClearBinding(this, PositionProperty);
        }

        partial void OnCenterChanged(Point oldValue, Point newValue);
        partial void OnPositionChanged(Point oldValue, Point newValue);


        private static void _0001_OnCenterChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is WorkflowConnector renderer)
            {
                renderer.OnCenterChanged((Point)e.OldValue, (Point)e.NewValue);
                renderer.InvalidateVisual();
                foreach (var source in renderer.connectionSources)
                {
                    source.InvalidateVisual();
                }
            }
        }
        private static void _0001_OnPositionChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is WorkflowConnector renderer)
            {
                var oldPoint = (Point)e.OldValue;
                var newPoint = (Point)e.NewValue;
                Canvas.SetLeft(renderer, newPoint.X);
                Canvas.SetTop(renderer, newPoint.Y);
                renderer.OnPositionChanged(oldPoint, newPoint);
                renderer.InvalidateVisual();
                foreach (var source in renderer.connectionSources)
                {
                    source.InvalidateVisual();
                }
            }
        }

        private int _0002_mousedown_Count = 0;
        private int _0002_mouseup_Count = 0;
        private void _0002_InvokeClick()
        {
            if (_0002_mousedown_Count > 0 && _0002_mouseup_Count > 0)
            {
                _0002_mousedown_Count = 0;
                _0002_mouseup_Count = 0;
                connectors.Enqueue(this);
                if (connectors.Count >= 2)
                {
                    var a = connectors.Dequeue();
                    var b = connectors.Dequeue();
                    a.connectionTargets.Add(b);
                    a.InvalidateVisual();
                    MessageBox.Show("建立连接✔");
                }
            }
        }
        private void _0002_MouseDown(object sender, MouseEventArgs e)
        {
            _0002_mousedown_Count++;
            _0002_InvokeClick();
        }
        private void _0002_MouseUp(object sender, MouseEventArgs e)
        {
            _0002_mouseup_Count++;
            _0002_InvokeClick();
        }
        private void _0002_MouseEnter(object sender, MouseEventArgs e)
        {
            _0002_mousedown_Count = 0;
            _0002_mouseup_Count = 0;
        }
        private void _0002_MouseLeave(object sender, MouseEventArgs e)
        {
            _0002_mousedown_Count = 0;
            _0002_mouseup_Count = 0;
        }
    }
}
