using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views
{
    public partial class BezierCurveView : UserControl
    {
        public BezierCurveView()
        {
            InitializeComponent();
            IsHitTestVisible = false;
            Panel.SetZIndex(this, -100);
        }

        public static readonly DependencyProperty StartAnchorProperty =
            DependencyProperty.Register(
                "StartAnchor",
                typeof(Anchor),
                typeof(BezierCurveView),
                new FrameworkPropertyMetadata(
                    new Anchor(),
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnStartAnchorChanged));

        public static readonly DependencyProperty EndAnchorProperty =
            DependencyProperty.Register(
                "EndAnchor",
                typeof(Anchor),
                typeof(BezierCurveView),
                new FrameworkPropertyMetadata(
                    new Anchor(),
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnEndAnchorChanged));

        public static readonly DependencyProperty CanRenderProperty =
            DependencyProperty.Register(
                "CanRender",
                typeof(bool),
                typeof(BezierCurveView),
                new FrameworkPropertyMetadata(
                    true,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnCanRenderChanged));

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

        private static void OnStartAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (BezierCurveView)d;
            control.OnStartAnchorChanged((Anchor)e.OldValue, (Anchor)e.NewValue);
        }

        private static void OnEndAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (BezierCurveView)d;
            control.OnEndAnchorChanged((Anchor)e.OldValue, (Anchor)e.NewValue);
        }

        private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (BezierCurveView)d;
            control.OnCanRenderChanged((bool)e.OldValue, (bool)e.NewValue);
        }

        private void OnStartAnchorChanged(Anchor oldValue, Anchor newValue)
        {
            InvalidateVisual();
        }

        private void OnEndAnchorChanged(Anchor oldValue, Anchor newValue)
        {
            InvalidateVisual();
        }

        private void OnCanRenderChanged(bool oldValue, bool newValue)
        {
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (!CanRender)
                return;

            // Calculate differences
            var diffx = EndAnchor.Left - StartAnchor.Left;
            var diffy = EndAnchor.Top - StartAnchor.Top;

            // Calculate control points based on position differences
            var cp1 = new Point(
                StartAnchor.Left + diffx * 0.618,
                StartAnchor.Top + diffy * 0.1);
            var cp2 = new Point(
                EndAnchor.Left - diffx * 0.618,
                EndAnchor.Top - diffy * 0.1);

            // Create path geometry for the Bezier curve
            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure
            {
                StartPoint = new Point(StartAnchor.Left, StartAnchor.Top),
                IsClosed = false
            };

            var bezierSegment = new BezierSegment
            {
                Point1 = cp1,
                Point2 = cp2,
                Point3 = new Point(EndAnchor.Left, EndAnchor.Top)
            };

            pathFigure.Segments.Add(bezierSegment);
            pathGeometry.Figures.Add(pathFigure);

            // Render the connection line
            var pen = new Pen(Brushes.Cyan, 2);
            drawingContext.DrawGeometry(null, pen, pathGeometry);
        }
    }
}