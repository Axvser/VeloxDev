using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.TransitionSystem;

namespace Demo
{
    /// <summary>
    /// 演示台：顶栏（全局三件 + 加载模式五件）、读数载荷，以及一张"一条案例一行"的列表。
    /// </summary>
    /// <remarks>
    /// 界面在运行时构建，设计器文件只当零件库用：那三块加载目标被搬进各自那一行，七个按钮被挪到顶栏。
    /// 搬而不是删 —— 按钮的 <c>Control.Name</c> 就是套件认的 AutomationId，删了令牌就没了。
    /// <para>
    /// 三种行长得一样（元素 / 一句"在验什么" / 启动·关闭·重置），区别只在各自的令牌、描述与动作里，与 WPF 版
    /// 同一张表。行高不统一：WinForms 的子控件被父级裁掉，而加载那三条动画写的是目标的 <c>Location</c>（绝对行程），
    /// 所以每一行的台子按这一条案例真正走多远来给，跑出格子的元素在这里不是"溢出"，是"没了"。
    /// </para>
    /// </remarks>
    partial class Form1
    {
        // -----------------------------------------------------------------------------------------------
        // 目标值与起点：动画与读数共用同一份常量，避免两处字面量漂移（之前 demo 的教训）。
        // 加载那三块目标的起点在 Form1.Designer.cs 里声明（(100,100)/(230,100)/(360,100)），
        // 载荷与被测的目标是同一份值，所以那三处不动。
        // -----------------------------------------------------------------------------------------------

        // 所有的目标都在自己台子内的局部坐标上，起点统一留在台子左上角内缩一点：台子要装得下元素本身，
        // 否则静止态就在边界外 —— WinForms 把它裁掉，那一行看上去就是空的。
        private const int TargetInset = 6;

        // 位移从起点横向走 150（Elastic 的峰值 1.37 × 150 也要落在台子里），其余原地改属性。
        private static readonly Point MoveStart = new(TargetInset, TargetInset);
        private const int MoveDistance = 150;

        // 场景④：尺寸（宽高同起点同目标，取整结果一致，肉眼即可验证宽高比不歪）。
        // 目标 100 而不是更大：它得装进统一行高的台子里，静止态不许被裁。
        private const int SizeStart = 60;
        private const int SizeTarget = 100;

        // 时长同时喂给 effect 与载荷：载荷靠它推出 done，两处若各写一份就会漂移。
        private const int BackDurationMs = 900;
        private const int ElasticDurationMs = 1100;

        // 场景③：中段色，每通道都留有余量（R 58→128、G 110→128、B 165→208），过冲不会撞上限。
        // 场景⑤：把 R 的目标顶到 255（上限），R 先到界，整组进度就停在端点，不再越界。
        private static readonly Color ColorStart = Color.FromArgb(0x3A, 0x6E, 0xA5);
        private static readonly Color ColorMidTarget = Color.FromArgb(0x80, 0x80, 0xD0);
        private static readonly Color ColorCeilTarget = Color.FromArgb(0xFF, 0x80, 0xD0);

        // ---- 台子：元素走多远，台子就给多大 ----
        //
        // 行高是统一的（见 SamplerBench.RowHeight），所以每一条案例的行程都得落在同一块台子里：
        // 台子这一层就是"场地"，装不下就等于元素跑没了。

        private static readonly Color StageBack = Color.White;

        /// <summary>
        /// 加载那三条与位移那一对的台子：一条 200 的横向行程加方块自己，Elastic 的峰值（1.37 × 行程）也落在里头。
        /// </summary>
        private static readonly Size PlayStage = new(300, SamplerBench.PlayHeight);

        /// <summary>只改颜色的那两条：方块不挪窝，台子只要装得下它。</summary>
        private static readonly Size BodyStage = new(96, SamplerBench.PlayHeight);

        /// <summary>尺寸那一条：60 长到 100，Elastic 的峰值（约 115）在最后一段会顶到边。</summary>
        private static readonly Size SizeStage = new(112, SamplerBench.PlayHeight);

        // ---- 目标 ----

        private readonly Panel over0 = MakeTarget(0);
        private readonly Panel over1 = MakeTarget(1);
        private readonly Panel over2 = MakeTarget(2);
        private readonly Panel over3 = MakeTarget(3);
        private readonly Panel over4 = MakeTarget(4);

        private Label readout = null!;
        private Label overState = null!;
        private Label overConf = null!;
        private Label overLive = null!;
        private Label overBatch = null!;

        private static Panel MakeTarget(int index) => new()
        {
            Name = $"over{index}",
            Location = new Point(TargetInset, TargetInset),
            Size = index == 2 ? new Size(SizeStart, SizeStart) : new Size(80, 60),
            BackColor = ColorStart,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // -----------------------------------------------------------------------------------------------
        // 场景定义。全部走 WinForms 适配器的 NonPriority 路径；
        // 可用属性受 Interpolator 注册表约束：Core 注册了 Point/Size/Color（+ float/double/int…），
        // 适配器只额外注册了 Padding。所以位移用 Location、尺寸用 Size、颜色用 BackColor。
        // -----------------------------------------------------------------------------------------------

        private static readonly Transition<Control> MoveBackAnimation =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(MoveStart.X + MoveDistance, MoveStart.Y))
                .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

        private static readonly Transition<Control> MoveElasticAnimation =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(MoveStart.X + MoveDistance, MoveStart.Y))
                .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

        private static readonly Transition<Control> ColorOvershootAnimation =
            Transition<Control>.Create()
                .Property(c => c.BackColor, ColorMidTarget)
                .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

        private static readonly Transition<Control> SizeOvershootAnimation =
            Transition<Control>.Create()
                .Property(c => c.Size, new Size(SizeTarget, SizeTarget))
                .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(ElasticDurationMs), Ease = Eases.Elastic.Out });

        // 场景⑤的替代实现，见列表里对该场景的说明。
        private static readonly Transition<Control> ColorCeilingAnimation =
            Transition<Control>.Create()
                .Property(c => c.BackColor, ColorCeilTarget)
                .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(BackDurationMs), Ease = Eases.Back.Out });

        // -----------------------------------------------------------------------------------------------
        // 顶栏与列表
        // -----------------------------------------------------------------------------------------------

        /// <summary>时间轴控制那一排的顶边：紧跟在加载模式那排（下沿 86）之后。</summary>
        private const int TimelineRowTop = 92;

        /// <summary>顶栏（全局三件 + 加载模式五件 + 时间轴六件 + 读数）的总高。列表从它下面开始。</summary>
        private const int TopAreaHeight = 348;

        /// <summary>案例列表内容区的宽度。</summary>
        private const int RowWidth = SamplerBench.RowWidth;

        private Panel _top = null!;
        private Panel _list = null!;
        private Button _btnStartAll = null!;

        /// <summary>列表里的每一行，按顺序 —— "全部启动"走的就是这里的动作。</summary>
        private readonly List<CaseRow> _caseRows = [];

        private readonly SamplerBench _bench = new();

        private void BuildBench()
        {
            // 窗体加高到装得下顶栏与一屏列表：WinForms 的子控件被窗体边界裁掉，必须留出真实空间。
            ClientSize = new Size(1220, 840);

            _top = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, TopAreaHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White,
            };

            _list = new Panel
            {
                Location = new Point(0, TopAreaHeight),
                Size = new Size(ClientSize.Width, ClientSize.Height - TopAreaHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White,
                // 行多了就滚。套件把某一行弄进视野走的也是它：AutoScroll 的容器会跟着拿到焦点的子控件滚。
                AutoScroll = true,
            };

            Controls.Add(_top);
            Controls.Add(_list);

            BuildToolbar();
            BuildCaseList();

            // 接上设计器那两件"全局"按钮的案例列表这一半：只追加一个处理器，设计器里既有的那两个一行不动。
            btnReset.Click += (sender, args) => ResetAllRows();
            btnExit.Click += (sender, args) => StopAllRows();

            // 读数必须走定时器而不是 effect 的事件：流水线每段都会 Clone() effect，
            // 订阅在原始 effect 实例上的处理函数根本不会触发。
            var timer = new System.Windows.Forms.Timer { Interval = 40 };
            timer.Tick += (sender, args) =>
            {
                UpdateReadout();
                overState.Text = BuildState();
            };
            timer.Start();

            // 挂到设计器已有的 components 上，随窗体一起释放
            components ??= new System.ComponentModel.Container();
            components.Add(timer);

            // 每一条采样器先写成它自己声明的起点。不这么做的话，静息时每行持有的是控件的默认值（零边距），
            // 于是"偏离起点"的行数在什么都没跑的时候就是满的 —— 顶栏那个数会变成一句假命题。
            Load += (sender, args) => ResetSamplerRows();
        }

        private void BuildToolbar()
        {
            // 全局面板：一次动到下面每一行。列表里那十几条各自是一条真 Transition，这里是同时发起它们，
            // 顺带就是一个并发压力场景。
            _btnStartAll = MakeToolbarButton("over.btn.start.all", "全部启动", 8, Color.LightBlue);
            _btnStartAll.Click += StartAllCases;

            // 设计器造的那两件搬进来：搬而不是删，处理器与令牌都还挂在原处。
            PlaceToolbar(btnExit, 122, Color.LightCoral);
            PlaceToolbar(btnReset, 236, Color.LightGreen);

            lblStatus.Location = new Point(360, 14);
            lblStatus.Size = new Size(560, 22);
            lblStatus.Font = new Font("微软雅黑", 9);
            _top.Controls.Add(lblStatus);

            // 加载模式那五件。它们是"怎么加载"，不是"加载什么"：它们驱动的三个形状在下面各占一行，
            // 各自有自己的启动。把它们留在顶栏，列表才可能只有一种行 —— 每行恰好 启动 / 关闭 / 重置。
            var loadModes = new (Button Button, string Text, int Left, Color Back)[]
            {
                (btnStartMainThread, "主线程互斥", 8, Color.LightGoldenrodYellow),
                (btnStart, "后台线程互斥", 130, Color.LightBlue),
                (btnStartMainThreadNonMutual, "主线程并发", 252, Color.PaleGreen),
                (btnStartNonMutual, "后台线程并发", 374, Color.LightCyan),
                (btnStartRepeatedMutual, "连续互斥", 496, Color.MistyRose),
            };

            foreach (var (button, text, left, back) in loadModes)
            {
                button.Text = text;
                PlaceToolbar(button, left, back, top: 48, width: 118, height: 38);
            }

            // 时间轴控制。作用对象是上面那排加载模式驱动的三块长动画（十来秒的循环），不是过冲那五行 ——
            // 后者是 900ms 的一次性过冲，暂停与不暂停在屏幕上分不出来，而这一排既要给验收套件当把手，也要给人看。
            // 三块各是一条真 Transition，暂停/变速/定位都按 target 寻址，所以逐块调用 —— 这本身就是
            // "控制面挂在 target 上、不挂在快照上"的一次演示。整排同一个底色：它是一个控制面，不是六条案例。
            var timelineBack = Color.LightSteelBlue;
            MakeToolbarButton("over.btn.pause", "暂停", 8, timelineBack, top: TimelineRowTop, width: 86).Click += PauseAll;
            MakeToolbarButton("over.btn.resume", "恢复", 100, timelineBack, top: TimelineRowTop, width: 86).Click += ResumeAll;
            MakeToolbarButton("over.btn.rate.slow", "慢速 ×0.25", 192, timelineBack, top: TimelineRowTop, width: 96).Click += RateSlow;
            MakeToolbarButton("over.btn.rate.fast", "快速 ×4", 294, timelineBack, top: TimelineRowTop, width: 96).Click += RateFast;
            MakeToolbarButton("over.btn.rate.normal", "正常 ×1", 396, timelineBack, top: TimelineRowTop, width: 96).Click += RateNormal;
            MakeToolbarButton("over.btn.seek.next", "下一程", 498, timelineBack, top: TimelineRowTop, width: 86).Click += SeekNextPass;

            // 读数：人看的那一行，与四份机器可读的载荷。载荷只报告"每个目标当前/峰值是多少"、
            // "这一行跑了什么"，至于哪个场景动哪个目标、起止与时长，由测试侧的 manifest 声明 ——
            // 观测与语义各自只有一处来源。
            readout = MakeLabel("over.readout", 132, 44, 9, Color.FromArgb(0x33, 0x33, 0x33),
                "按下面任一行；读数显示当前值与目标值");

            // 载荷比原先又多一截：时间轴那四个字段（paused/rate/pos/cycle）也进场了，而 nomutual 仍然排在最后，
            // 所以标签再高一行，留出折行的位置 —— 否则那个最要紧的字段会被裁掉。
            overState = MakeLabel("over.state", 178, 80, 8, Color.FromArgb(0x80, 0x80, 0x80), "v=1;seq=0");
            overConf = MakeLabel("over.conf", 260, 16, 8, Color.FromArgb(0x80, 0x80, 0x80), "v=1;seq=0;n=0;");
            overLive = MakeLabel("over.live", 278, 16, 8, Color.FromArgb(0x80, 0x80, 0x80), "v=1;seq=0;done=1;");

            // 批量载荷：点一次"全部启动"，每一行的五帧闭式解与这一行的观察摘要都在这一份里。
            // 逐行载荷是"点哪一行写哪一行"的形状，十几行一起跑只会互相覆盖，所以并发这一路另开一份。
            overBatch = MakeLabel("over.batch", 296, 44, 8, Color.FromArgb(0x80, 0x80, 0x80), "v=1;seq=0;done=1;rows=0;");
        }

        private Button MakeToolbarButton(string name, string text, int left, Color back,
            int top = 8, int width = 110, int height = 34)
        {
            var button = new Button
            {
                Name = name,
                Text = text,
                Location = new Point(left, top),
                Size = new Size(width, height),
                BackColor = back,
                Font = new Font("微软雅黑", 9),
            };

            _top.Controls.Add(button);
            return button;
        }

        /// <summary>把设计器造的按钮挪到顶栏：换父级这一步就把它从窗体上摘下来了。</summary>
        private void PlaceToolbar(Button button, int left, Color back, int top = 8, int width = 110, int height = 34)
        {
            button.Location = new Point(left, top);
            button.Size = new Size(width, height);
            button.BackColor = back;
            button.Font = new Font("微软雅黑", 9);
            _top.Controls.Add(button);
        }

        private Label MakeLabel(string name, int top, int height, float size, Color fore, string text)
        {
            var label = new Label
            {
                Name = name,
                Location = new Point(8, top),
                Size = new Size(RowWidth, height),
                Font = new Font("Consolas", size),
                ForeColor = fore,
                AutoSize = false,
                Text = text,
            };

            _top.Controls.Add(label);
            return label;
        }

        // -----------------------------------------------------------------------------------------------
        // 案例列表
        //
        // 一张统一的表：一条案例一行，行里有元素、有一句"这条在验什么"、有这一行自己的三个动作。行的种类只有
        // 三种 —— 加载、过冲、采样器 —— 但对读表的人来说它们长得一样，区别只在各自的令牌、描述与动作里。
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// 把三种案例行拼成一张表：先加载那三行，再过冲那五行，最后每个采样器一行。
        /// </summary>
        /// <remarks>
        /// 采样器排在最后是有意的：它们是套件逐条驱动的对象，也是最需要滚动的部分，而滚动这一步本身要被真的走到。
        /// </remarks>
        private void BuildCaseList()
        {
            _caseRows.Clear();

            _caseRows.Add(LoadRow("translate", "平移",
                "嵌套属性路径：动画写的是目标的 Location，外加父容器（台子）的 BackColor —— 子对象上的成员，两处都得回写。",
                panel1, () => Animation0.Execute(panel1), RestoreTranslateRow));

            _caseRows.Add(LoadRow("rotate", "旋转/放大",
                "尺寸与颜色一起换：端点是一组 Size/Color，自反向加两次循环，测的是互斥调度与取消。",
                panel2, () => Animation1.Execute(panel2), RestoreRotateRow));

            _caseRows.Add(LoadRow("combine", "三段串起来",
                "一条 Transition 里串三段：位移、尺寸、颜色依次上，中间夹两次等待 —— 测的是 AwaitThen 的分段与末段落点。",
                panel3, () => Animation2.Execute(panel3), RestoreCombineRow));

            _caseRows.Add(OvershootRow("shift-back", "位移 Back.Out",
                "共享进度的标量过冲：Back 越过目标 10% 再落回来，数字看得出来、眼睛看不出来。",
                over0, PlayStage, "back", MoveBackAnimation, BackDurationMs, 0, RestoreShiftStart));

            _caseRows.Add(OvershootRow("shift-elastic", "位移 Elastic.Out",
                "上面那一行的另一条曲线：Elastic 峰值更高、回弹次数更多，两行同时跑就是同一次对比。",
                over4, PlayStage, "elastic", MoveElasticAnimation, ElasticDurationMs, 4, RestoreElasticStart));

            _caseRows.Add(OvershootRow("color", "颜色过冲",
                "通道各自的界：中段色每通道都留有余量，过冲不会撞上限，看到的是亮度而不是色相。",
                over1, BodyStage, "color", ColorOvershootAnimation, BackDurationMs, 1, RestoreColorStart));

            _caseRows.Add(OvershootRow("size", "尺寸过冲",
                "宽高一起长，端点 60→100：Size 没有上下限，外推照走，所以峰值会略大于目标。",
                over2, SizeStage, "size", SizeOvershootAnimation, ElasticDurationMs, 2, RestoreSizeStart));

            _caseRows.Add(OvershootRow("brush", "端点饱和(⑤)",
                "WinForms 没有渐变插值，这一条是同一条饱和路径的替代：目标色的 R 顶到 255，整组进度被第一到界的通道钉在端点。",
                over3, BodyStage, "brush", ColorCeilingAnimation, BackDurationMs, 3, RestoreGradientStart));

            // 采样器行由探针表生成。行里的"启动"就是验收套件点的那个把手令牌，点它写闭式解载荷并起那条真动画 ——
            // 所以加一条采样器仍然只需要改 SamplerProbe 一处。
            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                var name = sampler;
                _watches[name] = new SamplerProbe.LiveWatch();
                _batchFrames[name] = string.Empty;

                _caseRows.Add(_bench.SamplerRow(
                    name,
                    () => RunSamplerProbe(name),
                    () => StopSamplerRows(name),
                    () => ResetSamplerRows(name),
                    // 批量路径：只起这一条自己的动画，不碰别的行，也不写逐行载荷 ——
                    // 十几条各自写 over.live 只会互相覆盖，并发那一路走的是 over.batch。
                    () =>
                    {
                        var subject = _bench.SubjectFor(name);
                        Transition.Exit(subject, IncludeMutual: true, IncludeNoMutual: true);

                        try
                        {
                            StartSamplerAnimation(name, subject);
                        }
                        catch (Exception exception)
                        {
                            // 记在**这一行自己的**观察里，所以十几条并发时认得出是谁起不来。
                            _watches[name].Fail($"{exception.GetType().Name}: {exception.Message}");
                        }
                    }));
            }

            _list.Controls.Add(SamplerBench.Build(_caseRows));
        }

        /// <summary>
        /// 一条加载案例：元素是设计器那三块目标之一，搬进这一行自己的台子里。
        /// </summary>
        /// <remarks>
        /// 台子统一是 <see cref="PlayStage"/>，而三条加载动画的行程都收在它里面（见 Form1.Designer.cs 里那三条
        /// 动画的端点）：位移横向 200、放大到 150×96、三段先到 (170,6) 再回头。给小了，WinForms 会把它裁掉 ——
        /// 元素"跑没了"与"没动"在屏幕上是同一个样子。
        /// </remarks>
        private CaseRow LoadRow(
            string id, string title, string description, Panel target, Action start, Action restore)
        {
            var frame = SamplerBench.Stage(PlayStage.Width, PlayStage.Height, StageBack);

            // 一个控件只能有一个父级：先从窗体上摘下来，再放进这一行的台子。
            // 目标的 Location 一个字节都不改 —— 载荷读的就是它，静息态与重置态都落在同一对数字上。
            Controls.Remove(target);
            frame.Controls.Add(target);

            return new CaseRow(
                title,
                description,
                frame,
                PlayStage.Width,
                $"over.row.start.{id}",
                $"over.row.stop.{id}",
                $"over.row.reset.{id}",
                start,
                () => Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true),
                () => ResetCase(target, restore));
        }

        /// <summary>一条过冲案例：元素是那块目标自己，台子只负责给它一个走得出的格子。</summary>
        private CaseRow OvershootRow(
            string id, string title, string description, Panel target, Size stage,
            string scenario, Transition<Control> animation, int durationMs, int targetIndex, Action restore)
        {
            var frame = SamplerBench.Stage(stage.Width, stage.Height, StageBack);
            frame.Controls.Add(target);

            return new CaseRow(
                title,
                description,
                frame,
                stage.Width,
                $"over.row.start.{id}",
                $"over.row.stop.{id}",
                $"over.row.reset.{id}",
                () => RunOvershoot(target, animation, scenario, durationMs, targetIndex, restore),
                () => Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true),
                () => ResetCase(target, restore));
        }

        /// <summary>停掉并把它放回声明的静止态。</summary>
        private static void ResetCase(Control element, Action restore)
        {
            Transition.Exit(element, IncludeMutual: true, IncludeNoMutual: true);
            restore();
        }

        // -----------------------------------------------------------------------------------------------
        // 过冲：把目标放回起点，再起动画
        // -----------------------------------------------------------------------------------------------

        /// <summary>
        /// 跑一条过冲案例：先把目标放回起点，再起动画。
        /// </summary>
        /// <remarks>
        /// 先放回起点是必须的：<c>Prepare</c> 读的是**目标此刻的值**当起点，不放回去就变成"从终点动到终点"，
        /// 看上去什么都没发生。同一条再点一次、或点了共用同一块目标的兄弟按钮，都撞在这上面。
        /// </remarks>
        private void RunOvershoot(
            Panel target, Transition<Control> animation, string scenario, int durationMs, int targetIndex, Action restoreStart)
        {
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
            restoreStart();
            BeginScenario(scenario, durationMs, targetIndex);
            animation.Execute(target);
        }

        // ---- 每一行"重置"与"起点"的目标状态：就是这些元素声明时的那份值 ----

        // 加载那三行走的是设计器里那条重置路径：同步套用目标值，不走动画。
        private void RestoreTranslateRow() => CreateReset1().Effect(TransitionEffects.Empty).Execute(panel1);

        private void RestoreRotateRow() => CreateReset2().Effect(TransitionEffects.Empty).Execute(panel2);

        private void RestoreCombineRow() => CreateReset3().Effect(TransitionEffects.Empty).Execute(panel3);

        private void RestoreShiftStart() => over0.Location = MoveStart;

        private void RestoreElasticStart() => over4.Location = MoveStart;

        private void RestoreColorStart() => over1.BackColor = ColorStart;

        private void RestoreSizeStart() => over2.Size = new Size(SizeStart, SizeStart);

        private void RestoreGradientStart() => over3.BackColor = ColorStart;

        // -----------------------------------------------------------------------------------------------
        // 采样器：演出与批量
        // -----------------------------------------------------------------------------------------------

        /// <summary>激活次数：载荷靠它证明这一次是新的，而不是上一次点击留下的。</summary>
        private long _probeSequence;

        // 只留一支演出用的定时器：连点两个把手时，后一次要能叫停前一次，否则两条采样器会同时往各自的格子里写。
        private System.Windows.Forms.Timer? _benchTimer;

        // 上一次演出写的那一格：换一格时要把它那条真动画停掉，不然它会在没人看的时候继续往旧格子上写。
        private SamplerSubject? _liveSubject;

        // 每一条采样器**各有一份**采样累积：逐行路径只用被点的那一份，批量路径十几份同时喂。
        // 异常也记在各自那一份里，所以十几条并发时谁的错是认得出的。
        private readonly Dictionary<string, SamplerProbe.LiveWatch> _watches = new(StringComparer.Ordinal);

        // 批量运行时每一行的闭式解帧（点那一刻算一次，不随动画走）与那支采样定时器。
        private readonly Dictionary<string, string> _batchFrames = new(StringComparer.Ordinal);
        private System.Windows.Forms.Timer? _batchTimer;

        /// <summary>
        /// 这一行的"启动"：验五个固定缓动时间，再起一条真动画把这一条跑一遍。
        /// </summary>
        /// <remarks>
        /// 采样器写在**这一格的在屏控件**上、载荷也从它读回，所以下面两件事是同一件事的两种读法。
        /// 这一条同时是套件点的那个把手令牌走的路径，也是行里"启动"按钮走的路径 —— 两者不可能各说各话。
        /// </remarks>
        private void RunSamplerProbe(string sampler)
        {
            var subject = _bench.SubjectFor(sampler);

            // 验：五个固定的缓动时间各跑一帧，写进载荷。
            overConf.Text = SamplerProbe.Run(subject, sampler, ++_probeSequence);

            // 跑：用真的 Transition 流水线把这条采样器跑一遍 —— 屏幕上看到的就是它 —— 全程从控件属性采样，
            // 结果写进 over.live。验收要看的正是这条路径：控件在一段真动画里每一刻持有的数据是否合法。
            PlaySampler(sampler, subject, _probeSequence);
        }

        /// <summary>
        /// 起一条真动画把某个案例从起点跑到终点。
        /// </summary>
        /// <remarks>
        /// 起点必须先同步写回目标：<c>Prepare</c> 读的是**目标此刻的值**当起点，不写回去就变成"从终点动到终点"，
        /// 屏幕上什么都不会发生。属性路径与采样器都取自同一张探针表。
        /// </remarks>
        private static void StartSamplerAnimation(string sampler, SamplerSubject subject)
        {
            var property = SamplerProbe.Property(sampler);
            property.SetValue(subject, SamplerProbe.Start(sampler));

            // Effect 的其余默认值正是这里要的：FPS 60、不自动反向、只跑一趟 —— 于是末帧精确落在终点。
            var animation = Transition<SamplerSubject>.Create()
                .Effect(new TransitionEffect { Duration = SamplerProbe.BenchDuration, Ease = Eases.Back.Out });

            animation.GetState().SetValue(property, SamplerProbe.End(sampler));
            animation.GetState().SetInterpolator(property, SamplerProbe.Create(sampler));

            animation.Execute(subject);
        }

        /// <summary>
        /// 顶栏的"全部启动"：每一行同时起一条真动画。
        /// </summary>
        /// <remarks>
        /// 十几条真 <c>Transition</c> 并发跑，是这个库要经得住的一种真实用法；刻意不写任何逐行载荷 ——
        /// 采样器各自写 <c>over.live</c> 只会互相覆盖，而那些载荷的归属是"点了哪一行"，由行里的"启动"负责。
        /// </remarks>
        private void StartAllCases(object? sender, EventArgs e)
        {
            // 正在被观察的那一行先停掉观察：它的载荷已经发过了，别让它在"全部启动"之后又补发一份属于别人的。
            StopBenchTimers();
            _liveSubject = null;

            var sequence = ++_probeSequence;

            // 闭式解那半是一次性算出来的，不需要动画 —— 十几行一起算完，拼进同一份载荷。
            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                _batchFrames[sampler] = SamplerProbe.RunFrames(_bench.SubjectFor(sampler), sampler);
                _watches[sampler].Reset();
            }

            WriteBatch(sequence, done: false);

            // 三种行一起发起 —— 加载三行、过冲五行、采样器若干行，十几条真 Transition 并发跑，
            // 这是这个库要经得住的一种真实用法。
            //
            // 走过行自己的动作而不是另写一套：行里那个动作就是这条案例的定义，两处各写一份必然漂移。
            // 采样器那一类走的是 BulkStart —— 行里那个"启动"会掐掉上一次被观察的行，批量这一路谁都不能动别人。
            foreach (var row in _caseRows)
            {
                (row.BulkStart ?? row.Start)();
            }

            StartBatchWatch(sequence);
        }

        /// <summary>
        /// 批量运行期间每一拍喂一次每一行，全部落定之后把这一份载荷收尾。
        /// </summary>
        /// <remarks>
        /// "跑完了"是**每一行都落定**，不是某一拍过去 —— 十几条并发，先跑完的等后跑完的。落定判据仍是那个
        /// 逐行用的稳定窗口（见 <see cref="SamplerProbe.LiveWatch"/>），兜底上限也一样。
        /// </remarks>
        private void StartBatchWatch(long sequence)
        {
            var clock = Stopwatch.StartNew();
            var timer = new System.Windows.Forms.Timer { Interval = 16 };
            _batchTimer = timer;

            timer.Tick += (sender, args) =>
            {
                var allSettled = true;

                foreach (var sampler in SamplerProbe.SamplerNames)
                {
                    var watch = _watches[sampler];
                    var measurement = SamplerProbe.Read(_bench.SubjectFor(sampler), sampler);
                    watch.Observe(measurement.TypeTag, measurement.Components);

                    if (!watch.Settled) allSettled = false;
                }

                if (clock.Elapsed < SamplerProbe.BenchDuration) return;
                if (!allSettled && clock.Elapsed < SamplerProbe.BenchDuration + SamplerProbe.BenchSettleCap) return;

                timer.Stop();
                timer.Dispose();
                WriteBatch(sequence, done: true);
            };

            timer.Start();
        }

        /// <summary>
        /// 写批量载荷：每一行的五帧闭式解，加上每一行的观察摘要。
        /// </summary>
        /// <remarks>
        /// 点那一刻先落一份 <c>done=0</c>（帧已经齐了，观察还没跑完），每一行都落定之后再落 <c>done=1</c>。
        /// 与逐行载荷同一套握手：对方靠序号越过基线、且 done 为真，才认这一份是新的、且是跑完了的。
        /// </remarks>
        private void WriteBatch(long sequence, bool done)
        {
            var payload = new System.Text.StringBuilder(
                $"v=1;seq={sequence};done={(done ? 1 : 0)};rows={SamplerProbe.SamplerNames.Count};");

            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                payload.Append(_batchFrames[sampler]);

                if (done) payload.Append(_watches[sampler].BatchFields(sampler));
            }

            overBatch.Text = payload.ToString();
        }

        /// <summary>停下每一行（或指定的一行）。原地冻结，与库的退出语义一致。</summary>
        private void StopSamplerRows(string? only = null)
        {
            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                if (only is not null && sampler != only) continue;
                Transition.Exit(_bench.SubjectFor(sampler), IncludeMutual: true, IncludeNoMutual: true);
            }
        }

        /// <summary>把每一行（或指定的一行）停回它声明的起点。</summary>
        private void ResetSamplerRows(string? only = null)
        {
            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                if (only is not null && sampler != only) continue;

                var subject = _bench.SubjectFor(sampler);
                Transition.Exit(subject, IncludeMutual: true, IncludeNoMutual: true);
                SamplerProbe.Property(sampler).SetValue(subject, SamplerProbe.Start(sampler));
            }
        }

        /// <summary>停下这一台子上的两支演出定时器，并把已经排队的收尾丢掉。</summary>
        private void StopBenchTimers()
        {
            StopBenchTimer(ref _benchTimer);
            StopBenchTimer(ref _batchTimer);
        }

        private static void StopBenchTimer(ref System.Windows.Forms.Timer? timer)
        {
            if (timer is null) return;

            timer.Stop();
            timer.Dispose();
            timer = null;
        }

        /// <summary>
        /// 演出：用真的 <c>Transition</c> 把一条采样器从起点跑到终点，全程对控件属性采样，最后写进 <c>over.live</c>。
        /// </summary>
        /// <remarks>
        /// 这不是原先那个手写循环 —— scheduler、effect、端点归一化、按属性类型解析采样器、每帧往 UI 线程投递，
        /// 走的全是库自己那条路径。
        /// <para>
        /// 起点必须先同步写回目标：<c>Prepare</c> 读的是**目标此刻的值**当起点，不写回去就变成"从终点动到终点"，
        /// 屏幕上什么都不会发生。
        /// </para>
        /// <para>
        /// 末帧是排队投递的，所以落定判据是"值连续几拍不再变"而不是一个固定的余量 —— 负载重的机器上固定余量会读早，
        /// 把一次正常的动画报成"没跑到终点"。
        /// </para>
        /// </remarks>
        private void PlaySampler(string sampler, SamplerSubject subject, long sequence)
        {
            StopBenchTimer(ref _benchTimer);
            if (_liveSubject is not null) Transition.Exit(_liveSubject, IncludeMutual: true, IncludeNoMutual: true);
            _liveSubject = subject;

            // 这一行自己的观察。共享一份认不出是谁的错，所以每行一份。
            var watch = _watches[sampler];
            watch.Reset();

            try
            {
                StartSamplerAnimation(sampler, subject);
            }
            catch (Exception exception)
            {
                // 这里同步抛出的都是"这条路径根本起不来"（例如声明的路径不可采样）。照实报给验收，
                // 而不是让一次点击把 demo 打挂。
                watch.Fail($"{exception.GetType().Name}: {exception.Message}");
            }

            // 点击那一刻先落一份 seq，验收侧靠它把"新的"与"上一次剩下的"分开。
            overLive.Text = SamplerProbe.LiveWatch.Pending(sampler, sequence);

            var clock = Stopwatch.StartNew();
            var timer = new System.Windows.Forms.Timer { Interval = 16 };
            _benchTimer = timer;

            timer.Tick += (sender, args) =>
            {
                var measurement = SamplerProbe.Read(subject, sampler);
                watch.Observe(measurement.TypeTag, measurement.Components);

                if (clock.Elapsed < SamplerProbe.BenchDuration) return;
                if (!watch.Settled && clock.Elapsed < SamplerProbe.BenchDuration + SamplerProbe.BenchSettleCap) return;

                // 落定了，或者等够了：报出去。等够还没落定本身就是一条要被看见的异常。
                timer.Stop();
                timer.Dispose();
                overLive.Text = watch.Digest(sampler, sequence);
            };

            timer.Start();
        }

        // -----------------------------------------------------------------------------------------------
        // 验收观测面
        //
        // 人类读数之外再写一份机器可读的载荷：同一支定时器、同一批值，但用固定的 key=value 而不是散文，
        // 测试就不必去解析一份随时可能被重新排版的版式。载荷只报告"每个目标当前/峰值是多少"，至于哪个场景
        // 动哪个目标、起止与时长，由测试侧的 manifest 声明 —— 观测与语义各自只有一处来源。
        // 目标编号与列表同序：t0=over0(位移 Back)、t1=over1(中段色)、t2=over2(尺寸)、t3=over3(端点饱和)、
        // t4=over4(位移 Elastic 的第二块目标，见下面 _targetPeaks 的说明)。
        // -----------------------------------------------------------------------------------------------

        private readonly Stopwatch _scenarioClock = new();
        /// <summary>每个过冲目标各自的峰值。第五条是位移那第二条曲线（Elastic）自己的目标。</summary>
        private readonly double[] _targetPeaks = new double[5];
        private string _scenario = "none";
        private int _scenarioDurationMs;
        private long _sequence;

        /// <summary>Records the scenario that is starting and clears that target's previous peak — every click has to begin from a clean observation.</summary>
        private void BeginScenario(string scenario, int durationMs, int targetIndex)
        {
            _scenario = scenario;
            _scenarioDurationMs = durationMs;
            _targetPeaks[targetIndex] = double.NegativeInfinity;
            _scenarioClock.Restart();
        }

        private string BuildState()
        {
            _sequence++;

            // 只有标量目标记录峰值。峰值存在的意义是"刀刃型峰"——采样落在尖峰两侧就会低估它；颜色的过冲是一段
            // 形状而不是一个尖峰，报当前值就够，测试轮询取最大即可。
            // 位移报的是 Location.X 本身（动画真正写的那个属性），目标即 MoveStart.X + MoveDistance。
            var shift = over0.Left;
            var elastic = over4.Left;
            var size = over2.Width;
            _targetPeaks[0] = Math.Max(_targetPeaks[0], shift);
            _targetPeaks[2] = Math.Max(_targetPeaks[2], size);
            _targetPeaks[4] = Math.Max(_targetPeaks[4], elastic);

            // done 由时长推出，而不是订阅 effect.Completed：流水线每段克隆 effect，订阅在原件上的处理函数不触发。
            // 它是必需的，不能靠"值等于目标"判断结束 —— 两条曲线都会中途再次穿过目标（Elastic 在 1.1s 内穿越七次）。
            var elapsed = _scenarioClock.ElapsedMilliseconds;
            var done = _scenario != "none" && elapsed >= _scenarioDurationMs + 50 ? 1 : 0;

            var row = RowState();

            return $"v=1;seq={_sequence};scen={_scenario};t={elapsed};done={done};"
                 + $"t0.cur={shift:F3};t0.peak={Peak(0)};"
                 + $"t1.cur={Describe(over1.BackColor)};"
                 + $"t2.cur={size:F3};t2.peak={Peak(2)};"
                 + $"t3.cur={Describe(over3.BackColor)};"
                 + $"t4.cur={elastic:F3};t4.peak={Peak(4)};"
                 + RecState("r0", panel1) + $"r0.parent={Describe(ParentBackColor(panel1))};"
                 + RecState("r1", panel2)
                 + RecState("r2", panel3)
                 // 时间轴那四个字段同样排在 nomutual 之前。pos 按毫秒取整报出：读的是当前这一程内的偏移，
                 // 而不是整条动画的位置 —— 程是独立的，跨程的位置没有意义。
                 + TimelineState(panel1)
                 // rows/away/moving 排在 nomutual **之前**：后者是加载模式那半必须读到的最后一个字段，
                 // 所以它排在最后，标签也得为这一份更长载荷留出折行的位置。
                 + $"rows={row.Rows};away={row.Away};moving={row.Moving};"
                 + $"nomutual={NoMutualCount()};";
        }

        /// <summary>
        /// 时间轴控制那排的回读：暂停与否、速率、当前程内位置、第几程。读的是 panel1 —— 它既是加载模式那排
        /// 每一条路径都动的第一块，也是"连续互斥"唯一点的那块，拿它当代表不会读到一块静息的目标。
        /// </summary>
        /// <remarks>
        /// 速率用不变文化格式化，免得小数点跟着机器区域设置变，验收侧读到 "0,25" 就解析不了。
        /// </remarks>
        private static string TimelineState(Control target)
        {
            const bool mutual = true, noMutual = true;
            return $"paused={(Transition.IsPaused(target, mutual, noMutual) ? 1 : 0)};"
                 + $"rate={Transition.Rate(target, mutual, noMutual).ToString("0.###", CultureInfo.InvariantCulture)};"
                 + $"pos={(int)Transition.Position(target, mutual, noMutual).TotalMilliseconds};"
                 + $"cycle={Transition.Cycle(target, mutual, noMutual)};";
        }

        /// <summary>
        /// 加载模式那一排三块目标的状态：动画真正写的那些属性，读出来报给测试。
        /// </summary>
        /// <remarks>
        /// 与过冲条同一支定时器、同一次采样，所以两者不可能不一致。报的是"动的是什么"而不是"应该动到哪" ——
        /// 那三条动画各自带 auto-reverse 与 loop，终点要靠复算库的语义才知道，测试不去复算它。
        /// WinForms 没有 Transform 集合：位置是 Location(Left/Top)，尺寸是 Size(Width/Height)，颜色是 BackColor。
        /// </remarks>
        private static string RecState(string prefix, Control target)
            => $"{prefix}.x={target.Left};"
             + $"{prefix}.y={target.Top};"
             + $"{prefix}.w={target.Width};"
             + $"{prefix}.h={target.Height};"
             + $"{prefix}.color={Describe(target.BackColor)};";

        /// <summary>
        /// 目标的父容器的背景色。Animation0 动的是一对颜色 —— 目标自己的 BackColor 和所在台子的，
        /// 后者是这条 demo 里唯一一条嵌套属性路径，所以两色都要报，否则那条路径是观测不到的。
        /// </summary>
        private static Color ParentBackColor(Control target)
            => target.Parent?.BackColor ?? Color.Empty;

        /// <summary>
        /// 这三块目标上还有几条**并发**（非互斥）动画在跑。
        /// </summary>
        /// <remarks>
        /// 这是唯一能把"互斥加载"和"并发加载"区分开的可观测量：互斥调度器是按目标缓存的一辈子不释放，
        /// `TryGetMutualScheduler` 返回 true 只说明"这目标跑过互斥动画"；而非互斥的那张表在每条动画结束时
        /// 真的会清空。要点是取**数组长度**而不是那个 bool —— 表项本身不随运行结束移除。
        /// 每一步都不许抛：这是在 Timer.Tick 里跑的。
        /// </remarks>
        private int NoMutualCount()
        {
            var running = 0;

            foreach (var target in new Control[] { panel1, panel2, panel3 })
            {
                if (TransitionScheduler.TryGetNoMutualScheduler(target, out var schedulers))
                {
                    running += schedulers.Length;
                }
            }

            return running;
        }

        /// <summary>上一拍每一行的值，用来判断"这一拍还在不在变"。按键是采样器名。</summary>
        private readonly Dictionary<string, double[]> _rowPrevious = new(StringComparer.Ordinal);

        /// <summary>
        /// 行数 / 偏离声明起点的行数 / 相对上一拍仍在变的行数。
        /// </summary>
        /// <remarks>
        /// 顶栏那三个按钮唯一的可观测量，与加载模式那边的 <c>nomutual</c> 同一个思路：它不解释动画该到哪，
        /// 只说明有没有在动、有没有回到起点。只数采样器行 —— 别的行的"起点"由各自的台子宣布，不在这里说话。
        /// <para>
        /// <b>两个数得分开，缺一个就有假命题。</b> 只看"偏离起点"，静息时也是满的 —— 每行初始持有的是控件的默认值
        /// （零边距），不是采样器声明的起点，于是"全部启动后 &gt; 0"在什么都没跑时也成立。所以进界面时先把每一行
        /// 写成它声明的起点（见 Load 处理器），并另记一个"这一拍还在变"：
        /// 静息 0 / 启动后满 / 停止后 0 / 重置后 0 且回到起点。
        /// </para>
        /// </remarks>
        private (int Rows, int Away, int Moving) RowState()
        {
            var away = 0;
            var moving = 0;

            foreach (var sampler in SamplerProbe.SamplerNames)
            {
                var subject = _bench.SubjectFor(sampler);
                var now = SamplerProbe.Read(subject, sampler);

                if (!SamplerProbe.MatchesStart(subject, sampler)) away++;

                if (_rowPrevious.TryGetValue(sampler, out var before)
                    && !SamplerProbe.SameComponents(before, now.Components))
                {
                    moving++;
                }

                _rowPrevious[sampler] = now.Components;
            }

            return (SamplerProbe.SamplerNames.Count, away, moving);
        }

        private string Peak(int index)
            => double.IsNegativeInfinity(_targetPeaks[index]) ? "0" : _targetPeaks[index].ToString("F3");

        /// <summary>Writes a colour in a form a test can read: #rrggbb. WinForms' BackColor is always a solid colour, so the type-name branch the other demos need is unreachable here; the only other case is the no-colour sentinel, which must not read as black.</summary>
        private static string Describe(Color color)
            => color.IsEmpty ? "none" : $"#{color.R:x2}{color.G:x2}{color.B:x2}";

        private void UpdateReadout()
        {
            var mid = over1.BackColor;
            var ceiling = over3.BackColor;

            // 四个可读点：两条位移曲线同屏比、尺寸峰值（宽高应始终相等）、颜色通道是否越界或回绕
            readout.Text =
                $"位移 Back  目标 {MoveDistance,4}   当前 {over0.Left,4}"
                + $"        Elastic 当前 {over4.Left,4}"
                + $"        尺寸  目标 {SizeTarget,3}x{SizeTarget,3}   当前 {over2.Width,3}x{over2.Height,3}\r\n"
                + $"中段色③ 目标 {ColorMidTarget.R,3},{ColorMidTarget.G,3},{ColorMidTarget.B,3}   当前 {mid.R,3},{mid.G,3},{mid.B,3}"
                + $"        上限色⑤ 目标 {ColorCeilTarget.R,3},{ColorCeilTarget.G,3},{ColorCeilTarget.B,3}   当前 {ceiling.R,3},{ceiling.G,3},{ceiling.B,3}";
        }

        // -----------------------------------------------------------------------------------------------
        // 顶栏那两件（停止全部 / 重置）在案例列表这一侧的落点
        // -----------------------------------------------------------------------------------------------

        /// <summary>停止全部：案例列表里的每一行也在内，否则这个按钮的名字就是假的。原地冻结，不回到起点。</summary>
        private void StopAllRows()
        {
            StopBenchTimers();
            _liveSubject = null;

            StopSamplerRows();

            foreach (var target in ScenarioTargets())
                Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
        }

        /// <summary>
        /// 重置：整个界面回到各自的起点。
        /// </summary>
        /// <remarks>
        /// 加载、过冲、采样器三类各自走自己那行的重置动作 —— 少一类，"重置"这个名字就与它做的事对不上。
        /// 设计器里那条处理器（<c>ResetAnimations</c>）同时也在做加载那三条的同一件事：它留在原处是因为
        /// 它还负责状态栏文案，重复执行同一组同步写值不是问题。
        /// </remarks>
        private void ResetAllRows()
        {
            StopBenchTimers();
            _liveSubject = null;

            ResetCase(panel1, RestoreTranslateRow);
            ResetCase(panel2, RestoreRotateRow);
            ResetCase(panel3, RestoreCombineRow);

            foreach (var target in new Control[] { over0, over1, over2, over3, over4 })
                Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

            RestoreShiftStart();
            RestoreElasticStart();
            RestoreColorStart();
            RestoreSizeStart();
            RestoreGradientStart();

            ResetSamplerRows();
        }

        /// <summary>三类场景共用的八块目标：加载三块 + 过冲五块。采样器行自成一套，不在这里。</summary>
        private Control[] ScenarioTargets()
            => [panel1, panel2, panel3, over0, over1, over2, over3, over4];

        // -----------------------------------------------------------------------------------------------
        // 时间轴控制
        //
        // 这一排作用在加载模式那三块长动画（panel1/2/3）上，而不是过冲那五行：暂停一个 900ms 的一次性过冲
        // 在屏幕上和"它就是这么快"分不开，而这里同时也要给人看。三块各自是一条真 Transition，暂停/变速/定位
        // 都按 target 寻址，所以是逐个调用 —— 这本身就是"控制面挂在 target 上、不挂在快照上"的一次演示。
        // -----------------------------------------------------------------------------------------------

        /// <summary>时间轴控制的那三块目标：加载模式驱动的三条长动画的作用对象。</summary>
        private Control[] ControlTargets() => [panel1, panel2, panel3];

        private void PauseAll(object? sender, EventArgs e)
        {
            foreach (var target in ControlTargets())
            {
                Transition.Pause(target, IncludeMutual: true, IncludeNoMutual: true);
            }
        }

        private void ResumeAll(object? sender, EventArgs e)
        {
            foreach (var target in ControlTargets())
            {
                Transition.Resume(target, IncludeMutual: true, IncludeNoMutual: true);
            }
        }

        private void RateSlow(object? sender, EventArgs e) => SetRate(0.25d);

        private void RateFast(object? sender, EventArgs e) => SetRate(4d);

        /// <summary>
        /// 正常速。把速率调回 1。时间轴只有正速率 —— 减速之后要回到原速就靠这一个，而不是再去点一次重置。
        /// </summary>
        private void RateNormal(object? sender, EventArgs e) => SetRate(1d);

        private void SetRate(double rate)
        {
            foreach (var target in ControlTargets())
            {
                Transition.SetRate(target, rate, IncludeMutual: true, IncludeNoMutual: true);
            }
        }

        /// <summary>
        /// 跳到下一程的起点。程计数器是整数，所以"第几程"可以被指名 —— 这正是绝对时间轴需要它的原因。
        /// </summary>
        private void SeekNextPass(object? sender, EventArgs e)
        {
            foreach (var target in ControlTargets())
            {
                Transition.Seek(target, Transition.Cycle(target, IncludeMutual: true, IncludeNoMutual: true) + 1,
                    TimeSpan.Zero, IncludeMutual: true, IncludeNoMutual: true);
            }
        }
    }
}
