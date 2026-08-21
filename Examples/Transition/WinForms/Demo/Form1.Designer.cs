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
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
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

            // The animation targets a Control; the library marshals directly to its UI thread via
            // Control.Invoke / BeginInvoke, so no capture is needed even when the animation is first
            // started from a background thread (Task.Run) below.
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Form settings
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1000, 700);
            this.Text = "VeloxDev WinForms 动画演示";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;

            // Create controls
            CreateControls();

            this.ResumeLayout(false);
        }

        private void CreateControls()
        {
            // Create the three demo panels
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

            // Create buttons
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

            // Status label
            lblStatus = new Label
            {
                Text = "点击“开始动画”按钮启动演示",
                Location = new Point(100, 320),
                Size = new Size(400, 30),
                Font = new Font("微软雅黑", 10),
                ForeColor = Color.DarkBlue
            };

            // Description label
            var lblDescription = new Label
            {
                Text = "VeloxDev动画演示 - 红色面板：移动 + 父容器背景色(嵌套属性)，绿色面板：缩放动画，蓝色面板：组合动画",
                Location = new Point(100, 50),
                Size = new Size(600, 30),
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Gray
            };

            // Add controls to the form
            this.Controls.AddRange(new Control[] {
                panel1, panel2, panel3,
                btnStart, btnReset, btnExit,
                btnStartNonMutual, btnStartRepeatedMutual,
                btnStartMainThread, btnStartMainThreadNonMutual,
                lblStatus, lblDescription
            });

            // Register events
            btnStart.Click += StartAnimations;
            btnReset.Click += ResetAnimations;
            btnExit.Click += ExitAnimations;
            btnStartNonMutual.Click += StartAnimationsNonMutual;
            btnStartRepeatedMutual.Click += StartRepeatedMutual;
            btnStartMainThread.Click += StartAnimationsMainThread;
            btnStartMainThreadNonMutual.Click += StartAnimationsMainThreadNonMutual;

            // Form load event
            this.Load += Form1_Load;
        }

        #endregion

        private void Form1_Load(object sender, System.EventArgs e)
        {

            // Snapshot(...) records explicitly specified property paths; SnapshotAll() automatically
            // records all animatable properties of the current object
            initialSnapshot1 = panel1.Snapshot(x => x.Location, x => x.BackColor, x => x.Parent.BackColor);
            initialSnapshot2 = panel2.SnapshotAll();
            initialSnapshot3 = panel3.SnapshotAll();

            lblStatus.Text = "系统就绪，可以开始动画演示";
        }

        // Save initial snapshots for reset
        private Transition<Panel>.StateSnapshot initialSnapshot1;
        private Transition<Panel>.StateSnapshot initialSnapshot2;
        private Transition<Panel>.StateSnapshot initialSnapshot3;

        private void StartAnimations(object sender, System.EventArgs e)
        {
            lblStatus.Text = "动画执行中...";
            btnStart.Enabled = false;

            // Run animations on a non-UI thread (the framework switches to the UI thread automatically)
            _ = Task.Run(() =>
            {
                try
                {
                    // Run the animations for the three panels
                    Animation0.Execute(panel1);
                    Animation1.Execute(panel2);
                    Animation2.Execute(panel3);

                    // Update the status after the animations complete
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
            // Stop all animations (including non-mutual)
            Transition.Exit(panel1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(panel2, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(panel3, IncludeMutual: true, IncludeNoMutual: true);

            // Reset to the initial state
            initialSnapshot1.Effect(TransitionEffects.Empty).Execute(panel1);
            initialSnapshot2.Effect(TransitionEffects.Empty).Execute(panel2);
            initialSnapshot3.Effect(TransitionEffects.Empty).Execute(panel3);

            lblStatus.Text = "已重置到初始状态";
        }

        private void ExitAnimations(object sender, System.EventArgs e)
        {
            // Stop all animations (including non-mutual)
            Transition.Exit(panel1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(panel2, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(panel3, IncludeMutual: true, IncludeNoMutual: true);

            lblStatus.Text = "动画已停止";
        }

        private void StartAnimationsNonMutual(object sender, System.EventArgs e)
        {
            // CanMutualTask: false — the three animations run concurrently without interference and
            // are not cancelled by one another
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
            // Each click starts a mutually-exclusive animation on panel1: the new animation cancels
            // the previous one (tests scheduler gating and cancellation).
            lblStatus.Text = "连续互斥动画（每次点击替换上一次）...";
            _ = Task.Run(() =>
            {
                Animation0.Execute(panel1); // CanMutualTask: true (default)
            });
        }

        private void StartAnimationsMainThread(object sender, System.EventArgs e)
        {
            // Start directly on the main (UI) thread; mutual exclusion (CanMutualTask: true by default)
            lblStatus.Text = "主线程互斥动画执行中...";
            Animation0.Execute(panel1);
            Animation1.Execute(panel2);
            Animation2.Execute(panel3);
        }

        private void StartAnimationsMainThreadNonMutual(object sender, System.EventArgs e)
        {
            // Main thread + CanMutualTask: false — run concurrently, neither cancels the other
            lblStatus.Text = "主线程并发动画执行中（CanMutualTask: false）...";
            Animation0.Execute(panel1, CanMutualTask: false);
            Animation1.Execute(panel2, CanMutualTask: false);
            Animation2.Execute(panel3, CanMutualTask: false);
        }

        // Animation definitions
        private static readonly Transition<Control>.StateSnapshot Animation0 =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(600, 100))  // move to the right
                .Property(c => c.Parent.BackColor, Color.Moccasin) // demonstrates nested property animation
                .Property(c => c.BackColor, Color.Orange)         // turns orange
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(3),
                    IsAutoReverse = true,
                    LoopTime = 1,
                    Ease = Eases.Quad.Out
                });

        private static readonly Transition<Control>.StateSnapshot Animation1 =
            Transition<Control>.Create()
                .Await(TimeSpan.FromSeconds(1))  // starts after a 1 second delay
                .Property(c => c.Size, new Size(150, 150))  // enlarge
                .Property(c => c.BackColor, Color.LightGreen)  // turns light green
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                    Ease = Eases.Cubic.InOut
                });

        private static readonly Transition<Control>.StateSnapshot Animation2 =
            Transition<Control>.Create()
                .Property(c => c.Location, new Point(400, 400))  // move to the bottom right
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Ease = Eases.Circ.InOut
                })
                .AwaitThen(TimeSpan.FromSeconds(1))  // wait 1 second
                .Property(c => c.Size, new Size(120, 120))  // shrink slightly
                .Property(c => c.BackColor, Color.Purple)   // turns purple
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(1.5),
                    Ease = Eases.Sine.In
                })
                .AwaitThen(TimeSpan.FromSeconds(0.5))  // wait another 0.5 seconds
                .Property(c => c.Location, new Point(100, 400))  // move to the bottom left
                .Property(c => c.BackColor, Color.Teal)  // turns teal
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Ease = Eases.Back.Out
                });
    }
}

