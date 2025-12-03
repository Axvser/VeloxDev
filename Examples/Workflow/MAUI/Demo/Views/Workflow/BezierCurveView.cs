namespace Demo.Views.Workflow
{
    public partial class BezierCurveView : GraphicsView
    {
        public static readonly BindableProperty StartLeftProperty =
            BindableProperty.Create(nameof(StartLeft), typeof(double), typeof(BezierCurveView), 0d,
                propertyChanged: OnRenderPropertyChanged);

        public static readonly BindableProperty StartTopProperty =
            BindableProperty.Create(nameof(StartTop), typeof(double), typeof(BezierCurveView), 0d,
                propertyChanged: OnRenderPropertyChanged);

        public static readonly BindableProperty EndLeftProperty =
            BindableProperty.Create(nameof(EndLeft), typeof(double), typeof(BezierCurveView), 0d,
                propertyChanged: OnRenderPropertyChanged);

        public static readonly BindableProperty EndTopProperty =
            BindableProperty.Create(nameof(EndTop), typeof(double), typeof(BezierCurveView), 0d,
                propertyChanged: OnRenderPropertyChanged);

        public static readonly BindableProperty CanRenderProperty =
            BindableProperty.Create(nameof(CanRender), typeof(bool), typeof(BezierCurveView), true,
                propertyChanged: OnRenderPropertyChanged);

        public static readonly BindableProperty IsVirtualProperty =
            BindableProperty.Create(nameof(IsVirtual), typeof(bool), typeof(BezierCurveView), false,
                propertyChanged: OnRenderPropertyChanged);

        public static readonly BindableProperty LineColorProperty =
            BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(BezierCurveView), Colors.Black,
                propertyChanged: OnRenderPropertyChanged);

        public static readonly BindableProperty LineWidthProperty =
            BindableProperty.Create(nameof(LineWidth), typeof(float), typeof(BezierCurveView), 2f,
                propertyChanged: OnRenderPropertyChanged);

        public double StartLeft
        {
            get => (double)GetValue(StartLeftProperty);
            set => SetValue(StartLeftProperty, value);
        }

        public double StartTop
        {
            get => (double)GetValue(StartTopProperty);
            set => SetValue(StartTopProperty, value);
        }

        public double EndLeft
        {
            get => (double)GetValue(EndLeftProperty);
            set => SetValue(EndLeftProperty, value);
        }

        public double EndTop
        {
            get => (double)GetValue(EndTopProperty);
            set => SetValue(EndTopProperty, value);
        }

        public bool CanRender
        {
            get => (bool)GetValue(CanRenderProperty);
            set => SetValue(CanRenderProperty, value);
        }

        public bool IsVirtual
        {
            get => (bool)GetValue(IsVirtualProperty);
            set => SetValue(IsVirtualProperty, value);
        }

        public Color LineColor
        {
            get => (Color)GetValue(LineColorProperty);
            set => SetValue(LineColorProperty, value);
        }

        public float LineWidth
        {
            get => (float)GetValue(LineWidthProperty);
            set => SetValue(LineWidthProperty, value);
        }

        public BezierCurveView()
        {
            // 设置绘制委托
            Drawable = new BezierCurveDrawable(this);

            // MAUI中不需要ZIndex和HitTest的等效设置
            BackgroundColor = Colors.Transparent;
        }

        private static void OnRenderPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var control = (BezierCurveView)bindable;
            control.Invalidate();
        }

        // 内部绘制类
        private class BezierCurveDrawable(BezierCurveView parent) : IDrawable
        {
            private readonly BezierCurveView _parent = parent;

            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                if (!_parent.CanRender) return;

                var path = CreateBezierPath();
                if (path == null) return;

                canvas.StrokeColor = _parent.LineColor;
                canvas.StrokeSize = _parent.LineWidth;

                if (_parent.IsVirtual)
                {
                    // 虚线效果
                    canvas.StrokeDashPattern = [8, 4];
                }
                else
                {
                    canvas.StrokeDashPattern = null;
                }

                canvas.DrawPath(path);
                DrawArrowhead(canvas, path);
            }

            private PathF CreateBezierPath()
            {
                var diffx = _parent.EndLeft - _parent.StartLeft;
                var diffy = _parent.EndTop - _parent.StartTop;

                var cp1 = new PointF((float)(_parent.StartLeft + diffx * 0.618), (float)(_parent.StartTop + diffy * 0.1));
                var cp2 = new PointF((float)(_parent.EndLeft - diffx * 0.618), (float)(_parent.EndTop - diffy * 0.1));

                var path = new PathF();
                path.MoveTo((float)_parent.StartLeft, (float)_parent.StartTop);
                path.CurveTo(cp1, cp2, new PointF((float)_parent.EndLeft, (float)_parent.EndTop));

                return path;
            }

            private void DrawArrowhead(ICanvas canvas, PathF path)
            {
                if (path == null || path.Count == 0) return;

                // 简化箭头绘制：在终点绘制三角形
                var arrowTip = new PointF((float)_parent.EndLeft, (float)_parent.EndTop);

                // 计算方向向量（简化版本）
                var directionX = (float)(_parent.EndLeft - _parent.StartLeft);
                var directionY = (float)(_parent.EndTop - _parent.StartTop);

                // 归一化
                var length = (float)Math.Sqrt(directionX * directionX + directionY * directionY);
                if (length > 0)
                {
                    directionX /= length;
                    directionY /= length;
                }
                else
                {
                    directionX = 1;
                    directionY = 0;
                }

                var arrowLength = 12f;
                var arrowWidth = 8f;

                var perpendicularX = -directionY;
                var perpendicularY = directionX;

                var arrowBase = new PointF(
                    arrowTip.X - directionX * arrowLength,
                    arrowTip.Y - directionY * arrowLength);

                var arrowWing1 = new PointF(
                    arrowBase.X + perpendicularX * arrowWidth / 2,
                    arrowBase.Y + perpendicularY * arrowWidth / 2);

                var arrowWing2 = new PointF(
                    arrowBase.X - perpendicularX * arrowWidth / 2,
                    arrowBase.Y - perpendicularY * arrowWidth / 2);

                var arrowPath = new PathF();
                arrowPath.MoveTo(arrowTip);
                arrowPath.LineTo(arrowWing1);
                arrowPath.LineTo(arrowWing2);
                arrowPath.Close();

                canvas.FillColor = _parent.LineColor;
                canvas.FillPath(arrowPath);
            }
        }
    }
}