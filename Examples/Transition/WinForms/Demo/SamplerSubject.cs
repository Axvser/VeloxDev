using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Demo
{
    // 采样器演示台上的被写对象：一个真正在窗体里的控件，每条采样器一条属性，类型与产物完全一致。
    // 换成控件而不是一个私有的 scratch 类，是为了让"采样器把值写到哪"这件事可被验证：采样器写它时走的是
    // 而不是一个屏幕外的对象。控件自己按属性重绘，所以"画出来"这件事不必再有另一套映射代码。
    // WinForms 没有属性系统可以挂钩子：WPF 那边一条 AffectsRender 就够，这里每条 setter 得自己叫
    // Invalidate() —— 这是两边唯一不同的地方。
    // 所以被写对象里必须有东西。
    // 绘制是有标尺的。端点的最大分量是 100，格子只有 96 宽，按原值画，子块一步就跨出格子被裁掉 ——
    internal sealed class SamplerSubject : Panel
    {
        // 边距的像素缩放。这一组端点是 (20,20,60,60) → (100,5,0,0)，最大分量 100，格子宽 96，
        // 所以取 96 / (100 + 富余) ≈ 0.6。
        internal const double DrawScale = 0.6d;

        // 舞台底色 —— 边距带里看得见的就是它。案例列表里那一格的底色也用它，两者才连成一片。
        internal static readonly Color StageColor = Color.FromArgb(0x1E, 0x1E, 0x1E);

        // 子块的颜色。它整块由边距决定摆在哪儿、有多大。
        private static readonly Color BlockColor = Color.FromArgb(0xC0, 0xC0, 0xC0);

        // 填满这一格的色块；边距改变的就是它的摆放。
        private readonly Panel _block;



        private Padding _inset;

        public SamplerSubject()
        {
            // 这一格的每个像素都由 OnPaint 画，所以不要系统再擦一遍背景；双缓冲是为了 16ms 一拍的重排不闪。
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer,
                true);

            _block = new Panel { Dock = DockStyle.Fill, BackColor = BlockColor };
            Controls.Add(_block);

        }

        // 索引器那两条写的那个集合：一段两停的渐变，按 this[i] 取放。
        // 自有索引器而不是去索引 Controls：子块是 Dock = Fill 的，布局会把写进去的 Size
        private readonly Color[] _ramp = [Color.OrangeRed, Color.SteelBlue];

        // 索引器被写过没有 —— 也就是"这一格是不是索引器那两行之一"。
        private bool _rampWritten;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color this[int index]
        {
            get => _ramp[index];
            set
            {
                _ramp[index] = value;
                _rampWritten = true;

                // WinForms 没有 AffectsRender 那样的属性钩子：重绘得自己叫。
                Invalidate();
            }
        }

        // 采样器写进来的值 —— 持有的是采样器写下的**原值**，缩放只在绘制反应里发生。
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Padding Inset
        {
            get => _inset;
            set
            {
                if (_inset.Equals(value)) return;

                _inset = value;
                Reposition();

                // WinForms 没有 AffectsRender 那样的属性钩子：重绘得自己叫。
                Invalidate();
            }
        }

        // 把采样器写下的原值**换算成格子里画得下的样子**。
        // 这是"反应"，不是"值"：属性持有的是采样器写下的原值（载荷读的就是它），这里只把它落到像素上。
        // 负的、或大过格子的边距都不必挡：WinForms 的布局会把子块收到 0，与 WPF 那边 Math.Max(1, …)
        private void Reposition()
            => Padding = new Padding(
                Scaled(_inset.Left),
                Scaled(_inset.Top),
                Scaled(_inset.Right),
                Scaled(_inset.Bottom));

        private static int Scaled(int component) => (int)Math.Round(component * DrawScale);

        // 这一格的画法，与 WPF 那边 OnRender 对应：底色只有这一处。
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(StageColor);

            // 索引器那两行画它们真正在写的那段渐变：停靠点的颜色一动，条带就跟着动 —— 写在集合元素上的
            // 值照样是看得见的。子块是不透明的、又铺满整格，所以那两行要把它让开；其余各行照旧交给它画。
            //
            // 判据是"索引器被写过"而不是采样器种类：这一格是一行一个对象，它身上没有 Kind 那种标记，
            // 而只有索引器那两行会写 this[i] —— 这个信号足够精确，也省得为它去动 bench。
            if (_block.Visible == _rampWritten) _block.Visible = !_rampWritten;

            if (_rampWritten)
            {
                var area = new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));
                using var ramp = new System.Drawing.Drawing2D.LinearGradientBrush(
                    area, _ramp[0], _ramp[1], System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
                e.Graphics.FillRectangle(ramp, area);
                return;
            }

            base.OnPaint(e);
        }
    }
}
