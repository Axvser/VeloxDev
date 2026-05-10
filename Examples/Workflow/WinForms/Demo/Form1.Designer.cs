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
            logTabControl = new TabControl();
            executionLogTab = new TabPage();
            agentLogTab = new TabPage();
            agentLogListBox = new ListBox();
            agentInputPanel = new Panel();
            agentInputTextBox = new TextBox();
            agentSendButton = new Button();
            toolbarPanel = new Panel();
            statusValueLabel = new Label();
            statusLabel = new Label();
            summaryLabel = new Label();
            reloadButton = new Button();
            stopButton = new Button();
            runButton = new Button();
            undoButton = new Button();
            redoButton = new Button();
            saveButton = new Button();
            selectButton = new Button();
            loadDemoButton = new Button();
            nodeCountLabel = new Label();
            visibleCountLabel = new Label();
            nodeCountCaptionLabel = new Label();
            visibleCountCaptionLabel = new Label();
            seedTextBox = new TextBox();
            seedLabel = new Label();
            titleLabel = new Label();
            executionLogListBox = new ListBox();
            workflowSurfaceControl = new Controls.WorkflowCanvas();
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
            splitContainer.Panel1.Controls.Add(logTabControl);
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
            toolbarPanel.Controls.Add(undoButton);
            toolbarPanel.Controls.Add(redoButton);
            toolbarPanel.Controls.Add(saveButton);
            toolbarPanel.Controls.Add(selectButton);
            toolbarPanel.Controls.Add(loadDemoButton);
            toolbarPanel.Controls.Add(nodeCountCaptionLabel);
            toolbarPanel.Controls.Add(nodeCountLabel);
            toolbarPanel.Controls.Add(visibleCountCaptionLabel);
            toolbarPanel.Controls.Add(visibleCountLabel);
            toolbarPanel.Controls.Add(seedTextBox);
            toolbarPanel.Controls.Add(seedLabel);
            toolbarPanel.Controls.Add(titleLabel);
            toolbarPanel.Dock = DockStyle.Top;
            toolbarPanel.Location = new Point(12, 12);
            toolbarPanel.Name = "toolbarPanel";
            toolbarPanel.Size = new Size(376, 248);
            toolbarPanel.TabIndex = 0;
            // 
            // statusValueLabel
            // 
            statusValueLabel.AutoSize = true;
            statusValueLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point, 134);
            statusValueLabel.Location = new Point(313, 42);
            statusValueLabel.Name = "statusValueLabel";
            statusValueLabel.Size = new Size(37, 19);
            statusValueLabel.TabIndex = 10;
            statusValueLabel.Text = "空闲";
            // 
            // statusLabel
            // 
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(266, 44);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(43, 17);
            statusLabel.TabIndex = 9;
            statusLabel.Text = "状态：";
            // 
            // summaryLabel
            // 
            summaryLabel.Visible = false;
            summaryLabel.AutoSize = true;
            summaryLabel.Location = new Point(4, 248);
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
            // undoButton
            // 
            undoButton.Location = new Point(4, 122);
            undoButton.Name = "undoButton";
            undoButton.Size = new Size(80, 32);
            undoButton.TabIndex = 11;
            undoButton.Text = "Undo";
            undoButton.UseVisualStyleBackColor = true;
            undoButton.Click += UndoWorkflow;
            // 
            // redoButton
            // 
            redoButton.Location = new Point(90, 122);
            redoButton.Name = "redoButton";
            redoButton.Size = new Size(80, 32);
            redoButton.TabIndex = 12;
            redoButton.Text = "Redo";
            redoButton.UseVisualStyleBackColor = true;
            redoButton.Click += RedoWorkflow;
            // 
            // saveButton
            // 
            saveButton.Location = new Point(176, 122);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(80, 32);
            saveButton.TabIndex = 13;
            saveButton.Text = "Save";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += SaveWorkflow;
            // 
            // selectButton
            // 
            selectButton.Location = new Point(4, 160);
            selectButton.Name = "selectButton";
            selectButton.Size = new Size(80, 32);
            selectButton.TabIndex = 14;
            selectButton.Text = "Select";
            selectButton.UseVisualStyleBackColor = true;
            selectButton.Click += SelectWorkflow;
            // 
            // loadDemoButton
            // 
            loadDemoButton.Location = new Point(90, 160);
            loadDemoButton.Name = "loadDemoButton";
            loadDemoButton.Size = new Size(166, 32);
            loadDemoButton.TabIndex = 15;
            loadDemoButton.Text = "Load Workflow Demo";
            loadDemoButton.UseVisualStyleBackColor = true;
            loadDemoButton.Click += LoadNetworkDemo;
            // 
            // nodeCountCaptionLabel
            // 
            nodeCountCaptionLabel.AutoSize = true;
            nodeCountCaptionLabel.Location = new Point(4, 202);
            nodeCountCaptionLabel.Name = "nodeCountCaptionLabel";
            nodeCountCaptionLabel.TabIndex = 16;
            nodeCountCaptionLabel.Text = "节点总数：";
            // 
            // nodeCountLabel
            // 
            nodeCountLabel.AutoSize = true;
            nodeCountLabel.Location = new Point(80, 202);
            nodeCountLabel.Name = "nodeCountLabel";
            nodeCountLabel.TabIndex = 17;
            nodeCountLabel.Text = "0";
            // 
            // visibleCountCaptionLabel
            // 
            visibleCountCaptionLabel.AutoSize = true;
            visibleCountCaptionLabel.Location = new Point(4, 222);
            visibleCountCaptionLabel.Name = "visibleCountCaptionLabel";
            visibleCountCaptionLabel.TabIndex = 18;
            visibleCountCaptionLabel.Text = "可见组件数：";
            // 
            // visibleCountLabel
            // 
            visibleCountLabel.AutoSize = true;
            visibleCountLabel.Location = new Point(80, 222);
            visibleCountLabel.Name = "visibleCountLabel";
            visibleCountLabel.TabIndex = 19;
            visibleCountLabel.Text = "0";
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
            // logTabControl
            // 
            logTabControl.Dock = DockStyle.Fill;
            logTabControl.TabPages.Add(agentLogTab);
            logTabControl.TabPages.Add(executionLogTab);
            logTabControl.Name = "logTabControl";
            logTabControl.TabIndex = 3;
            // 
            // agentLogTab
            // 
            agentLogTab.Text = "Agent Chat";
            agentLogTab.Padding = new Padding(4);
            agentLogTab.Controls.Add(agentLogListBox);
            agentLogTab.Controls.Add(agentInputPanel);
            // 
            // agentLogListBox
            // 
            agentLogListBox.Dock = DockStyle.Fill;
            agentLogListBox.Font = new Font("Consolas", 10F);
            agentLogListBox.FormattingEnabled = true;
            agentLogListBox.ItemHeight = 15;
            agentLogListBox.Name = "agentLogListBox";
            agentLogListBox.TabIndex = 0;
            agentLogListBox.HorizontalScrollbar = true;
            // 
            // agentInputPanel
            // 
            agentInputPanel.Dock = DockStyle.Bottom;
            agentInputPanel.Height = 36;
            agentInputPanel.Padding = new Padding(0, 4, 0, 0);
            agentInputPanel.Controls.Add(agentSendButton);
            agentInputPanel.Controls.Add(agentInputTextBox);
            agentInputPanel.Name = "agentInputPanel";
            agentInputPanel.TabIndex = 1;
            // 
            // agentInputTextBox
            // 
            agentInputTextBox.Dock = DockStyle.Fill;
            agentInputTextBox.Name = "agentInputTextBox";
            agentInputTextBox.TabIndex = 0;
            agentInputTextBox.PlaceholderText = "输入消息...";
            agentInputTextBox.KeyDown += OnAgentInputKeyDown;
            // 
            // agentSendButton
            // 
            agentSendButton.Dock = DockStyle.Right;
            agentSendButton.Width = 70;
            agentSendButton.Text = "发送";
            agentSendButton.Name = "agentSendButton";
            agentSendButton.TabIndex = 1;
            agentSendButton.UseVisualStyleBackColor = true;
            agentSendButton.Click += OnSendToAgent;
            // 
            // executionLogTab
            // 
            executionLogTab.Text = "Execution Log";
            executionLogTab.Padding = new Padding(4);
            executionLogTab.Controls.Add(executionLogListBox);
            // 
            // executionLogListBox
            // 
            executionLogListBox.Dock = DockStyle.Fill;
            executionLogListBox.Font = new Font("Consolas", 10F);
            executionLogListBox.FormattingEnabled = true;
            executionLogListBox.ItemHeight = 15;
            executionLogListBox.Name = "executionLogListBox";
            executionLogListBox.TabIndex = 0;
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
        private Button undoButton;
        private Button redoButton;
        private Button saveButton;
        private Button selectButton;
        private Button loadDemoButton;
        private Label nodeCountCaptionLabel;
        private Label nodeCountLabel;
        private Label visibleCountCaptionLabel;
        private Label visibleCountLabel;
        private TextBox seedTextBox;
        private Label seedLabel;
        private Label titleLabel;
        private TabControl logTabControl;
        private TabPage agentLogTab;
        private TabPage executionLogTab;
        private ListBox agentLogListBox;
        private Panel agentInputPanel;
        private TextBox agentInputTextBox;
        private Button agentSendButton;
        private ListBox executionLogListBox;
        private Controls.WorkflowCanvas workflowSurfaceControl;
    }
}
