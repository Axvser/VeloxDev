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
            reloadButton = new Button();
            undoButton = new Button();
            redoButton = new Button();
            saveButton = new Button();
            selectButton = new Button();
            permissionModeLabel = new Label();
            permissionModePicker = new ComboBox();
            agentLogListBox = new ListBox();
            agentInputPanel = new Panel();
            agentInputTextBox = new TextBox();
            agentSendButton = new Button();
            executionLogListBox = new ListBox();
            workflowSurfaceControl = new Views.Workflow.WorkflowCanvas();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.SuspendLayout();
            SuspendLayout();
            //
            // splitContainer
            //
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.FixedPanel = FixedPanel.Panel1;
            splitContainer.Location = new Point(0, 0);
            splitContainer.Name = "splitContainer";
            splitContainer.Size = new Size(1440, 820);
            splitContainer.SplitterDistance = 400;
            splitContainer.TabIndex = 0;
            //
            // reloadButton
            //
            reloadButton.Name = "reloadButton";
            reloadButton.TabIndex = 7;
            reloadButton.Text = "重置示例";
            reloadButton.UseVisualStyleBackColor = true;
            reloadButton.Click += ReloadWorkflow;
            //
            // undoButton
            //
            undoButton.Name = "undoButton";
            undoButton.TabIndex = 11;
            undoButton.Text = "Undo";
            undoButton.UseVisualStyleBackColor = true;
            undoButton.Click += UndoWorkflow;
            //
            // redoButton
            //
            redoButton.Name = "redoButton";
            redoButton.TabIndex = 12;
            redoButton.Text = "Redo";
            redoButton.UseVisualStyleBackColor = true;
            redoButton.Click += RedoWorkflow;
            //
            // saveButton
            //
            saveButton.Name = "saveButton";
            saveButton.TabIndex = 13;
            saveButton.Text = "Save";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += SaveWorkflow;
            //
            // selectButton
            //
            selectButton.Name = "selectButton";
            selectButton.TabIndex = 14;
            selectButton.Text = "Load";
            selectButton.UseVisualStyleBackColor = true;
            selectButton.Click += SelectWorkflow;
            //
            // permissionModeLabel
            //
            permissionModeLabel.AutoSize = true;
            permissionModeLabel.Name = "permissionModeLabel";
            permissionModeLabel.TabIndex = 25;
            permissionModeLabel.Text = "权限模式：";
            //
            // permissionModePicker
            //
            permissionModePicker.DropDownStyle = ComboBoxStyle.DropDownList;
            permissionModePicker.Name = "permissionModePicker";
            permissionModePicker.TabIndex = 26;
            permissionModePicker.SelectedIndexChanged += OnPermissionModeChanged;
            //
            // agentLogListBox
            //
            agentLogListBox.Dock = DockStyle.Fill;
            agentLogListBox.FormattingEnabled = true;
            agentLogListBox.HorizontalScrollbar = true;
            agentLogListBox.ItemHeight = 15;
            agentLogListBox.Name = "agentLogListBox";
            agentLogListBox.TabIndex = 0;
            //
            // agentInputPanel
            //
            agentInputPanel.Height = 36;
            agentInputPanel.Name = "agentInputPanel";
            agentInputPanel.Padding = new Padding(0, 4, 0, 0);
            agentInputPanel.TabIndex = 1;
            agentInputPanel.Controls.Add(agentSendButton);
            agentInputPanel.Controls.Add(agentInputTextBox);
            //
            // agentInputTextBox
            //
            agentInputTextBox.Dock = DockStyle.Fill;
            agentInputTextBox.Name = "agentInputTextBox";
            agentInputTextBox.PlaceholderText = "输入消息...";
            agentInputTextBox.TabIndex = 0;
            agentInputTextBox.KeyDown += OnAgentInputKeyDown;
            //
            // agentSendButton
            //
            agentSendButton.Dock = DockStyle.Right;
            agentSendButton.Name = "agentSendButton";
            agentSendButton.TabIndex = 1;
            agentSendButton.Text = "发送";
            agentSendButton.UseVisualStyleBackColor = true;
            agentSendButton.Width = 70;
            agentSendButton.Click += OnSendToAgent;
            //
            // executionLogListBox
            //
            executionLogListBox.Dock = DockStyle.Fill;
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
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer splitContainer;
        private Button reloadButton;
        private Button undoButton;
        private Button redoButton;
        private Button saveButton;
        private Button selectButton;
        private Label permissionModeLabel;
        private ComboBox permissionModePicker;
        private ListBox agentLogListBox;
        private Panel agentInputPanel;
        private TextBox agentInputTextBox;
        private Button agentSendButton;
        private ListBox executionLogListBox;
        private Views.Workflow.WorkflowCanvas workflowSurfaceControl;
    }
}
