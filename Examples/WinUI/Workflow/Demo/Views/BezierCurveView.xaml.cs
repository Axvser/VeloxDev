using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Numerics;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class BezierCurveView : UserControl
    {
        public BezierCurveView()
        {
            this.InitializeComponent();
            Canvas.ClearColor = Colors.Transparent;
        }

        // ========= 依赖属性定义 =========

        public Anchor StartAnchor
        {
            get => (Anchor)GetValue(StartAnchorProperty);
            set => SetValue(StartAnchorProperty, value);
        }

        public static readonly DependencyProperty StartAnchorProperty =
            DependencyProperty.Register(nameof(StartAnchor),
                typeof(Anchor),
                typeof(BezierCurveView),
                new PropertyMetadata(new Anchor(), OnAnchorChanged));

        public Anchor EndAnchor
        {
            get => (Anchor)GetValue(EndAnchorProperty);
            set => SetValue(EndAnchorProperty, value);
        }

        public static readonly DependencyProperty EndAnchorProperty =
            DependencyProperty.Register(nameof(EndAnchor),
                typeof(Anchor),
                typeof(BezierCurveView),
                new PropertyMetadata(new Anchor(), OnAnchorChanged));

        public bool CanRender
        {
            get => (bool)GetValue(CanRenderProperty);
            set => SetValue(CanRenderProperty, value);
        }

        public static readonly DependencyProperty CanRenderProperty =
            DependencyProperty.Register(nameof(CanRender),
                typeof(bool),
                typeof(BezierCurveView),
                new PropertyMetadata(true, OnCanRenderChanged));

        // ========= 属性变更处理 =========

        private static void OnAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BezierCurveView view)
                view.Canvas.Invalidate();
        }

        private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BezierCurveView view)
                view.Canvas.Invalidate();
        }

        // ========= 绘制 =========

        private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (!CanRender)
                return;

            var ds = args.DrawingSession;
            ds.Antialiasing = Microsoft.Graphics.Canvas.CanvasAntialiasing.Antialiased;

            // 计算差距
            float diffx = (float)(EndAnchor.Left - StartAnchor.Left);
            float diffy = (float)(EndAnchor.Top - StartAnchor.Top);

            // 控制点
            var cp1 = new Vector2(
                (float)(StartAnchor.Left + diffx * 0.618),
                (float)(StartAnchor.Top + diffy * 0.1));
            var cp2 = new Vector2(
                (float)(EndAnchor.Left - diffx * 0.618),
                (float)(EndAnchor.Top - diffy * 0.1));

            var start = new Vector2((float)StartAnchor.Left, (float)StartAnchor.Top);
            var end = new Vector2((float)EndAnchor.Left, (float)EndAnchor.Top);

            // 用 PathBuilder 构造贝塞尔几何
            using var pathBuilder = new Microsoft.Graphics.Canvas.Geometry.CanvasPathBuilder(sender);
            pathBuilder.BeginFigure(start);
            pathBuilder.AddCubicBezier(cp1, cp2, end);
            pathBuilder.EndFigure(Microsoft.Graphics.Canvas.Geometry.CanvasFigureLoop.Open);

            using var geometry = Microsoft.Graphics.Canvas.Geometry.CanvasGeometry.CreatePath(pathBuilder);
            ds.DrawGeometry(geometry, Colors.Cyan, 2f);
        }
    }
}
