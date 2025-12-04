using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WinForms.PlatformAdapters;

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

            // VeloxDev库在 WinForms 加载动画时，必须捕获一次主线程
            UIThreadInspector.CaptureUIThread();
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
                Text = "开始动画",
                Location = new Point(100, 250),
                Size = new Size(100, 40),
                BackColor = Color.LightBlue,
                Font = new Font("微软雅黑", 10)
            };

            btnReset = new Button
            {
                Text = "重置状态",
                Location = new Point(220, 250),
                Size = new Size(100, 40),
                BackColor = Color.LightGreen,
                Font = new Font("微软雅黑", 10)
            };

            btnExit = new Button
            {
                Text = "停止动画",
                Location = new Point(340, 250),
                Size = new Size(100, 40),
                BackColor = Color.LightCoral,
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
                Text = "VeloxDev动画演示 - 红色面板：移动动画，绿色面板：缩放动画，蓝色面板：组合动画",
                Location = new Point(100, 50),
                Size = new Size(600, 30),
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Gray
            };

            // 添加控件到窗体
            this.Controls.AddRange(new Control[] {
                panel1, panel2, panel3,
                btnStart, btnReset, btnExit,
                lblStatus, lblDescription
            });

            // 注册事件
            btnStart.Click += StartAnimations;
            btnReset.Click += ResetAnimations;
            btnExit.Click += ExitAnimations;

            // 窗体加载事件
            this.Load += Form1_Load;
        }

        #endregion

        private void Form1_Load(object sender, System.EventArgs e)
        {

            // 创建初始快照
            initialSnapshot1 = panel1.Snapshot();
            initialSnapshot2 = panel2.Snapshot();
            initialSnapshot3 = panel3.Snapshot();

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
            // 停止所有动画
            Transition.Exit(panel1);
            Transition.Exit(panel2);
            Transition.Exit(panel3);

            // 重置到初始状态
            initialSnapshot1.Effect(TransitionEffects.Empty).Execute(panel1);
            initialSnapshot2.Effect(TransitionEffects.Empty).Execute(panel2);
            initialSnapshot3.Effect(TransitionEffects.Empty).Execute(panel3);

            lblStatus.Text = "已重置到初始状态";
        }

        private void ExitAnimations(object sender, System.EventArgs e)
        {
            // 停止所有动画
            Transition.Exit(panel1);
            Transition.Exit(panel2);
            Transition.Exit(panel3);

            lblStatus.Text = "动画已停止";
        }

        // 动画定义
        private static readonly Transition<Control>.StateSnapshot Animation0 =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(600, 100))  // 移动到右侧
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

