using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using VeloxDev.Core.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views
{
    public sealed partial class BezierCurveView : UserControl
    {
        public BezierCurveView()
        {
            InitializeComponent();
        }

        #region 依赖属性

        public Anchor StartAnchor
        {
            get => (Anchor)GetValue(StartAnchorProperty);
            set => SetValue(StartAnchorProperty, value);
        }
        public static readonly DependencyProperty StartAnchorProperty =
            DependencyProperty.Register(nameof(StartAnchor), typeof(Anchor), typeof(BezierCurveView),
                new PropertyMetadata(new Anchor(), OnAnchorChanged));

        public Anchor EndAnchor
        {
            get => (Anchor)GetValue(EndAnchorProperty);
            set => SetValue(EndAnchorProperty, value);
        }
        public static readonly DependencyProperty EndAnchorProperty =
            DependencyProperty.Register(nameof(EndAnchor), typeof(Anchor), typeof(BezierCurveView),
                new PropertyMetadata(new Anchor(), OnAnchorChanged));

        public bool CanRender
        {
            get => (bool)GetValue(CanRenderProperty);
            set => SetValue(CanRenderProperty, value);
        }
        public static readonly DependencyProperty CanRenderProperty =
            DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(BezierCurveView),
                new PropertyMetadata(true, OnCanRenderChanged));

        #endregion

        private static void OnAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BezierCurveView view)
                view.UpdateCurve();
        }

        private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BezierCurveView view)
                view.UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            CurvePath.Visibility = CanRender ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateCurve()
        {
            if (!CanRender)
            {
                CurvePath.Visibility = Visibility.Collapsed;
                return;
            }

            var start = new Point(StartAnchor.Left, StartAnchor.Top);
            var end = new Point(EndAnchor.Left, EndAnchor.Top);

            // 控制点计算逻辑（根据 X 方向距离自动拉伸）
            double dx = Math.Abs(end.X - start.X);
            double controlOffset = Math.Max(40, dx * 0.5);

            var c1 = new Point(start.X + controlOffset, start.Y);
            var c2 = new Point(end.X - controlOffset, end.Y);

            var figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(new BezierSegment
            {
                Point1 = c1,
                Point2 = c2,
                Point3 = end
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            CurvePath.Data = geometry;

            CurvePath.Visibility = Visibility.Visible;
        }
    }
}
