using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using TsRemoteLib;
using Test_1.Services;
using Test_1.Utilities;
using Test_1.Models;

namespace Test_1.UI
{
    public partial class MainForm : Form
    {

        // Exposed controls for events
        private TrackBar tbSpeed;
        private Button btnGuideRun;
        private Button btnGuideStop;
        private Button btnStartDemo;
        private Button btnStartTrap;
        private Panel pnlMode1;
        private Panel pnlMode2;
        private Panel pnlMode3;
        private TextBox[] txtAxes = new TextBox[4];
        private TrackBar[] tbAxes = new TrackBar[4];
        private TextBox[] txtCart = new TextBox[4];
        private System.Windows.Forms.DataVisualization.Charting.Chart motionChart;
        private System.Windows.Forms.DataVisualization.Charting.Series sPos;
        private System.Windows.Forms.DataVisualization.Charting.Series sVel;
        private System.Windows.Forms.DataVisualization.Charting.Series sAcc;

        private void InitializeModernUI()
        {
            // Register paint handlers
            panelHeader.Paint += panelHeader_Paint;
            panelStatus.Paint += panelStatus_Paint;
            this.BackColor = Color.FromArgb(245, 246, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));
            this.AutoScaleMode = AutoScaleMode.None;

            // ==========================================
            // PROJECT INFORMATION
            // ==========================================
            string projAuthor = "Tran Quang Truong";
            string projCoAuthor = "Luong Hoang Long";
            string projDate = "10/07/2026";
            string projName = "Scara Robot";
            string projTarget = "TSL3000";
            string projToolVers = "v1.0";
            string projDesc = "Robot Control";
            string projComments = "None";
            // ==========================================

            // Allow resize + maximize (Designer had FixedSingle / MaximizeBox = false)
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.MinimumSize = new Size(900, 600);

            // Fit inside the working area (screen minus taskbar)
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(1280, wa.Width - 40);
            int h = Math.Min(800, wa.Height - 40);
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(w, h);
            this.Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
            this.WindowState = FormWindowState.Normal;

            // Clear old layout from main form
            this.Controls.Clear();

            Color primaryColor = Color.FromArgb(52, 71, 103);  // Calm slate navy
            Color darkText = Color.FromArgb(33, 37, 41);       // Neutral dark charcoal
            Color mutedText = Color.FromArgb(108, 117, 125);   // Neutral subtle gray
            Color lightBg = Color.White;
            Color sidebarBg = Color.FromArgb(248, 249, 250);
            Color borderColor = Color.FromArgb(226, 230, 234);
            Color canvasBg = Color.FromArgb(245, 247, 250);

            // 1. Top Header
            Panel header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = lightBg };
            header.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, header.Height - 1, header.Width, header.Height - 1);
            };
            
            Label lblTitle = new Label {
                Text = "ROBOT STUDIO",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = primaryColor,
                AutoSize = true,
                Location = new Point(160, 18)
            };
            header.Controls.Add(lblTitle);

            try
            {
                string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FPT UNIVERSITY.png");
                if (System.IO.File.Exists(logoPath))
                {
                    PictureBox picLogo = new PictureBox {
                        Image = Image.FromFile(logoPath),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Size = new Size(130, 40),
                        Location = new Point(20, 10)
                    };
                    header.Controls.Add(picLogo);
                }
            }
            catch { }

            // Status indicators in header
            Label lblDatePrefix = new Label { Text = "DATE:", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = mutedText, AutoSize = true, Location = new Point(header.Width - 280, 10), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Label lblDateValue = new Label { Text = DateTime.Now.ToString("ddd, dd-MM-yyyy, HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).ToUpper(), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = darkText, AutoSize = true, Location = new Point(header.Width - 240, 10), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            
            Label lblAlarmPrefix = new Label { Text = "ALARM:", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = mutedText, AutoSize = true, Location = new Point(header.Width - 280, 30), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Label lblAlarmValue = new Label { Text = "No alarm !", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(46, 125, 50), AutoSize = true, Location = new Point(header.Width - 230, 30), Anchor = AnchorStyles.Right | AnchorStyles.Top };

            header.Controls.Add(lblDatePrefix);
            header.Controls.Add(lblDateValue);
            header.Controls.Add(lblAlarmPrefix);
            header.Controls.Add(lblAlarmValue);

            // Timer for real-time clock
            if (components == null) components = new System.ComponentModel.Container();
            System.Windows.Forms.Timer clockTimer = new System.Windows.Forms.Timer(components);
            clockTimer.Interval = 1000;
            clockTimer.Tick += (s, e) => {
                if (lblDateValue.IsDisposed) return;
                lblDateValue.Text = DateTime.Now.ToString("ddd, dd-MM-yyyy, HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).ToUpper();
            };
            clockTimer.Start();

            // 2. Left Sidebar
            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = sidebarBg };
            sidebar.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            };

            string[] navItems = { "🏠 Home", "🎮 Guide key", "🎨 Shape Drawing", "📷 Vision Camera" };
            Button[] navBtns = new Button[navItems.Length];
            int navY = 20;
            for (int i = 0; i < navItems.Length; i++)
            {
                Button btn = new Button {
                    Text = "  " + navItems[i],
                    FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Width = 220,
                    Height = 45,
                    Location = new Point(0, navY),
                    Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(80, 90, 105),
                    BackColor = sidebarBg,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 242, 246);
                btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(228, 233, 240);
                sidebar.Controls.Add(btn);
                navBtns[i] = btn;
                navY += 45;
            }

            Panel activeIndicator = new Panel { BackColor = primaryColor, Width = 4, Height = 45, Location = new Point(0, 20) };
            sidebar.Controls.Add(activeIndicator);
            activeIndicator.BringToFront();

            Action<int> updateNavStyles = (selIdx) => {
                for (int i = 0; i < navBtns.Length; i++) {
                    if (i == selIdx) {
                        navBtns[i].ForeColor = primaryColor;
                        navBtns[i].BackColor = Color.FromArgb(238, 242, 246);
                        navBtns[i].Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                    } else {
                        navBtns[i].ForeColor = Color.FromArgb(90, 100, 115);
                        navBtns[i].BackColor = sidebarBg;
                        navBtns[i].Font = new Font("Segoe UI", 10F, FontStyle.Regular);
                    }
                }
                activeIndicator.Location = new Point(0, 20 + selIdx * 45);
            };

            // Project Info Panel in Sidebar
            Panel pnlProjInfo = new Panel { Dock = DockStyle.Bottom, Height = 250, BackColor = sidebarBg, Padding = new Padding(10) };
            pnlProjInfo.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, pnlProjInfo.Width, 0);
            };
            Label lblProjTitle = new Label { Text = "PROJECT INFO", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = mutedText, AutoSize = true, Location = new Point(15, 15) };
            pnlProjInfo.Controls.Add(lblProjTitle);

            string[] infoLabels = { "Author:", "", "Create Date:", "Project Name:", "Target Devices:", "Tool Versions:", "Description:", "Comments:" };
            string[] infoValues = { projAuthor, projCoAuthor, projDate, projName, projTarget, projToolVers, projDesc, projComments };
            
            for (int i = 0; i < infoLabels.Length; i++)
            {
                Label lblKey = new Label { Text = infoLabels[i], Font = new Font("Segoe UI", 8F, FontStyle.Regular), ForeColor = mutedText, AutoSize = true, Location = new Point(15, 45 + i*25) };
                Label lblVal = new Label { Text = infoValues[i], Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = darkText, AutoSize = true, Location = new Point(100, 45 + i*25) };
                pnlProjInfo.Controls.Add(lblKey);
                pnlProjInfo.Controls.Add(lblVal);
            }
            sidebar.Controls.Add(pnlProjInfo);

            // 3. Right Status Panel
            Panel rightPanel = new Panel { Dock = DockStyle.Right, Width = 260, BackColor = lightBg };
            rightPanel.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, 0, rightPanel.Height);
            };
            Label lblStatusTitle = new Label { Text = "ROBOT STATUS", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, AutoSize = true, Location = new Point(20, 20) };
            rightPanel.Controls.Add(lblStatusTitle);
            
            lblConnectionStatus.Location = new Point(20, 55);
            lblConnectionStatus.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            lblServoStatus.Location = new Point(20, 85);
            lblServoStatus.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            lblWatchdogStatus.Location = new Point(20, 115);
            lblWatchdogStatus.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            
            rightPanel.Controls.Add(lblConnectionStatus);
            rightPanel.Controls.Add(lblServoStatus);
            rightPanel.Controls.Add(lblWatchdogStatus);

            // Hide unused pos controls
            lblCurrentPos.Visible = false;
            btnReadPos.Visible = false;

            // Responsive layout inside right panel
            rightPanel.Resize += (s, e) => {
                int btnW = Math.Max(120, rightPanel.Width - 40);
                int halfW = Math.Max(50, (btnW - 10) / 2);
                btnConnect.Size = new Size(btnW, 38);
                btnConnect.Location = new Point(20, rightPanel.Height - 165);
                btnServoOn.Size = new Size(halfW, 36);
                btnServoOn.Location = new Point(20, rightPanel.Height - 118);
                btnServoOff.Size = new Size(halfW, 36);
                btnServoOff.Location = new Point(20 + halfW + 10, rightPanel.Height - 118);
                btnWatchdog.Size = new Size(btnW, 36);
                btnWatchdog.Location = new Point(20, rightPanel.Height - 72);
            };

            btnConnect.FlatStyle = FlatStyle.Flat; btnConnect.FlatAppearance.BorderSize = 0; btnConnect.Cursor = Cursors.Hand;
            btnConnect.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            btnServoOn.FlatStyle = FlatStyle.Flat; btnServoOn.FlatAppearance.BorderSize = 0; btnServoOn.Cursor = Cursors.Hand;
            btnServoOn.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            btnServoOff.FlatStyle = FlatStyle.Flat; btnServoOff.FlatAppearance.BorderSize = 0; btnServoOff.Cursor = Cursors.Hand;
            btnServoOff.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            btnWatchdog.FlatStyle = FlatStyle.Flat; btnWatchdog.FlatAppearance.BorderSize = 0; btnWatchdog.Cursor = Cursors.Hand;
            btnWatchdog.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            rightPanel.Controls.Add(btnConnect);
            rightPanel.Controls.Add(btnServoOn);
            rightPanel.Controls.Add(btnServoOff);
            rightPanel.Controls.Add(btnWatchdog);

            // 4. Draggable Splitter for Right Panel
            Panel splitterRight = new Panel {
                Dock = DockStyle.Right,
                Width = 6,
                Cursor = Cursors.VSplit,
                BackColor = Color.FromArgb(235, 238, 242)
            };
            bool isDraggingRight = false;
            int startMouseX = 0;
            int startRightW = 0;

            splitterRight.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    isDraggingRight = true;
                    startMouseX = Cursor.Position.X;
                    startRightW = rightPanel.Width;
                    splitterRight.Capture = true;
                }
            };
            splitterRight.MouseMove += (s, e) => {
                if (isDraggingRight) {
                    int delta = startMouseX - Cursor.Position.X;
                    rightPanel.Width = Math.Max(180, Math.Min(450, startRightW + delta));
                }
            };
            splitterRight.MouseUp += (s, e) => {
                isDraggingRight = false;
                splitterRight.Capture = false;
            };
            splitterRight.DoubleClick += (s, e) => {
                rightPanel.Width = 260;
            };
            splitterRight.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, 0, splitterRight.Height);
                int cy = splitterRight.Height / 2;
                using (Pen p = new Pen(Color.FromArgb(170, 175, 185), 2)) {
                    e.Graphics.DrawLine(p, 2, cy - 12, 2, cy + 12);
                }
            };

            // 5. Bottom Panel (System Log)
            Panel bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 120, BackColor = lightBg };
            bottomPanel.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, bottomPanel.Width, 0);
            };
            Label lblConsoleTitle = new Label { Text = "SYSTEM LOG", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = mutedText, AutoSize = true, Location = new Point(20, 8) };
            bottomPanel.Controls.Add(lblConsoleTitle);

            txtLog.Location = new Point(20, 30);
            txtLog.Height = 80;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.BackColor = Color.FromArgb(250, 250, 250);
            txtLog.ForeColor = Color.FromArgb(50, 50, 50);
            txtLog.Font = new Font("Consolas", 9F);
            bottomPanel.Resize += (s, e) => {
                txtLog.Width = Math.Max(1, bottomPanel.Width - 40);
                txtLog.Height = Math.Max(1, bottomPanel.Height - 40);
            };
            bottomPanel.Controls.Add(txtLog);

            // 6. Draggable Splitter for Bottom Panel
            Panel splitterBottom = new Panel {
                Dock = DockStyle.Bottom,
                Height = 6,
                Cursor = Cursors.HSplit,
                BackColor = Color.FromArgb(235, 238, 242)
            };
            bool isDraggingBottom = false;
            int startMouseY = 0;
            int startBottomH = 0;

            splitterBottom.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    isDraggingBottom = true;
                    startMouseY = Cursor.Position.Y;
                    startBottomH = bottomPanel.Height;
                    splitterBottom.Capture = true;
                }
            };
            splitterBottom.MouseMove += (s, e) => {
                if (isDraggingBottom) {
                    int delta = startMouseY - Cursor.Position.Y;
                    bottomPanel.Height = Math.Max(35, Math.Min(450, startBottomH + delta));
                }
            };
            splitterBottom.MouseUp += (s, e) => {
                isDraggingBottom = false;
                splitterBottom.Capture = false;
            };
            splitterBottom.DoubleClick += (s, e) => {
                bottomPanel.Height = (bottomPanel.Height > 40) ? 35 : 120;
            };
            splitterBottom.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, splitterBottom.Width, 0);
                int cx = splitterBottom.Width / 2;
                using (Pen p = new Pen(Color.FromArgb(170, 175, 185), 2)) {
                    e.Graphics.DrawLine(p, cx - 12, 2, cx + 12, 2);
                }
            };

            // 7. Workspace Container (Holds all tabs, fills remaining area)
            Panel workspaceContainer = new Panel {
                Dock = DockStyle.Fill,
                BackColor = canvasBg
            };

            // Home Workspace (Digital Twin)
            Panel workspace = new Panel {
                Dock = DockStyle.Fill, 
                BackColor = canvasBg,
                Padding = new Padding(12)
            };
            workspace.Name = "workspaceHome";
            workspace.Controls.Add(InitializeDigitalTwin());

            // Guide Key Workspace
            Panel workspaceGuide = new Panel {
                Dock = DockStyle.Fill,
                BackColor = canvasBg,
                Padding = new Padding(12),
                Visible = false,
                AutoScroll = true
            };
            
            // Mode selector buttons at top
            Button btnMode1 = new Button { Text = "TextBox", Location = new Point(12, 10), Size = new Size(110, 32), FlatStyle = FlatStyle.Flat, BackColor = primaryColor, ForeColor = Color.White, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnMode1.FlatAppearance.BorderSize = 0;
            Button btnMode2 = new Button { Text = "Slider", Location = new Point(128, 10), Size = new Size(110, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = darkText, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnMode2.FlatAppearance.BorderColor = borderColor;
            Button btnMode3 = new Button { Text = "Cartesian", Location = new Point(244, 10), Size = new Size(120, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = darkText, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnMode3.FlatAppearance.BorderColor = borderColor;

            workspaceGuide.Controls.Add(btnMode1);
            workspaceGuide.Controls.Add(btnMode2);
            workspaceGuide.Controls.Add(btnMode3);

            // Left Card: Target Position Inputs
            Panel pnlTarget = new Panel { BackColor = Color.White, AutoScroll = true };
            pnlTarget.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlTarget.Width - 1, pnlTarget.Height - 1); };
            Label lblTargetTitle = new Label { Text = "🎯 Vị trí Đích (Target Position)", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = primaryColor, Location = new Point(14, 12), AutoSize = true };
            pnlTarget.Controls.Add(lblTargetTitle);

            // Button to read current coordinates directly into target fields
            Button btnReadToTarget = new Button {
                Text = "📥 ĐỌC TỌA ĐỘ HIỆN TẠI VÀO ĐÍCH",
                Location = new Point(14, 38),
                Size = new Size(260, 32),
                BackColor = Color.FromArgb(70, 95, 133),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnReadToTarget.FlatAppearance.BorderSize = 0;
            pnlTarget.Controls.Add(btnReadToTarget);

            Panel pnlMode1 = new Panel { Location = new Point(12, 78), Visible = true, BackColor = Color.White };
            Panel pnlMode2 = new Panel { Location = new Point(12, 78), Visible = false, BackColor = Color.White };
            Panel pnlMode3 = new Panel { Location = new Point(12, 78), Visible = false, BackColor = Color.White };

            pnlTarget.Controls.Add(pnlMode1);
            pnlTarget.Controls.Add(pnlMode2);
            pnlTarget.Controls.Add(pnlMode3);

            TextBox[] txtAxes = new TextBox[4];
            Label[] unitsAxes = new Label[4];
            TrackBar[] tbAxes = new TrackBar[4];
            Label[] lblAxesVal = new Label[4];
            TextBox[] txtCart = new TextBox[4];

            // Build Mode 1 & 2 controls
            for (int i = 1; i <= 4; i++) {
                int y = 6 + (i - 1) * 44;
                
                // Mode 1: TextBox
                Label lbl1 = new Label { Text = "Axis " + i + ":", Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = darkText, Location = new Point(10, y + 4), AutoSize = true };
                TextBox txt = new TextBox { Text = "0.0", Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.White, ForeColor = darkText, Location = new Point(85, y), Size = new Size(130, 26), TextAlign = HorizontalAlignment.Right, BorderStyle = BorderStyle.FixedSingle };
                txt.Enter += (s, e) => txt.SelectAll();
                txt.Click += (s, e) => txt.SelectAll();
                Label unit1 = new Label { Text = (i == 3) ? "mm" : "deg", Font = new Font("Segoe UI", 9F), ForeColor = mutedText, Location = new Point(225, y + 4), AutoSize = true };
                txtAxes[i - 1] = txt;
                unitsAxes[i - 1] = unit1;
                pnlMode1.Controls.Add(lbl1); pnlMode1.Controls.Add(txt); pnlMode1.Controls.Add(unit1);
                
                // Mode 2: Slider
                Label lbl2 = new Label { Text = "Axis " + i + ":", Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = darkText, Location = new Point(6, y + 4), AutoSize = true };
                TrackBar tb = new TrackBar { Minimum = -180, Maximum = 180, Value = 0, Location = new Point(68, y), Size = new Size(160, 40), TickStyle = TickStyle.None };
                if (i == 3) { tb.Minimum = 0; tb.Maximum = 150; }
                Label val2 = new Label { Text = "0.0", Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = darkText, Location = new Point(235, y + 4), AutoSize = true };
                
                int axisNo = i;
                tb.Scroll += (s, e) => { val2.Text = tb.Value.ToString() + ".0"; };
                tbAxes[i - 1] = tb;
                lblAxesVal[i - 1] = val2;
                pnlMode2.Controls.Add(lbl2); pnlMode2.Controls.Add(tb); pnlMode2.Controls.Add(val2);
            }

            // Build Mode 3 controls (Cartesian)
            string[] cartLabels = { "X (mm):", "Y (mm):", "Z (mm):", "C (deg):" };
            for (int i = 0; i < 4; i++) {
                int y = 6 + i * 44;
                Label lbl = new Label { Text = cartLabels[i], Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = darkText, Location = new Point(10, y + 4), AutoSize = true };
                TextBox txt = new TextBox { Text = "0.0", Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.White, ForeColor = darkText, Location = new Point(85, y), Size = new Size(130, 26), TextAlign = HorizontalAlignment.Right, BorderStyle = BorderStyle.FixedSingle };
                txt.Enter += (s, e) => txt.SelectAll();
                txt.Click += (s, e) => txt.SelectAll();
                txtCart[i] = txt;
                pnlMode3.Controls.Add(lbl); pnlMode3.Controls.Add(txt);
            }

            Action<int> setGuideMode = (mode) => {
                btnMode1.BackColor = (mode == 1) ? primaryColor : Color.White;
                btnMode1.ForeColor = (mode == 1) ? Color.White : darkText;
                btnMode1.FlatAppearance.BorderSize = (mode == 1) ? 0 : 1;

                btnMode2.BackColor = (mode == 2) ? primaryColor : Color.White;
                btnMode2.ForeColor = (mode == 2) ? Color.White : darkText;
                btnMode2.FlatAppearance.BorderSize = (mode == 2) ? 0 : 1;

                btnMode3.BackColor = (mode == 3) ? primaryColor : Color.White;
                btnMode3.ForeColor = (mode == 3) ? Color.White : darkText;
                btnMode3.FlatAppearance.BorderSize = (mode == 3) ? 0 : 1;

                pnlMode1.Visible = (mode == 1);
                pnlMode2.Visible = (mode == 2);
                pnlMode3.Visible = (mode == 3);
            };

            btnMode1.Click += (s, e) => setGuideMode(1);
            btnMode2.Click += (s, e) => setGuideMode(2);
            btnMode3.Click += (s, e) => setGuideMode(3);

            // Right Card: Current Feedback & Motion Execution
            Action<bool> copyCurrentPosToTarget = (showFeedbackIfNull) => {
                var pos = _robotService.GetCurrentPosition();
                if (pos != null) {
                    // Populate Cartesian coordinates
                    txtCart[0].Text = pos.WorldPosition.X.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    txtCart[1].Text = pos.WorldPosition.Y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    txtCart[2].Text = pos.WorldPosition.Z.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    txtCart[3].Text = pos.WorldPosition.C.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

                    // Populate Joint coordinates
                    txtAxes[0].Text = pos.JointPosition.J1.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    txtAxes[1].Text = pos.JointPosition.J2.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    txtAxes[2].Text = pos.JointPosition.J3.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    txtAxes[3].Text = pos.JointPosition.J4.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

                    // Populate Joint sliders
                    int v1 = (int)Math.Round(pos.JointPosition.J1);
                    int v2 = (int)Math.Round(pos.JointPosition.J2);
                    int v3 = (int)Math.Round(pos.JointPosition.J3);
                    int v4 = (int)Math.Round(pos.JointPosition.J4);
                    tbAxes[0].Value = Math.Max(tbAxes[0].Minimum, Math.Min(tbAxes[0].Maximum, v1));
                    tbAxes[1].Value = Math.Max(tbAxes[1].Minimum, Math.Min(tbAxes[1].Maximum, v2));
                    tbAxes[2].Value = Math.Max(tbAxes[2].Minimum, Math.Min(tbAxes[2].Maximum, v3));
                    tbAxes[3].Value = Math.Max(tbAxes[3].Minimum, Math.Min(tbAxes[3].Maximum, v4));
                    lblAxesVal[0].Text = tbAxes[0].Value + ".0";
                    lblAxesVal[1].Text = tbAxes[1].Value + ".0";
                    lblAxesVal[2].Text = tbAxes[2].Value + ".0";
                    lblAxesVal[3].Text = tbAxes[3].Value + ".0";

                    _logger.Log($"[Guide] Đã đọc tọa độ robot vào ô đích: X={pos.WorldPosition.X:F1}, Y={pos.WorldPosition.Y:F1}, Z={pos.WorldPosition.Z:F1}, C={pos.WorldPosition.C:F1}");
                } else {
                    if (showFeedbackIfNull) {
                        MessageBox.Show("Chưa nhận được dữ liệu từ Robot. Vui lòng kiểm tra kết nối Robot hoặc bật Servo.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };

            btnReadToTarget.Click += (s, e) => copyCurrentPosToTarget(true);

            // Divider line
            Panel pnlDiv = new Panel { Location = new Point(14, 258), Size = new Size(330, 1), BackColor = borderColor };
            pnlTarget.Controls.Add(pnlDiv);

            // Speed Control
            Label lblSpeed = new Label { Text = "Tốc độ (Speed):", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = darkText, Location = new Point(14, 273), AutoSize = true };
            TrackBar tbSpeed = new TrackBar { Minimum = 1, Maximum = 100, Value = 10, Location = new Point(115, 268), Size = new Size(180, 40), TickStyle = TickStyle.None };
            Label valSpeed = new Label { Text = "10 %", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = primaryColor, Location = new Point(300, 273), AutoSize = true };
            tbSpeed.Scroll += (s, e) => { valSpeed.Text = tbSpeed.Value.ToString() + " %"; };

            pnlTarget.Controls.Add(lblSpeed);
            pnlTarget.Controls.Add(tbSpeed);
            pnlTarget.Controls.Add(valSpeed);

            // Execution Buttons: RUN & STOP inside pnlTarget
            Button btnGuideRun = new Button { Text = "► RUN (CHẠY)", Location = new Point(14, 312), Size = new Size(170, 38), BackColor = Color.FromArgb(46, 125, 50), ForeColor = Color.White, Font = new Font("Segoe UI", 11F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnGuideRun.FlatAppearance.BorderSize = 0;

            Button btnGuideStop = new Button { Text = "⏹ STOP (DỪNG)", Location = new Point(194, 312), Size = new Size(130, 38), BackColor = Color.FromArgb(198, 40, 40), ForeColor = Color.White, Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnGuideStop.FlatAppearance.BorderSize = 0;

            pnlTarget.Controls.Add(btnGuideRun);
            pnlTarget.Controls.Add(btnGuideStop);

            workspaceGuide.Controls.Add(pnlTarget);

            // Responsive Layout Manager for Guide Key
            Action layoutGuide = () => {
                int pad = 12;
                int topY = 50;
                int availW = workspaceGuide.ClientSize.Width - pad * 2;
                int availH = workspaceGuide.ClientSize.Height - topY - pad;

                pnlTarget.Location = new Point(pad, topY);
                pnlTarget.Size = new Size(Math.Max(320, availW), Math.Max(380, availH));
            };
            workspaceGuide.Resize += (s, e) => layoutGuide();

            // Inner responsive layout for pnlTarget
            pnlTarget.Resize += (s, e) => {
                int innerW = pnlTarget.ClientSize.Width - 28;
                btnReadToTarget.Size = new Size(Math.Max(200, Math.Min(340, innerW)), 32);

                int modeH = 175;
                pnlMode1.Size = new Size(innerW, modeH);
                pnlMode2.Size = new Size(innerW, modeH);
                pnlMode3.Size = new Size(innerW, modeH);

                int boxW = Math.Max(100, Math.Min(180, innerW - 130));
                for (int i = 0; i < 4; i++) {
                    if (txtCart[i] != null) txtCart[i].Width = boxW;
                    if (txtAxes[i] != null) {
                        txtAxes[i].Width = boxW;
                        if (unitsAxes[i] != null) unitsAxes[i].Location = new Point(txtAxes[i].Right + 8, txtAxes[i].Top + 4);
                    }
                    if (tbAxes[i] != null) {
                        tbAxes[i].Width = Math.Max(70, Math.Min(300, innerW - 130));
                        if (lblAxesVal[i] != null) {
                            lblAxesVal[i].Location = new Point(tbAxes[i].Right + 5, tbAxes[i].Top + 4);
                        }
                    }
                }

                pnlDiv.Location = new Point(14, 258);
                pnlDiv.Size = new Size(Math.Max(280, innerW), 1);

                lblSpeed.Location = new Point(14, 273);
                tbSpeed.Location = new Point(115, 268);
                tbSpeed.Size = new Size(Math.Max(80, Math.Min(220, innerW - 180)), 40);
                valSpeed.Location = new Point(tbSpeed.Right + 5, 273);

                int runW = Math.Max(130, Math.Min(200, (int)(innerW * 0.50)));
                int stopW = Math.Max(110, Math.Min(160, (int)(innerW * 0.38)));
                btnGuideRun.Location = new Point(14, 312);
                btnGuideRun.Size = new Size(runW, 38);
                btnGuideStop.Location = new Point(14 + runW + 12, 312);
                btnGuideStop.Size = new Size(stopW, 38);
            };

            // Logic for Run
            btnGuideRun.Click += async (s, e) => {
                if (_connectionChangePending || _statusCommandPending) return;
                if (!_robotService.State.IsConnected) {
                    _logger.Log("[Guide] Cannot run: Robot is not connected.");
                    MessageBox.Show("Robot chưa kết nối. Vui lòng bấm Connect trước.", "Chưa kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!_robotService.State.IsServoOn) {
                    _logger.Log("[Guide] Cannot run: Servo is OFF. Please turn Servo ON.");
                    MessageBox.Show("Servo đang tắt. Vui lòng bật Servo ON trước khi bấm RUN.", "Servo đang tắt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try {
                    int speed = tbSpeed.Value;
                    btnGuideRun.Enabled = false;
                    
                    if (pnlMode1.Visible || pnlMode2.Visible) {
                        _logger.Log($"[Guide] RUN Joint bắt đầu ở tốc độ {speed}%...");
                        bool isMode1 = pnlMode1.Visible;
                        double[] targets = new double[4];
                        for (int i = 0; i < 4; i++) {
                            if (isMode1) {
                                double.TryParse(txtAxes[i].Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out targets[i]);
                            } else {
                                targets[i] = tbAxes[i].Value;
                            }
                        }
                        
                        await Task.Run(() => {
                            _robotService.EnsureWatchdogRunning();
                            _robotService.SetSpeed(speed);
                            for (int i = 0; i < 4; i++) {
                                _robotService.MoveJointAxis(i + 1, targets[i]);
                            }
                        });
                    } else if (pnlMode3.Visible) {
                        double.TryParse(txtCart[0].Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double x);
                        double.TryParse(txtCart[1].Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double y);
                        double.TryParse(txtCart[2].Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double z);
                        double.TryParse(txtCart[3].Text.Trim().Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double c);
                        
                        _logger.Log($"[Guide] RUN Cartesian bắt đầu: X={x:F1}, Y={y:F1}, Z={z:F1}, C={c:F1} ở tốc độ {speed}%...");
                        
                        await Task.Run(() => {
                            _robotService.EnsureWatchdogRunning();
                            _robotService.SetSpeed(speed);
                            _robotService.MoveCartesian(x, y, z, c);
                        });
                    }
                } catch (Exception ex) {
                    _logger.Log("[Guide] RUN Error: " + ex.Message);
                } finally {
                    btnGuideRun.Enabled = true;
                }
            };

            // Logic for Stop
            btnGuideStop.Click += async (s, e) => {
                await Task.Run(() => _robotService.StopMotion());
                _logger.Log("[Guide] Đã gửi lệnh STOP dừng chuyển động robot.");
            };

            // --- Shape Drawing Workspace ---
            Panel workspaceShapes = new Panel { Dock = DockStyle.Fill, BackColor = canvasBg, Padding = new Padding(16), Visible = false, AutoScroll = true };
            
            Label lblShapesTitle = new Label { Text = "Shape Drawing", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = primaryColor, AutoSize = true, Location = new Point(16, 16) };
            
            Panel pnlSquare = new Panel { Location = new Point(16, 50), Size = new Size(290, 140), BackColor = Color.White };
            pnlSquare.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlSquare.Width - 1, pnlSquare.Height - 1); };
            Label lblSquare = new Label { Text = "Draw Square", Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), ForeColor = darkText, Location = new Point(12, 12), AutoSize = true };
            Label lblSide = new Label { Text = "Side Length (mm):", Font = new Font("Segoe UI", 9F), ForeColor = darkText, Location = new Point(12, 48), AutoSize = true };
            TextBox txtSide = new TextBox { Text = "100.0", Font = new Font("Segoe UI", 10F), Location = new Point(140, 44), Size = new Size(90, 25) };
            Button btnDrawSquare = new Button { Text = "► START SQUARE", Location = new Point(12, 85), Size = new Size(260, 34), BackColor = Color.FromArgb(46, 125, 50), ForeColor = Color.White, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnDrawSquare.FlatAppearance.BorderSize = 0;
            pnlSquare.Controls.Add(lblSquare); pnlSquare.Controls.Add(lblSide); pnlSquare.Controls.Add(txtSide); pnlSquare.Controls.Add(btnDrawSquare);
            
            Panel pnlCircle = new Panel { Location = new Point(320, 50), Size = new Size(290, 140), BackColor = Color.White };
            pnlCircle.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlCircle.Width - 1, pnlCircle.Height - 1); };
            Label lblCircle = new Label { Text = "Draw Circle", Font = new Font("Segoe UI", 10.5F, FontStyle.Bold), ForeColor = darkText, Location = new Point(12, 12), AutoSize = true };
            Label lblRadius = new Label { Text = "Radius (mm):", Font = new Font("Segoe UI", 9F), ForeColor = darkText, Location = new Point(12, 48), AutoSize = true };
            TextBox txtRadius = new TextBox { Text = "50.0", Font = new Font("Segoe UI", 10F), Location = new Point(140, 44), Size = new Size(90, 25) };
            Button btnDrawCircle = new Button { Text = "► START CIRCLE", Location = new Point(12, 85), Size = new Size(260, 34), BackColor = Color.FromArgb(70, 95, 133), ForeColor = Color.White, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnDrawCircle.FlatAppearance.BorderSize = 0;
            pnlCircle.Controls.Add(lblCircle); pnlCircle.Controls.Add(lblRadius); pnlCircle.Controls.Add(txtRadius); pnlCircle.Controls.Add(btnDrawCircle);

            workspaceShapes.Controls.Add(lblShapesTitle);
            workspaceShapes.Controls.Add(pnlSquare);
            workspaceShapes.Controls.Add(pnlCircle);

            btnDrawSquare.Click += async (s, e) => {
                if (_connectionChangePending || _statusCommandPending) return;
                if (double.TryParse(txtSide.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double side)) {
                    btnDrawSquare.Enabled = false;
                    await Task.Run(() => _robotService.DrawSquareAsync(side));
                    if (!IsDisposed && !Disposing) btnDrawSquare.Enabled = true;
                }
            };
            btnDrawCircle.Click += async (s, e) => {
                if (_connectionChangePending || _statusCommandPending) return;
                if (double.TryParse(txtRadius.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rad)) {
                    btnDrawCircle.Enabled = false;
                    await Task.Run(() => _robotService.DrawCircleAsync(rad));
                    if (!IsDisposed && !Disposing) btnDrawCircle.Enabled = true;
                }
            };

            // --- Camera Vision Workspace ---
            Panel workspaceCamera = InitializeCameraWorkspace(primaryColor, darkText, borderColor);

            // Put all 4 workspaces into workspaceContainer
            workspaceContainer.Controls.Add(workspaceCamera);
            workspaceContainer.Controls.Add(workspaceShapes);
            workspaceContainer.Controls.Add(workspaceGuide);
            workspaceContainer.Controls.Add(workspace);

            // Navigation Events (4 tabs)
            navBtns[0].Click += (s, e) => {
                workspace.Visible = true; workspaceGuide.Visible = false; workspaceShapes.Visible = false; workspaceCamera.Visible = false;
                updateNavStyles(0);
            };
            navBtns[1].Click += (s, e) => {
                workspace.Visible = false; workspaceGuide.Visible = true; workspaceShapes.Visible = false; workspaceCamera.Visible = false;
                updateNavStyles(1);
                layoutGuide();
            };
            navBtns[2].Click += (s, e) => {
                workspace.Visible = false; workspaceGuide.Visible = false; workspaceShapes.Visible = true; workspaceCamera.Visible = false;
                updateNavStyles(2);
            };
            navBtns[3].Click += (s, e) => {
                workspace.Visible = false; workspaceGuide.Visible = false; workspaceShapes.Visible = false; workspaceCamera.Visible = true;
                updateNavStyles(3);
            };

            // Initial nav selection style
            updateNavStyles(0);

            // Add everything to Form in correct dock z-order
            this.Controls.Add(workspaceContainer);
            this.Controls.Add(splitterBottom);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(splitterRight);
            this.Controls.Add(rightPanel);
            this.Controls.Add(sidebar);
            this.Controls.Add(header);
            
            // Trigger initial UI update
            UpdateButtonStates();
        }

        private void panelHeader_Paint(object sender, PaintEventArgs e)
        {
            
            using (LinearGradientBrush brush = new LinearGradientBrush(
                panelHeader.ClientRectangle,
                Color.FromArgb(20, 20, 35),
                Color.FromArgb(35, 30, 60),
                LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, panelHeader.ClientRectangle);
            }

            // Draw bottom accent line
            using (Pen pen = new Pen(Color.FromArgb(60, 120, 220), 2))
            {
                e.Graphics.DrawLine(pen, 0, panelHeader.Height - 1, panelHeader.Width, panelHeader.Height - 1);
            }
        }

        private void panelStatus_Paint(object sender, PaintEventArgs e)
        {
            // Draw rounded border for status panel
            using (Pen pen = new Pen(Color.FromArgb(45, 50, 72), 1))
            {
                Rectangle rect = new Rectangle(0, 0, panelStatus.Width - 1, panelStatus.Height - 1);
                int radius = 8;
                using (GraphicsPath path = CreateRoundedRectPath(rect, radius))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }

        private GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

            }
}




