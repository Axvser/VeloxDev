using System;
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.TransitionSystem;

namespace Demo
{
    /// <summary>
    /// 过冲演示区：四个并排目标，各自一条 Transition，互不干扰、可同屏对比。
    /// 界面在运行时构建，不改设计器生成的文件（设计器文件是生成的，手改易错）。
    /// </summary>
    partial class Form1
    {
        // -----------------------------------------------------------------------------------------------
        // 目标值与起点：动画与读数共用同一份常量，避免两处字面量漂移（之前 demo 的教训）。
        // Location / Color 不能是 const，只能用 static readonly。
        // -----------------------------------------------------------------------------------------------

        // 场景①②：水平位移。PointSampler 两个分量共用同一进度，纵向 delta 恒为 0，所以只横向移动。
        private static readonly Point MoveStart = new Point(38, 640);
        private const int MoveDistance = 150;

        // 场景④：正方形（宽高同起点同目标，取整结果一致，肉眼即可验证宽高比不歪）
        private const int SizeStart = 60;
        private const int SizeTarget = 110;

        // 场景③：中段色，每通道都留有余量（R 58→128、G 110→128、B 165→208），过冲不会撞上限。
        // 场景⑤：把 R 的目标顶到 255（上限），R 先到界，整组进度就停在端点，不再越界。
        private static readonly Color ColorStart = Color.FromArgb(0x3A, 0x6E, 0xA5);
        private static readonly Color ColorMidTarget = Color.FromArgb(0x80, 0x80, 0xD0);
        private static readonly Color ColorCeilTarget = Color.FromArgb(0xFF, 0x80, 0xD0);

        private Panel over0 = null!;
        private Panel over1 = null!;
        private Panel over2 = null!;
        private Panel over3 = null!;
        private Label readout = null!;

        // -----------------------------------------------------------------------------------------------
        // 场景定义。全部走 WinForms 适配器的 NonPriority 路径；
        // 可用属性受 Interpolator 注册表约束：Core 注册了 Point/Size/Color（+ float/double/int…），
        // 适配器只额外注册了 Padding。所以位移用 Location、尺寸用 Size、颜色用 BackColor。
        // -----------------------------------------------------------------------------------------------

        private static readonly Transition<Control> MoveBackAnimation =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(MoveStart.X + MoveDistance, MoveStart.Y))
                .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

        private static readonly Transition<Control> MoveElasticAnimation =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(MoveStart.X + MoveDistance, MoveStart.Y))
                .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Elastic.Out });

        private static readonly Transition<Control> ColorOvershootAnimation =
            Transition<Control>.Create()
                .Property(c => c.BackColor, ColorMidTarget)
                .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

        private static readonly Transition<Control> SizeOvershootAnimation =
            Transition<Control>.Create()
                .Property(c => c.Size, new Size(SizeTarget, SizeTarget))
                .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Elastic.Out });

        // 场景⑤的替代实现，见 InitializeOvershootStrip 里对该场景的说明。
        private static readonly Transition<Control> ColorCeilingAnimation =
            Transition<Control>.Create()
                .Property(c => c.BackColor, ColorCeilTarget)
                .Effect(new TransitionEffect { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

        private void InitializeOvershootStrip()
        {
            const int cellWidth = 235;
            const int cellLeft = 30;
            const int captionTop = 580;
            const int stripTop = 640;
            const int buttonTop = 800;

            // 既有动画最远到 y≈520（panel3 会移到 (400,400)），过冲条另占 y 560 以下一条带，
            // 因此把窗体加高：WinForms 的子控件会被窗体边界裁掉，必须留出真实空间。
            ClientSize = new Size(ClientSize.Width, 900);

            var caption = new Label
            {
                Text = "过冲演示区 — 四个目标互不干扰、可同屏对比；下方读数每 40ms 采样一次目标的真实属性值",
                Location = new Point(cellLeft, 552),
                Size = new Size(940, 22),
                Font = new Font("微软雅黑", 10, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray
            };

            // 每列一份说明，落在各自 235px 的格子内
            var captions = new string[]
            {
                "① 位移 Back / Elastic（目标 +150）",
                "③ 颜色（中段色，可过冲）",
                "④ 尺寸 Elastic（正方形，验宽高比）",
                "⑤ 色彩上限饱和（替代渐变）"
            };

            // ①② 共用一个目标（同一时刻只有一条在跑，后者会取消前者）；③④⑤ 各一个。
            over0 = MakeStripTarget("over0", new Point(MoveStart.X, MoveStart.Y), new Size(50, 50));
            over1 = MakeStripTarget("over1", new Point(cellLeft + cellWidth + 80, stripTop), new Size(90, 90));
            over2 = MakeStripTarget("over2", new Point(cellLeft + 2 * cellWidth + 60, stripTop), new Size(SizeStart, SizeStart));
            over3 = MakeStripTarget("over3", new Point(cellLeft + 3 * cellWidth + 65, stripTop), new Size(90, 90));

            var btnMoveBack = MakeStripButton("位移 Back.Out", new Point(cellLeft, buttonTop), 130, OvershootMoveBack);
            var btnMoveElastic = MakeStripButton("位移 Elastic.Out", new Point(cellLeft + 136, buttonTop), 146, OvershootMoveElastic);
            var btnColor = MakeStripButton("颜色过冲(③)", new Point(cellLeft + 288, buttonTop), 120, OvershootColor);
            var btnSize = MakeStripButton("尺寸过冲(④)", new Point(cellLeft + 414, buttonTop), 120, OvershootSize);
            // 场景⑤：WinForms 的 BackColor 只有纯色，没有刷子/渐变插值，跨框架的「非纯色刷」在这里无法表达。
            // 用能表达的最接近场景替代：同一条 BoundedProgress 饱和路径 —— 目标色的 R 顶到 255，
            // 整组进度被第一到界的通道钉在端点（既不越界也不回绕），这正是渐变交叉淡出系数饱和的那条语义。
            var btnCeiling = MakeStripButton("端点饱和(⑤)=渐变替代", new Point(cellLeft + 540, buttonTop), 170, OvershootCeiling);
            var btnResetOver = MakeStripButton("重置过冲", new Point(cellLeft + 716, buttonTop), 110, OvershootReset);

            // 读数的字号/等宽与 WPF 版一致；数字用对齐占位保证同屏可比
            readout = new Label
            {
                Location = new Point(cellLeft, 842),
                Size = new Size(940, 52),
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(0x33, 0x33, 0x33),
                Text = "按上方任一按钮；读数显示真实属性值"
            };

            Controls.AddRange(new Control[]
            {
                caption,
                MakeColumnCaption(captions[0], cellLeft, captionTop),
                MakeColumnCaption(captions[1], cellLeft + cellWidth, captionTop),
                MakeColumnCaption(captions[2], cellLeft + 2 * cellWidth, captionTop),
                MakeColumnCaption(captions[3], cellLeft + 3 * cellWidth, captionTop),
                over0, over1, over2, over3,
                btnMoveBack, btnMoveElastic, btnColor, btnSize, btnCeiling, btnResetOver,
                readout
            });

            // 接进既有的「重置」按钮：只追加一个处理器，既有的 ResetAnimations 一行不动
            btnReset.Click += (s, e) => ResetOvershoot();

            // 读数必须走定时器而不是 effect 的事件：流水线每段都会 Clone() effect，
            // 订阅在原始 effect 实例上的处理函数根本不会触发。
            var timer = new System.Windows.Forms.Timer { Interval = 40 };
            timer.Tick += (s, e) => UpdateReadout();
            timer.Start();

            // 挂到设计器已有的 components 上，随窗体一起释放
            components ??= new System.ComponentModel.Container();
            components.Add(timer);
        }

        private static Panel MakeStripTarget(string name, Point location, Size size) => new Panel
        {
            Name = name,
            Location = location,
            Size = size,
            BackColor = ColorStart,
            BorderStyle = BorderStyle.FixedSingle
        };

        private static Label MakeColumnCaption(string text, int left, int top) => new Label
        {
            Text = text,
            Location = new Point(left + 8, top),
            Size = new Size(228, 20),
            Font = new Font("微软雅黑", 9),
            ForeColor = Color.DimGray
        };

        private static Button MakeStripButton(string text, Point location, int width, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(width, 34),
                BackColor = Color.Beige,
                Font = new Font("微软雅黑", 9)
            };
            button.Click += onClick;
            return button;
        }

        // 每次运行前先把该元素同步写回起点，而不是靠动画回去：Prepare 以目标的当前值当动画起点，
        // 否则再点一次同一条、或点共用该元素的兄弟按钮，就会从目标动画到目标，看上去毫无反应。
        // 只重置本元素 —— 重置整条会掐掉别的元素上正在跑的动画，同屏对比就不成立了。
        private void OvershootMoveBack(object? sender, EventArgs e) => RunMove(MoveBackAnimation);

        private void OvershootMoveElastic(object? sender, EventArgs e) => RunMove(MoveElasticAnimation);

        // ①② 共用 over0，所以这一对走同一个入口
        private void RunMove(Transition<Control> animation)
        {
            Transition.Exit(over0, IncludeMutual: true, IncludeNoMutual: true);
            over0.Location = MoveStart;
            animation.Execute(over0);
        }

        private void OvershootColor(object? sender, EventArgs e)
        {
            Transition.Exit(over1, IncludeMutual: true, IncludeNoMutual: true);
            over1.BackColor = ColorStart;
            ColorOvershootAnimation.Execute(over1);
        }

        private void OvershootSize(object? sender, EventArgs e)
        {
            Transition.Exit(over2, IncludeMutual: true, IncludeNoMutual: true);
            over2.Size = new Size(SizeStart, SizeStart);
            SizeOvershootAnimation.Execute(over2);
        }

        private void OvershootCeiling(object? sender, EventArgs e)
        {
            Transition.Exit(over3, IncludeMutual: true, IncludeNoMutual: true);
            over3.BackColor = ColorStart;
            ColorCeilingAnimation.Execute(over3);
        }

        private void OvershootReset(object? sender, EventArgs e) => ResetOvershoot();

        private void ResetOvershoot()
        {
            foreach (var target in new Control[] { over0, over1, over2, over3 })
                Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

            // 直接写回起点（与设计值同一份常量），不走动画：重置是同步的，不受调度门影响
            over0.Location = MoveStart;
            over1.BackColor = ColorStart;
            over2.Size = new Size(SizeStart, SizeStart);
            over3.BackColor = ColorStart;
        }

        private void UpdateReadout()
        {
            var mid = over1.BackColor;
            var ceiling = over3.BackColor;

            // 三个可读点：位移越过目标、尺寸峰值（宽高应始终相等）、颜色通道是否越界或回绕
            readout.Text =
                $"位移X  目标 {MoveStart.X + MoveDistance,4}   当前 {over0.Left,4}"
                + $"        尺寸  目标 {SizeTarget,3}x{SizeTarget,3}   当前 {over2.Width,3}x{over2.Height,3}\r\n"
                + $"中段色③ 目标 {ColorMidTarget.R,3},{ColorMidTarget.G,3},{ColorMidTarget.B,3}   当前 {mid.R,3},{mid.G,3},{mid.B,3}"
                + $"        上限色⑤ 目标 {ColorCeilTarget.R,3},{ColorCeilTarget.G,3},{ColorCeilTarget.B,3}   当前 {ceiling.R,3},{ceiling.G,3},{ceiling.B,3}";
        }
    }
}
