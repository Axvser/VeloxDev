using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace Demo
{
    partial class Form1
    {
        private Panel panel1;
        private Panel panel2;
        private Panel panel3;
        private Button btnStart;
        private Button btnReset;
        private Button btnExit;
        private Button btnStartNonMutual;
        private Button btnStartRepeatedMutual;
        private Button btnStartMainThread;
        private Button btnStartMainThreadNonMutual;
        private Label lblStatus;

        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // 动画目标是 Control，库会直接用 Control.Invoke / BeginInvoke 编组到它的 UI 线程，
            // 即便下方从后台线程（Task.Run）首次启动动画，也无需任何捕获。
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();

            // 窗体设置
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1000, 700);
            this.Text = "VeloxDev WinForms 动画演示";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;

            // 创建控件
            CreateControls();

            this.ResumeLayout(false);
        }

        private void CreateControls()
        {
            // 创建三个演示面板
            panel1 = new Panel
            {
                Name = "panel1",
                Size = new Size(100, 100),
                Location = new Point(100, 100),
                BackColor = Color.Red,
                BorderStyle = BorderStyle.FixedSingle
            };

            panel2 = new Panel
            {
                Name = "panel2",
                Size = new Size(100, 100),
                Location = new Point(250, 100),
                BackColor = Color.Green,
                BorderStyle = BorderStyle.FixedSingle
            };

            panel3 = new Panel
            {
                Name = "panel3",
                Size = new Size(100, 100),
                Location = new Point(400, 100),
                BackColor = Color.Blue,
                BorderStyle = BorderStyle.FixedSingle
            };

            // 创建按钮
            btnStart = new Button
            {
                Text = "后台线程互斥",
                Location = new Point(100, 250),
                Size = new Size(100, 40),
                BackColor = Color.LightBlue,
                Font = new Font("微软雅黑", 10)
            };

            btnReset = new Button
            {
                Text = "重置",
                Location = new Point(220, 250),
                Size = new Size(100, 40),
                BackColor = Color.LightGreen,
                Font = new Font("微软雅黑", 10)
            };

            btnExit = new Button
            {
                Text = "停止全部",
                Location = new Point(340, 250),
                Size = new Size(100, 40),
                BackColor = Color.LightCoral,
                Font = new Font("微软雅黑", 10)
            };

            btnStartNonMutual = new Button
            {
                Text = "后台线程并发",
                Location = new Point(460, 250),
                Size = new Size(100, 40),
                BackColor = Color.LightCyan,
                Font = new Font("微软雅黑", 10)
            };

            btnStartRepeatedMutual = new Button
            {
                Text = "连续互斥",
                Location = new Point(580, 250),
                Size = new Size(100, 40),
                BackColor = Color.MistyRose,
                Font = new Font("微软雅黑", 10)
            };

            btnStartMainThread = new Button
            {
                Text = "主线程互斥",
                Location = new Point(700, 250),
                Size = new Size(100, 40),
                BackColor = Color.LightGoldenrodYellow,
                Font = new Font("微软雅黑", 10)
            };

            btnStartMainThreadNonMutual = new Button
            {
                Text = "主线程并发",
                Location = new Point(820, 250),
                Size = new Size(100, 40),
                BackColor = Color.PaleGreen,
                Font = new Font("微软雅黑", 10)
            };

            // 状态标签
            lblStatus = new Label
            {
                Text = "点击“开始动画”按钮启动演示",
                Location = new Point(100, 320),
                Size = new Size(400, 30),
                Font = new Font("微软雅黑", 10),
                ForeColor = Color.DarkBlue
            };

            // 说明标签
            var lblDescription = new Label
            {
                Text = "VeloxDev动画演示 - 红色面板：移动 + 父容器背景色(嵌套属性)，绿色面板：缩放动画，蓝色面板：组合动画",
                Location = new Point(100, 50),
                Size = new Size(600, 30),
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Gray
            };

            // 添加控件到窗体
            this.Controls.AddRange(new Control[] {
                panel1, panel2, panel3,
                btnStart, btnReset, btnExit,
                btnStartNonMutual, btnStartRepeatedMutual,
                btnStartMainThread, btnStartMainThreadNonMutual,
                lblStatus, lblDescription
            });

            // 注册事件
            btnStart.Click += StartAnimations;
            btnReset.Click += ResetAnimations;
            btnExit.Click += ExitAnimations;
            btnStartNonMutual.Click += StartAnimationsNonMutual;
            btnStartRepeatedMutual.Click += StartRepeatedMutual;
            btnStartMainThread.Click += StartAnimationsMainThread;
            btnStartMainThreadNonMutual.Click += StartAnimationsMainThreadNonMutual;

            // 窗体加载事件
            this.Load += Form1_Load;
        }

        #endregion

        private void Form1_Load(object sender, System.EventArgs e)
        {

            // Snapshot(...) 记录显式指定的属性路径，SnapshotAll() 自动记录当前对象中可动画的属性
            initialSnapshot1 = panel1.Snapshot(x => x.Location, x => x.BackColor, x => x.Parent.BackColor);
            initialSnapshot2 = panel2.SnapshotAll();
            initialSnapshot3 = panel3.SnapshotAll();

            lblStatus.Text = "系统就绪，可以开始动画演示";
        }

        // 保存初始快照用于重置
        private Transition<Panel>.StateSnapshot initialSnapshot1;
        private Transition<Panel>.StateSnapshot initialSnapshot2;
        private Transition<Panel>.StateSnapshot initialSnapshot3;

        private void StartAnimations(object sender, System.EventArgs e)
        {
            lblStatus.Text = "动画执行中...";
            btnStart.Enabled = false;

            // 在非UI线程中执行动画（框架会自动切换到UI线程）
            _ = Task.Run(() =>
            {
                try
                {
                    // 执行三个面板的动画
                    Animation0.Execute(panel1);
                    Animation1.Execute(panel2);
                    Animation2.Execute(panel3);

                    // 动画完成后更新状态
                    this.Invoke(new Action(() =>
                    {
                        lblStatus.Text = "动画执行完成";
                        btnStart.Enabled = true;
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() =>
                    {
                        lblStatus.Text = $"动画执行错误: {ex.Message}";
                        btnStart.Enabled = true;
                    }));
                }
            });
        }

        private void ResetAnimations(object sender, System.EventArgs e)
        {
            // 停止所有动画（含非互斥）
            Transition.Exit(panel1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(panel2, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(panel3, IncludeMutual: true, IncludeNoMutual: true);

            // 重置到初始状态
            initialSnapshot1.Effect(TransitionEffects.Empty).Execute(panel1);
            initialSnapshot2.Effect(TransitionEffects.Empty).Execute(panel2);
            initialSnapshot3.Effect(TransitionEffects.Empty).Execute(panel3);

            lblStatus.Text = "已重置到初始状态";
        }

        private void ExitAnimations(object sender, System.EventArgs e)
        {
            // 停止所有动画（含非互斥）
            Transition.Exit(panel1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(panel2, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(panel3, IncludeMutual: true, IncludeNoMutual: true);

            lblStatus.Text = "动画已停止";
        }

        private void StartAnimationsNonMutual(object sender, System.EventArgs e)
        {
            // CanMutualTask: false —— 三段动画互不干扰地并发运行，不会被彼此取消
            lblStatus.Text = "并发动画执行中（CanMutualTask: false）...";
            btnStart.Enabled = false;

            _ = Task.Run(() =>
            {
                try
                {
                    Animation0.Execute(panel1, CanMutualTask: false);
                    Animation1.Execute(panel2, CanMutualTask: false);
                    Animation2.Execute(panel3, CanMutualTask: false);

                    this.Invoke(new Action(() =>
                    {
                        lblStatus.Text = "并发动画执行完成";
                        btnStart.Enabled = true;
                    }));
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(() =>
                    {
                        lblStatus.Text = $"并发动画执行错误: {ex.Message}";
                        btnStart.Enabled = true;
                    }));
                }
            });
        }

        private void StartRepeatedMutual(object sender, System.EventArgs e)
        {
            // 每次点击都在 panel1 上启动互斥动画：新动画会取消上一次（测试调度器门控与取消）
            lblStatus.Text = "连续互斥动画（每次点击替换上一次）...";
            _ = Task.Run(() =>
            {
                Animation0.Execute(panel1); // CanMutualTask: true（默认）
            });
        }

        private void StartAnimationsMainThread(object sender, System.EventArgs e)
        {
            // 主线程（UI 线程）直接启动，互斥（CanMutualTask: true 默认）
            lblStatus.Text = "主线程互斥动画执行中...";
            Animation0.Execute(panel1);
            Animation1.Execute(panel2);
            Animation2.Execute(panel3);
        }

        private void StartAnimationsMainThreadNonMutual(object sender, System.EventArgs e)
        {
            // 主线程 + CanMutualTask: false —— 并发运行，互不取消
            lblStatus.Text = "主线程并发动画执行中（CanMutualTask: false）...";
            Animation0.Execute(panel1, CanMutualTask: false);
            Animation1.Execute(panel2, CanMutualTask: false);
            Animation2.Execute(panel3, CanMutualTask: false);
        }

        // 动画定义
        private static readonly Transition<Control>.StateSnapshot Animation0 =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(600, 100))  // 移动到右侧
                .Property(c => c.Parent.BackColor, Color.Moccasin) // 演示嵌套属性动画
                .Property(c => c.BackColor, Color.Orange)         // 变为橙色
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    IsAutoReverse = true,
                    LoopTime = 1,
                    Ease = Eases.Quad.Out
                });

        private static readonly Transition<Control>.StateSnapshot Animation1 =
            Transition<Control>.Create()
                .Await(TimeSpan.FromSeconds(1))  // 延迟1秒开始
                .Property(c => c.Size, new Size(150, 150))  // 放大
                .Property(c => c.BackColor, Color.LightGreen)  // 变为浅绿色
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                    Ease = Eases.Cubic.InOut
                });

        private static readonly Transition<Control>.StateSnapshot Animation2 =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(400, 400))  // 移动到右下角
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Ease = Eases.Circ.InOut
                })
                .AwaitThen(TimeSpan.FromSeconds(1))  // 等待1秒
                .Property(c => c.Size, new Size(120, 120))  // 稍微缩小
                .Property(c => c.BackColor, Color.Purple)   // 变为紫色
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(1.5),
                    Ease = Eases.Sine.In
                })
                .AwaitThen(TimeSpan.FromSeconds(0.5))  // 再等待0.5秒
                .Property(c => c.Location, new Point(100, 400))  // 移动到左下角
                .Property(c => c.BackColor, Color.Teal)  // 变为青绿色
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Ease = Eases.Back.Out
                });
    }
}

