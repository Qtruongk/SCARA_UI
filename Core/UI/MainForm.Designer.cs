namespace Test_1.UI
{
    partial class MainForm
    {
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
            if (disposing) ShutdownDigitalTwin();
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.panelHeader = new System.Windows.Forms.Panel();
            this.lblSubtitle = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.panelBody = new System.Windows.Forms.Panel();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.lblLog = new System.Windows.Forms.Label();
            this.panelMove = new System.Windows.Forms.Panel();
            this.lblMoveTitle = new System.Windows.Forms.Label();
            this.lblPointA = new System.Windows.Forms.Label();
            this.lblPointB = new System.Windows.Forms.Label();
            this.lblXA = new System.Windows.Forms.Label();
            this.lblYA = new System.Windows.Forms.Label();
            this.lblXB = new System.Windows.Forms.Label();
            this.lblYB = new System.Windows.Forms.Label();
            this.txtXA = new System.Windows.Forms.TextBox();
            this.txtYA = new System.Windows.Forms.TextBox();
            this.txtXB = new System.Windows.Forms.TextBox();
            this.txtYB = new System.Windows.Forms.TextBox();
            this.lblSpeed = new System.Windows.Forms.Label();
            this.txtSpeed = new System.Windows.Forms.TextBox();
            this.lblCycles = new System.Windows.Forms.Label();
            this.txtCycles = new System.Windows.Forms.TextBox();
            this.btnMoveTest = new System.Windows.Forms.Button();
            this.btnMoveStop = new System.Windows.Forms.Button();
            this.btnReadPos = new System.Windows.Forms.Button();
            this.lblCurrentPos = new System.Windows.Forms.Label();
            this.panelButtons = new System.Windows.Forms.Panel();
            this.btnConnect = new System.Windows.Forms.Button();
            this.btnServoOn = new System.Windows.Forms.Button();
            this.btnServoOff = new System.Windows.Forms.Button();
            this.btnWatchdog = new System.Windows.Forms.Button();
            this.panelStatus = new System.Windows.Forms.Panel();
            this.lblConnectionStatus = new System.Windows.Forms.Label();
            this.lblServoStatus = new System.Windows.Forms.Label();
            this.lblWatchdogStatus = new System.Windows.Forms.Label();
            this.panelHeader.SuspendLayout();
            this.panelBody.SuspendLayout();
            this.panelMove.SuspendLayout();
            this.panelButtons.SuspendLayout();
            this.panelStatus.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelHeader
            // 
            this.panelHeader.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(20)))), ((int)(((byte)(20)))), ((int)(((byte)(35)))));
            this.panelHeader.Controls.Add(this.lblSubtitle);
            this.panelHeader.Controls.Add(this.lblTitle);
            this.panelHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelHeader.Location = new System.Drawing.Point(0, 0);
            this.panelHeader.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.panelHeader.Name = "panelHeader";
            this.panelHeader.Size = new System.Drawing.Size(1040, 98);
            this.panelHeader.TabIndex = 0;
            this.panelHeader.Paint += new System.Windows.Forms.PaintEventHandler(this.panelHeader_Paint);
            // 
            // lblSubtitle
            // 
            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSubtitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(140)))), ((int)(((byte)(160)))), ((int)(((byte)(200)))));
            this.lblSubtitle.Location = new System.Drawing.Point(31, 62);
            this.lblSubtitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblSubtitle.Name = "lblSubtitle";
            this.lblSubtitle.Size = new System.Drawing.Size(187, 20);
            this.lblSubtitle.TabIndex = 1;
            this.lblSubtitle.Text = "Author: Tran Quang Truong";
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI Semibold", 18F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Location = new System.Drawing.Point(27, 15);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(333, 41);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "⚙  V1.0 Robot Control";
            // 
            // panelBody
            // 
            this.panelBody.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(25)))), ((int)(((byte)(25)))), ((int)(((byte)(42)))));
            this.panelBody.Controls.Add(this.txtLog);
            this.panelBody.Controls.Add(this.lblLog);
            this.panelBody.Controls.Add(this.panelMove);
            this.panelBody.Controls.Add(this.panelButtons);
            this.panelBody.Controls.Add(this.panelStatus);
            this.panelBody.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelBody.Location = new System.Drawing.Point(0, 98);
            this.panelBody.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.panelBody.Name = "panelBody";
            this.panelBody.Padding = new System.Windows.Forms.Padding(27, 18, 27, 18);
            this.panelBody.Size = new System.Drawing.Size(1040, 702);
            this.panelBody.TabIndex = 1;
            // 
            // txtLog
            // 
            this.txtLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(18)))), ((int)(((byte)(30)))));
            this.txtLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(130)))), ((int)(((byte)(200)))), ((int)(((byte)(160)))));
            this.txtLog.Location = new System.Drawing.Point(27, 430);
            this.txtLog.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(987, 246);
            this.txtLog.TabIndex = 3;
            // 
            // lblLog
            // 
            this.lblLog.AutoSize = true;
            this.lblLog.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblLog.Location = new System.Drawing.Point(27, 400);
            this.lblLog.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblLog.Name = "lblLog";
            this.lblLog.Size = new System.Drawing.Size(101, 23);
            this.lblLog.TabIndex = 2;
            this.lblLog.Text = "📋  Activity";
            // 
            // panelButtons
            // 
            this.panelButtons.Controls.Add(this.btnConnect);
            this.panelButtons.Controls.Add(this.btnServoOn);
            this.panelButtons.Controls.Add(this.btnServoOff);
            this.panelButtons.Controls.Add(this.btnWatchdog);
            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelButtons.Location = new System.Drawing.Point(27, 80);
            this.panelButtons.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Padding = new System.Windows.Forms.Padding(0, 18, 0, 0);
            this.panelButtons.Size = new System.Drawing.Size(986, 117);
            this.panelButtons.TabIndex = 1;
            // 
            // btnConnect
            // 
            this.btnConnect.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(120)))), ((int)(((byte)(220)))));
            this.btnConnect.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnConnect.FlatAppearance.BorderSize = 0;
            this.btnConnect.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(100)))), ((int)(((byte)(190)))));
            this.btnConnect.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(55)))), ((int)(((byte)(140)))), ((int)(((byte)(240)))));
            this.btnConnect.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnConnect.Font = new System.Drawing.Font("Segoe UI Semibold", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnConnect.ForeColor = System.Drawing.Color.White;
            this.btnConnect.Location = new System.Drawing.Point(0, 18);
            this.btnConnect.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(227, 74);
            this.btnConnect.TabIndex = 0;
            this.btnConnect.Text = "⚡  Connect";
            this.btnConnect.UseVisualStyleBackColor = false;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
            // 
            // btnServoOn
            // 
            this.btnServoOn.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(45)))), ((int)(((byte)(60)))));
            this.btnServoOn.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnServoOn.Enabled = false;
            this.btnServoOn.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.btnServoOn.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(20)))), ((int)(((byte)(110)))), ((int)(((byte)(60)))));
            this.btnServoOn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(140)))), ((int)(((byte)(80)))));
            this.btnServoOn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnServoOn.Font = new System.Drawing.Font("Segoe UI Semibold", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnServoOn.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(110)))), ((int)(((byte)(130)))));
            this.btnServoOn.Location = new System.Drawing.Point(253, 18);
            this.btnServoOn.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnServoOn.Name = "btnServoOn";
            this.btnServoOn.Size = new System.Drawing.Size(227, 74);
            this.btnServoOn.TabIndex = 1;
            this.btnServoOn.Text = "▶  Servo ON";
            this.btnServoOn.UseVisualStyleBackColor = false;
            this.btnServoOn.Click += new System.EventHandler(this.btnServoOn_Click);
            // 
            // btnServoOff
            // 
            this.btnServoOff.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(45)))), ((int)(((byte)(60)))));
            this.btnServoOff.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnServoOff.Enabled = false;
            this.btnServoOff.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.btnServoOff.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(140)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.btnServoOff.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(180)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.btnServoOff.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnServoOff.Font = new System.Drawing.Font("Segoe UI Semibold", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnServoOff.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(110)))), ((int)(((byte)(130)))));
            this.btnServoOff.Location = new System.Drawing.Point(507, 18);
            this.btnServoOff.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnServoOff.Name = "btnServoOff";
            this.btnServoOff.Size = new System.Drawing.Size(227, 74);
            this.btnServoOff.TabIndex = 2;
            this.btnServoOff.Text = "■  Servo OFF";
            this.btnServoOff.UseVisualStyleBackColor = false;
            this.btnServoOff.Click += new System.EventHandler(this.btnServoOff_Click);
            // 
            // btnWatchdog
            // 
            this.btnWatchdog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(35)))), ((int)(((byte)(45)))), ((int)(((byte)(60)))));
            this.btnWatchdog.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnWatchdog.Enabled = false;
            this.btnWatchdog.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.btnWatchdog.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(90)))), ((int)(((byte)(65)))), ((int)(((byte)(170)))));
            this.btnWatchdog.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(90)))), ((int)(((byte)(200)))));
            this.btnWatchdog.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWatchdog.Font = new System.Drawing.Font("Segoe UI Semibold", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnWatchdog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(110)))), ((int)(((byte)(130)))));
            this.btnWatchdog.Location = new System.Drawing.Point(760, 18);
            this.btnWatchdog.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnWatchdog.Name = "btnWatchdog";
            this.btnWatchdog.Size = new System.Drawing.Size(227, 74);
            this.btnWatchdog.TabIndex = 3;
            this.btnWatchdog.Text = "👁  Watchdog";
            this.btnWatchdog.UseVisualStyleBackColor = false;
            this.btnWatchdog.Click += new System.EventHandler(this.btnWatchdog_Click);
            // 
            // panelStatus
            // 
            this.panelStatus.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(32)))), ((int)(((byte)(52)))));
            this.panelStatus.Controls.Add(this.lblConnectionStatus);
            this.panelStatus.Controls.Add(this.lblServoStatus);
            this.panelStatus.Controls.Add(this.lblWatchdogStatus);
            this.panelStatus.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelStatus.Location = new System.Drawing.Point(27, 18);
            this.panelStatus.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.panelStatus.Name = "panelStatus";
            this.panelStatus.Padding = new System.Windows.Forms.Padding(20, 12, 20, 12);
            this.panelStatus.Size = new System.Drawing.Size(986, 62);
            this.panelStatus.TabIndex = 0;
            this.panelStatus.Paint += new System.Windows.Forms.PaintEventHandler(this.panelStatus_Paint);
            // 
            // lblConnectionStatus
            // 
            this.lblConnectionStatus.AutoSize = true;
            this.lblConnectionStatus.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblConnectionStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(85)))), ((int)(((byte)(85)))));
            this.lblConnectionStatus.Location = new System.Drawing.Point(24, 18);
            this.lblConnectionStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblConnectionStatus.Name = "lblConnectionStatus";
            this.lblConnectionStatus.Size = new System.Drawing.Size(133, 23);
            this.lblConnectionStatus.TabIndex = 0;
            this.lblConnectionStatus.Text = "●  Disconnected";
            // 
            // lblServoStatus
            // 
            this.lblServoStatus.AutoSize = true;
            this.lblServoStatus.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblServoStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(120)))), ((int)(((byte)(140)))));
            this.lblServoStatus.Location = new System.Drawing.Point(347, 18);
            this.lblServoStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblServoStatus.Name = "lblServoStatus";
            this.lblServoStatus.Size = new System.Drawing.Size(117, 23);
            this.lblServoStatus.TabIndex = 1;
            this.lblServoStatus.Text = "○  Servo: N/A";
            // 
            // lblWatchdogStatus
            // 
            this.lblWatchdogStatus.AutoSize = true;
            this.lblWatchdogStatus.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblWatchdogStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(120)))), ((int)(((byte)(140)))));
            this.lblWatchdogStatus.Location = new System.Drawing.Point(667, 18);
            this.lblWatchdogStatus.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblWatchdogStatus.Name = "lblWatchdogStatus";
            this.lblWatchdogStatus.Size = new System.Drawing.Size(145, 23);
            this.lblWatchdogStatus.TabIndex = 2;
            this.lblWatchdogStatus.Text = "○  Watchdog: Off";
            // 
            // panelMove
            // 
            this.panelMove.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(32)))), ((int)(((byte)(52)))));
            this.panelMove.Controls.Add(this.lblMoveTitle);
            this.panelMove.Controls.Add(this.lblPointA);
            this.panelMove.Controls.Add(this.lblXA);
            this.panelMove.Controls.Add(this.txtXA);
            this.panelMove.Controls.Add(this.lblYA);
            this.panelMove.Controls.Add(this.txtYA);
            this.panelMove.Controls.Add(this.lblPointB);
            this.panelMove.Controls.Add(this.lblXB);
            this.panelMove.Controls.Add(this.txtXB);
            this.panelMove.Controls.Add(this.lblYB);
            this.panelMove.Controls.Add(this.txtYB);
            this.panelMove.Controls.Add(this.lblSpeed);
            this.panelMove.Controls.Add(this.txtSpeed);
            this.panelMove.Controls.Add(this.lblCycles);
            this.panelMove.Controls.Add(this.txtCycles);
            this.panelMove.Controls.Add(this.btnMoveTest);
            this.panelMove.Controls.Add(this.btnMoveStop);
            this.panelMove.Controls.Add(this.btnReadPos);
            this.panelMove.Controls.Add(this.lblCurrentPos);
            this.panelMove.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelMove.Location = new System.Drawing.Point(27, 197);
            this.panelMove.Name = "panelMove";
            this.panelMove.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
            this.panelMove.Size = new System.Drawing.Size(986, 185);
            this.panelMove.TabIndex = 4;
            // 
            // lblMoveTitle
            // 
            this.lblMoveTitle.AutoSize = true;
            this.lblMoveTitle.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
            this.lblMoveTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblMoveTitle.Location = new System.Drawing.Point(12, 8);
            this.lblMoveTitle.Name = "lblMoveTitle";
            this.lblMoveTitle.Size = new System.Drawing.Size(200, 23);
            this.lblMoveTitle.Text = "🔄  Move Test (XY Only)";
            // 
            // lblPointA
            // 
            this.lblPointA.AutoSize = true;
            this.lblPointA.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
            this.lblPointA.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(80)))), ((int)(((byte)(220)))), ((int)(((byte)(120)))));
            this.lblPointA.Location = new System.Drawing.Point(12, 42);
            this.lblPointA.Name = "lblPointA";
            this.lblPointA.Text = "Point A:";
            // 
            // lblXA
            // 
            this.lblXA.AutoSize = true;
            this.lblXA.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblXA.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblXA.Location = new System.Drawing.Point(85, 42);
            this.lblXA.Name = "lblXA";
            this.lblXA.Text = "X:";
            // 
            // txtXA
            // 
            this.txtXA.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(18)))), ((int)(((byte)(30)))));
            this.txtXA.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtXA.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtXA.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(220)))), ((int)(((byte)(240)))));
            this.txtXA.Location = new System.Drawing.Point(110, 38);
            this.txtXA.Name = "txtXA";
            this.txtXA.Size = new System.Drawing.Size(100, 27);
            this.txtXA.Text = "200.0";
            // 
            // lblYA
            // 
            this.lblYA.AutoSize = true;
            this.lblYA.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblYA.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblYA.Location = new System.Drawing.Point(220, 42);
            this.lblYA.Name = "lblYA";
            this.lblYA.Text = "Y:";
            // 
            // txtYA
            // 
            this.txtYA.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(18)))), ((int)(((byte)(30)))));
            this.txtYA.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtYA.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtYA.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(220)))), ((int)(((byte)(240)))));
            this.txtYA.Location = new System.Drawing.Point(245, 38);
            this.txtYA.Name = "txtYA";
            this.txtYA.Size = new System.Drawing.Size(100, 27);
            this.txtYA.Text = "0.0";
            // 
            // lblPointB
            // 
            this.lblPointB.AutoSize = true;
            this.lblPointB.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
            this.lblPointB.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(170)))), ((int)(((byte)(60)))));
            this.lblPointB.Location = new System.Drawing.Point(12, 78);
            this.lblPointB.Name = "lblPointB";
            this.lblPointB.Text = "Point B:";
            // 
            // lblXB
            // 
            this.lblXB.AutoSize = true;
            this.lblXB.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblXB.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblXB.Location = new System.Drawing.Point(85, 78);
            this.lblXB.Name = "lblXB";
            this.lblXB.Text = "X:";
            // 
            // txtXB
            // 
            this.txtXB.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(18)))), ((int)(((byte)(30)))));
            this.txtXB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtXB.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtXB.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(220)))), ((int)(((byte)(240)))));
            this.txtXB.Location = new System.Drawing.Point(110, 74);
            this.txtXB.Name = "txtXB";
            this.txtXB.Size = new System.Drawing.Size(100, 27);
            this.txtXB.Text = "300.0";
            // 
            // lblYB
            // 
            this.lblYB.AutoSize = true;
            this.lblYB.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblYB.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblYB.Location = new System.Drawing.Point(220, 78);
            this.lblYB.Name = "lblYB";
            this.lblYB.Text = "Y:";
            // 
            // txtYB
            // 
            this.txtYB.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(18)))), ((int)(((byte)(30)))));
            this.txtYB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtYB.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtYB.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(220)))), ((int)(((byte)(240)))));
            this.txtYB.Location = new System.Drawing.Point(245, 74);
            this.txtYB.Name = "txtYB";
            this.txtYB.Size = new System.Drawing.Size(100, 27);
            this.txtYB.Text = "100.0";
            // 
            // lblSpeed
            // 
            this.lblSpeed.AutoSize = true;
            this.lblSpeed.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblSpeed.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblSpeed.Location = new System.Drawing.Point(380, 42);
            this.lblSpeed.Name = "lblSpeed";
            this.lblSpeed.Text = "Speed %:";
            // 
            // txtSpeed
            // 
            this.txtSpeed.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(18)))), ((int)(((byte)(30)))));
            this.txtSpeed.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtSpeed.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtSpeed.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(220)))), ((int)(((byte)(240)))));
            this.txtSpeed.Location = new System.Drawing.Point(455, 38);
            this.txtSpeed.Name = "txtSpeed";
            this.txtSpeed.Size = new System.Drawing.Size(60, 27);
            this.txtSpeed.Text = "10";
            // 
            // lblCycles
            // 
            this.lblCycles.AutoSize = true;
            this.lblCycles.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblCycles.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(160)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblCycles.Location = new System.Drawing.Point(380, 78);
            this.lblCycles.Name = "lblCycles";
            this.lblCycles.Text = "Cycles:";
            // 
            // txtCycles
            // 
            this.txtCycles.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(18)))), ((int)(((byte)(18)))), ((int)(((byte)(30)))));
            this.txtCycles.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtCycles.Font = new System.Drawing.Font("Consolas", 10F);
            this.txtCycles.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(220)))), ((int)(((byte)(240)))));
            this.txtCycles.Location = new System.Drawing.Point(455, 74);
            this.txtCycles.Name = "txtCycles";
            this.txtCycles.Size = new System.Drawing.Size(60, 27);
            this.txtCycles.Text = "3";
            // 
            // btnMoveTest
            // 
            this.btnMoveTest.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(130)))), ((int)(((byte)(76)))));
            this.btnMoveTest.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnMoveTest.FlatAppearance.BorderSize = 0;
            this.btnMoveTest.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(20)))), ((int)(((byte)(100)))), ((int)(((byte)(56)))));
            this.btnMoveTest.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(160)))), ((int)(((byte)(96)))));
            this.btnMoveTest.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMoveTest.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
            this.btnMoveTest.ForeColor = System.Drawing.Color.White;
            this.btnMoveTest.Location = new System.Drawing.Point(545, 34);
            this.btnMoveTest.Name = "btnMoveTest";
            this.btnMoveTest.Size = new System.Drawing.Size(150, 34);
            this.btnMoveTest.TabIndex = 10;
            this.btnMoveTest.Text = "▶  Move Test";
            this.btnMoveTest.UseVisualStyleBackColor = false;
            // 
            // btnMoveStop
            // 
            this.btnMoveStop.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(180)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.btnMoveStop.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnMoveStop.FlatAppearance.BorderSize = 0;
            this.btnMoveStop.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(140)))), ((int)(((byte)(35)))), ((int)(((byte)(35)))));
            this.btnMoveStop.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(200)))), ((int)(((byte)(65)))), ((int)(((byte)(65)))));
            this.btnMoveStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMoveStop.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold);
            this.btnMoveStop.ForeColor = System.Drawing.Color.White;
            this.btnMoveStop.Location = new System.Drawing.Point(545, 74);
            this.btnMoveStop.Name = "btnMoveStop";
            this.btnMoveStop.Size = new System.Drawing.Size(150, 34);
            this.btnMoveStop.TabIndex = 11;
            this.btnMoveStop.Text = "■  Stop Move";
            this.btnMoveStop.UseVisualStyleBackColor = false;
            this.btnMoveStop.Enabled = false;
            // 
            // btnReadPos
            // 
            this.btnReadPos.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(60)))), ((int)(((byte)(90)))));
            this.btnReadPos.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnReadPos.FlatAppearance.BorderSize = 0;
            this.btnReadPos.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(50)))), ((int)(((byte)(75)))));
            this.btnReadPos.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(65)))), ((int)(((byte)(80)))), ((int)(((byte)(110)))));
            this.btnReadPos.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnReadPos.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
            this.btnReadPos.ForeColor = System.Drawing.Color.White;
            this.btnReadPos.Location = new System.Drawing.Point(720, 34);
            this.btnReadPos.Name = "btnReadPos";
            this.btnReadPos.Size = new System.Drawing.Size(130, 34);
            this.btnReadPos.TabIndex = 12;
            this.btnReadPos.Text = "📍 Read Position";
            this.btnReadPos.UseVisualStyleBackColor = false;
            // 
            // lblCurrentPos
            // 
            this.lblCurrentPos.AutoSize = true;
            this.lblCurrentPos.Font = new System.Drawing.Font("Consolas", 9F);
            this.lblCurrentPos.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(130)))), ((int)(((byte)(200)))), ((int)(((byte)(160)))));
            this.lblCurrentPos.Location = new System.Drawing.Point(12, 120);
            this.lblCurrentPos.Name = "lblCurrentPos";
            this.lblCurrentPos.Text = "Current Pos: (not read)";
            // 
            // Scara
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(25)))), ((int)(((byte)(25)))), ((int)(((byte)(42)))));
            this.ClientSize = new System.Drawing.Size(1040, 800);
            this.Controls.Add(this.panelBody);
            this.Controls.Add(this.panelHeader);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "SCARA Robot Control Panel";
            this.panelHeader.ResumeLayout(false);
            this.panelHeader.PerformLayout();
            this.panelBody.ResumeLayout(false);
            this.panelBody.PerformLayout();
            this.panelMove.ResumeLayout(false);
            this.panelMove.PerformLayout();
            this.panelButtons.ResumeLayout(false);
            this.panelStatus.ResumeLayout(false);
            this.panelStatus.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panelHeader;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblSubtitle;
        private System.Windows.Forms.Panel panelBody;
        private System.Windows.Forms.Panel panelStatus;
        private System.Windows.Forms.Label lblConnectionStatus;
        private System.Windows.Forms.Label lblServoStatus;
        private System.Windows.Forms.Label lblWatchdogStatus;
        private System.Windows.Forms.Panel panelButtons;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Button btnServoOn;
        private System.Windows.Forms.Button btnServoOff;
        private System.Windows.Forms.Button btnWatchdog;
        private System.Windows.Forms.Label lblLog;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Panel panelMove;
        private System.Windows.Forms.Label lblMoveTitle;
        private System.Windows.Forms.Label lblPointA;
        private System.Windows.Forms.Label lblPointB;
        private System.Windows.Forms.Label lblXA;
        private System.Windows.Forms.Label lblYA;
        private System.Windows.Forms.Label lblXB;
        private System.Windows.Forms.Label lblYB;
        private System.Windows.Forms.TextBox txtXA;
        private System.Windows.Forms.TextBox txtYA;
        private System.Windows.Forms.TextBox txtXB;
        private System.Windows.Forms.TextBox txtYB;
        private System.Windows.Forms.Label lblSpeed;
        private System.Windows.Forms.TextBox txtSpeed;
        private System.Windows.Forms.Label lblCycles;
        private System.Windows.Forms.TextBox txtCycles;
        private System.Windows.Forms.Button btnMoveTest;
        private System.Windows.Forms.Button btnMoveStop;
        private System.Windows.Forms.Button btnReadPos;
        private System.Windows.Forms.Label lblCurrentPos;
    }
}

