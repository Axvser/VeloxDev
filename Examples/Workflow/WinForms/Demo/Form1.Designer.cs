namespace Demo
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            splitContainer = new SplitContainer();
            toolbarPanel = new Panel();
            statusValueLabel = new Label();
            statusLabel = new Label();
            summaryLabel = new Label();
            reloadButton = new Button();
            stopButton = new Button();
            runButton = new Button();
            seedTextBox = new TextBox();
            seedLabel = new Label();
            titleLabel = new Label();
            executionLogListBox = new ListBox();
            executionLogLabel = new Label();
            workflowSurfaceControl = new Controls.WorkflowSurfaceControl();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            toolbarPanel.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer
            // 
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.FixedPanel = FixedPanel.Panel1;
            splitContainer.Location = new Point(0, 0);
            splitContainer.Name = "splitContainer";
            splitContainer.Panel1.Controls.Add(executionLogListBox);
            splitContainer.Panel1.Controls.Add(executionLogLabel);
            splitContainer.Panel1.Controls.Add(toolbarPanel);
            splitContainer.Panel1.Padding = new Padding(12);
            splitContainer.Panel2.Controls.Add(workflowSurfaceControl);
            splitContainer.Panel2.Padding = new Padding(12);
            splitContainer.Size = new Size(1440, 820);
            splitContainer.SplitterDistance = 400;
            splitContainer.TabIndex = 0;
            // 
            // toolbarPanel
            // 
            toolbarPanel.Controls.Add(statusValueLabel);
            toolbarPanel.Controls.Add(statusLabel);
            toolbarPanel.Controls.Add(summaryLabel);
            toolbarPanel.Controls.Add(reloadButton);
            toolbarPanel.Controls.Add(stopButton);
            toolbarPanel.Controls.Add(runButton);
            toolbarPanel.Controls.Add(seedTextBox);
            toolbarPanel.Controls.Add(seedLabel);
            toolbarPanel.Controls.Add(titleLabel);
            toolbarPanel.Dock = DockStyle.Top;
            toolbarPanel.Location = new Point(12, 12);
            toolbarPanel.Name = "toolbarPanel";
            toolbarPanel.Size = new Size(376, 220);
            toolbarPanel.TabIndex = 0;
            // 
            // statusValueLabel
            // 
            statusValueLabel.AutoSize = true;
            statusValueLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 134);
            statusValueLabel.Location = new Point(247, 120);
            statusValueLabel.Name = "statusValueLabel";
            statusValueLabel.Size = new Size(37, 19);
            statusValueLabel.TabIndex = 10;
            statusValueLabel.Text = "空闲";
            // 
            // statusLabel
            // 
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(190, 122);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(43, 17);
            statusLabel.TabIndex = 9;
            statusLabel.Text = "状态：";
            // 
            // summaryLabel
            // 
            summaryLabel.AutoSize = true;
            summaryLabel.Location = new Point(4, 152);
            summaryLabel.MaximumSize = new Size(360, 0);
            summaryLabel.Name = "summaryLabel";
            summaryLabel.Size = new Size(356, 51);
            summaryLabel.TabIndex = 8;
            summaryLabel.Text = "示例链路展示最简化的 Workflow 行为：节点只模拟耗时执行，并直接向下游广播。";
            // 
            // reloadButton
            // 
            reloadButton.Location = new Point(252, 78);
            reloadButton.Name = "reloadButton";
            reloadButton.Size = new Size(108, 32);
            reloadButton.TabIndex = 7;
            reloadButton.Text = "重置示例";
            reloadButton.UseVisualStyleBackColor = true;
            reloadButton.Click += ReloadWorkflow;
            // 
            // stopButton
            // 
            stopButton.Location = new Point(128, 78);
            stopButton.Name = "stopButton";
            stopButton.Size = new Size(108, 32);
            stopButton.TabIndex = 6;
            stopButton.Text = "停止工作流";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopWorkflow;
            // 
            // runButton
            // 
            runButton.Location = new Point(4, 78);
            runButton.Name = "runButton";
            runButton.Size = new Size(108, 32);
            runButton.TabIndex = 5;
            runButton.Text = "运行工作流";
            runButton.UseVisualStyleBackColor = true;
            runButton.Click += RunWorkflow;
            // 
            // seedTextBox
            // 
            seedTextBox.Location = new Point(95, 39);
            seedTextBox.Name = "seedTextBox";
            seedTextBox.Size = new Size(265, 23);
            seedTextBox.TabIndex = 2;
            // 
            // seedLabel
            // 
            seedLabel.AutoSize = true;
            seedLabel.Location = new Point(4, 42);
            seedLabel.Name = "seedLabel";
            seedLabel.Size = new Size(87, 17);
            seedLabel.TabIndex = 1;
            seedLabel.Text = "Seed Payload";
            // 
            // titleLabel
            // 
            titleLabel.AutoSize = true;
            titleLabel.Font = new Font("Microsoft YaHei UI", 15.75F, FontStyle.Bold, GraphicsUnit.Point, 134);
            titleLabel.Location = new Point(0, 0);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(262, 28);
            titleLabel.TabIndex = 0;
            titleLabel.Text = "VeloxDev Workflow Demo";
            // 
            // executionLogListBox
            // 
            executionLogListBox.Dock = DockStyle.Fill;
            executionLogListBox.Font = new Font("Consolas", 10F);
            executionLogListBox.FormattingEnabled = true;
            executionLogListBox.ItemHeight = 15;
            executionLogListBox.Location = new Point(12, 261);
            executionLogListBox.Name = "executionLogListBox";
            executionLogListBox.Size = new Size(376, 547);
            executionLogListBox.TabIndex = 2;
            // 
            // executionLogLabel
            // 
            executionLogLabel.Dock = DockStyle.Top;
            executionLogLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 134);
            executionLogLabel.Location = new Point(12, 232);
            executionLogLabel.Name = "executionLogLabel";
            executionLogLabel.Padding = new Padding(0, 8, 0, 8);
            executionLogLabel.Size = new Size(376, 29);
            executionLogLabel.TabIndex = 1;
            executionLogLabel.Text = "Execution Log";
            // 
            // workflowSurfaceControl
            // 
            workflowSurfaceControl.Dock = DockStyle.Fill;
            workflowSurfaceControl.Location = new Point(12, 12);
            workflowSurfaceControl.Name = "workflowSurfaceControl";
            workflowSurfaceControl.Size = new Size(1000, 796);
            workflowSurfaceControl.TabIndex = 0;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1440, 820);
            Controls.Add(splitContainer);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "VeloxDev Workflow Demo";
            WindowState = FormWindowState.Maximized;
            FormClosing += OnFormClosing;
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            toolbarPanel.ResumeLayout(false);
            toolbarPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer splitContainer;
        private Panel toolbarPanel;
        private Label statusValueLabel;
        private Label statusLabel;
        private Label summaryLabel;
        private Button reloadButton;
        private Button stopButton;
        private Button runButton;
        private TextBox seedTextBox;
        private Label seedLabel;
        private Label titleLabel;
        private ListBox executionLogListBox;
        private Label executionLogLabel;
        private Controls.WorkflowSurfaceControl workflowSurfaceControl;
    }
}
