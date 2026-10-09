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

        // Expose parameters so main class can update them
        private System.Collections.Generic.Dictionary<string, TextBox> paramBoxes = new System.Collections.Generic.Dictionary<string, TextBox>();
        
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
            panelStatus.Paint += panelStatus_Paint;this.BackColor = Color.FromArgb(245, 246, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(0)));

            // ==========================================
            // PROJECT INFORMATION
            // ==========================================
            string projAuthor = "Tran Quang Truong";
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

            Color brandOrange = Color.FromArgb(232, 93, 4);
            Color darkText = Color.FromArgb(40, 40, 40);
            Color lightBg = Color.White;
            Color sidebarBg = Color.FromArgb(250, 250, 250);
            Color borderColor = Color.FromArgb(220, 220, 220);

            // 1. Top Header
            Panel header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = lightBg };
            header.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, header.Height - 1, header.Width, header.Height - 1);
            };
            
            Label lblTitle = new Label {
                Text = "ROBOT STUDIO",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = brandOrange,
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
                        Location = new Point(20, 10) // Left of the title
                    };
                    header.Controls.Add(picLogo);
                }
            }
            catch { }

            // Status indicators in header
            Label lblDatePrefix = new Label { Text = "DATE:", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(100, 100, 100), AutoSize = true, Location = new Point(header.Width - 280, 10), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Label lblDateValue = new Label { Text = DateTime.Now.ToString("ddd, dd-MM-yyyy, HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).ToUpper(), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = darkText, AutoSize = true, Location = new Point(header.Width - 240, 10), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            
            Label lblAlarmPrefix = new Label { Text = "ALARM:", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(100, 100, 100), AutoSize = true, Location = new Point(header.Width - 280, 30), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Label lblAlarmValue = new Label { Text = "No alarm !", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(40, 167, 69), AutoSize = true, Location = new Point(header.Width - 230, 30), Anchor = AnchorStyles.Right | AnchorStyles.Top };

            header.Controls.Add(lblDatePrefix);
            header.Controls.Add(lblDateValue);
            header.Controls.Add(lblAlarmPrefix);
            header.Controls.Add(lblAlarmValue);

            // Timer for real-time clock
            System.Windows.Forms.Timer clockTimer = new System.Windows.Forms.Timer();
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

            string[] navItems = { "🏠 Home", "🎮 Guide key", "📈 Motion Profile", "🎨 Shape Drawing" };
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
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = brandOrange,
                    BackColor = sidebarBg,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 235, 235);
                btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(225, 225, 225);
                sidebar.Controls.Add(btn);
                navBtns[i] = btn;
                navY += 45;
            }

            Panel activeIndicator = new Panel { BackColor = brandOrange, Width = 4, Height = 45, Location = new Point(0, 20) };
            sidebar.Controls.Add(activeIndicator);
            activeIndicator.BringToFront();

            // Project Info Panel in Sidebar
            Panel pnlProjInfo = new Panel { Dock = DockStyle.Bottom, Height = 250, BackColor = sidebarBg, Padding = new Padding(10) };
            pnlProjInfo.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, pnlProjInfo.Width, 0); // Top border line
            };
            Label lblProjTitle = new Label { Text = "PROJECT INFO", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(150, 150, 150), AutoSize = true, Location = new Point(15, 15) };
            pnlProjInfo.Controls.Add(lblProjTitle);

            string[] infoLabels = { "Author:", "Create Date:", "Project Name:", "Target Devices:", "Tool Versions:", "Description:", "Comments:" };
            string[] infoValues = { projAuthor, projDate, projName, projTarget, projToolVers, projDesc, projComments };
            
            for (int i = 0; i < infoLabels.Length; i++)
            {
                Label lblKey = new Label { Text = infoLabels[i], Font = new Font("Segoe UI", 8F, FontStyle.Regular), ForeColor = Color.FromArgb(100, 100, 100), AutoSize = true, Location = new Point(15, 45 + i*25) };
                Label lblVal = new Label { Text = infoValues[i], Font = new Font("Segoe UI", 8F, FontStyle.Bold), ForeColor = darkText, AutoSize = true, Location = new Point(100, 45 + i*25) };
                pnlProjInfo.Controls.Add(lblKey);
                pnlProjInfo.Controls.Add(lblVal);
            }
            sidebar.Controls.Add(pnlProjInfo);

            // 3. Right Status Panel
            Panel rightPanel = new Panel { Dock = DockStyle.Right, Width = 280, BackColor = lightBg };
            rightPanel.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, 0, rightPanel.Height);
            };
            Label lblStatusTitle = new Label { Text = "ROBOT STATUS", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, AutoSize = true, Location = new Point(20, 20) };
            rightPanel.Controls.Add(lblStatusTitle);
            
            lblConnectionStatus.Location = new Point(20, 60);
            lblConnectionStatus.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            lblServoStatus.Location = new Point(20, 90);
            lblServoStatus.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            lblWatchdogStatus.Location = new Point(20, 120);
            lblWatchdogStatus.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            
            rightPanel.Controls.Add(lblConnectionStatus);
            rightPanel.Controls.Add(lblServoStatus);
            rightPanel.Controls.Add(lblWatchdogStatus);

            // Hide unused pos controls
            lblCurrentPos.Visible = false;
            btnReadPos.Visible = false;

            // Handle bottom right anchoring when form resizes
            rightPanel.Resize += (s, e) => {
                btnConnect.Location = new Point(20, rightPanel.Height - 160);
                btnServoOn.Location = new Point(20, rightPanel.Height - 110);
                btnServoOff.Location = new Point(145, rightPanel.Height - 110);
                btnWatchdog.Location = new Point(20, rightPanel.Height - 60);
            };

            btnConnect.Size = new Size(240, 40);
            btnConnect.FlatStyle = FlatStyle.Flat; btnConnect.FlatAppearance.BorderSize = 0; btnConnect.Cursor = Cursors.Hand;
            btnConnect.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            btnServoOn.Size = new Size(115, 40);
            btnServoOn.FlatStyle = FlatStyle.Flat; btnServoOn.FlatAppearance.BorderSize = 0; btnServoOn.Cursor = Cursors.Hand;
            btnServoOn.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            btnServoOff.Size = new Size(115, 40);
            btnServoOff.FlatStyle = FlatStyle.Flat; btnServoOff.FlatAppearance.BorderSize = 0; btnServoOff.Cursor = Cursors.Hand;
            btnServoOff.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            btnWatchdog.Size = new Size(240, 40);
            btnWatchdog.FlatStyle = FlatStyle.Flat; btnWatchdog.FlatAppearance.BorderSize = 0; btnWatchdog.Cursor = Cursors.Hand;
            btnWatchdog.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            rightPanel.Controls.Add(btnConnect);
            rightPanel.Controls.Add(btnServoOn);
            rightPanel.Controls.Add(btnServoOff);
            rightPanel.Controls.Add(btnWatchdog);

            // 4. Bottom Panel (Console)
            Panel bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 220, BackColor = lightBg };
            bottomPanel.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, bottomPanel.Width, 0);
            };
            Label lblConsoleTitle = new Label { Text = "SYSTEM LOG", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(150,150,150), AutoSize = true, Location = new Point(20, 10) };
            bottomPanel.Controls.Add(lblConsoleTitle);

            txtLog.Location = new Point(20, 35);
            txtLog.Height = 165;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.BackColor = Color.FromArgb(250, 250, 250);
            txtLog.ForeColor = Color.FromArgb(60, 60, 60);
            txtLog.Font = new Font("Consolas", 9.5F);
            // Handle resizing for txtLog manually since Anchor needs a parent layout pass
            bottomPanel.Resize += (s, e) => { txtLog.Width = bottomPanel.Width - 40; };
            bottomPanel.Controls.Add(txtLog);

            // 5. Main Workspace
            FlowLayoutPanel workspace = new FlowLayoutPanel { 
                Dock = DockStyle.Fill, 
                BackColor = Color.FromArgb(245, 246, 250),
                Padding = new Padding(20),
                AutoScroll = true
            };
            
            // Helper to create cards
            Func<string, Size, Panel> CreateCard = (title, size) => {
                Panel card = new Panel { BackColor = lightBg, Margin = new Padding(10), Size = size };
                card.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, card.Width-1, card.Height-1); };
                Label lbl = new Label { Text = title, Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, AutoSize = true, Location = new Point(20, 15) };
                card.Controls.Add(lbl);
                return card;
            };

            // Helper to create label-value pairs
            Action<Panel, string, string, int, int> AddParam = (parent, labelText, valueText, x, y) => {
                Label lblParamTitle = new Label { Text = labelText, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = darkText, AutoSize = true, Location = new Point(x, y+3) };
                TextBox txtValue = new TextBox { Text = valueText, Font = new Font("Consolas", 10F), ForeColor = darkText, BackColor = Color.FromArgb(250, 250, 250), BorderStyle = BorderStyle.FixedSingle, Location = new Point(x + 70, y), Size = new Size(70, 25), ReadOnly = true, TextAlign = HorizontalAlignment.Right };
                parent.Controls.Add(lblParamTitle);
                parent.Controls.Add(txtValue);
                paramBoxes[labelText] = txtValue;
            };

            // Card: Theta
            Panel cardTheta = CreateCard("THETA (°)", new Size(200, 310));
            for (int i=1; i<=6; i++) AddParam(cardTheta, "Theta " + i + ":", "0.0", 20, 50 + (i-1)*40);
            workspace.Controls.Add(cardTheta);

            // Card: Speed
            Panel cardSpeed = CreateCard("SPEED (°/s)", new Size(200, 310));
            for (int i=1; i<=6; i++) AddParam(cardSpeed, "Speed " + i + ":", "0.0", 20, 50 + (i-1)*40);
            workspace.Controls.Add(cardSpeed);

            // Column 3 for Matrix and Acc/Dec
            FlowLayoutPanel col3 = new FlowLayoutPanel { Size = new Size(320, 310), Margin = new Padding(0), FlowDirection = FlowDirection.TopDown };

            // Card: Matrix
            Panel cardMatrix = CreateCard("EE ORIENTATION MATRIX", new Size(300, 190));
            for (int r=0; r<3; r++) {
                for (int c=0; c<3; c++) {
                    TextBox txtVal = new TextBox { Text = "0.00", Font = new Font("Consolas", 10F), ForeColor = darkText, BackColor = Color.FromArgb(250, 250, 250), BorderStyle = BorderStyle.FixedSingle, Location = new Point(20 + c*85, 55 + r*40), Size = new Size(70, 25), ReadOnly = true, TextAlign = HorizontalAlignment.Right };
                    cardMatrix.Controls.Add(txtVal);
                }
            }
            col3.Controls.Add(cardMatrix);

            // Card: Acc & Dec
            Panel cardAcc = CreateCard("ACC & DEC (ms)", new Size(300, 100));
            AddParam(cardAcc, "Acc:", "80", 20, 55);
            AddParam(cardAcc, "Dec:", "80", 160, 55);
            col3.Controls.Add(cardAcc);

            workspace.Controls.Add(col3);

            // Card: Position (full width of the 3 columns)
            Panel cardPos = CreateCard("POSITION (mm)", new Size(740, 100));
            AddParam(cardPos, "EEx:", "0.0", 50, 50);
            AddParam(cardPos, "EEy:", "0.0", 280, 50);
            AddParam(cardPos, "EEz:", "0.0", 510, 50);
            workspace.Controls.Add(cardPos);

            // Guide Key Workspace
            Panel workspaceGuide = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 246, 250), Padding = new Padding(20), Visible = false };
            
            Button btnMode1 = new Button { Text = "Joint (TextBox)", Location = new Point(20, 20), Size = new Size(130, 35), FlatStyle = FlatStyle.Flat, BackColor = brandOrange, ForeColor = Color.White, Cursor = Cursors.Hand };
            btnMode1.FlatAppearance.BorderSize = 0;
            Button btnMode2 = new Button { Text = "Joint (Slider)", Location = new Point(160, 20), Size = new Size(130, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = darkText, Cursor = Cursors.Hand };
            btnMode2.FlatAppearance.BorderSize = 0;
            Button btnMode3 = new Button { Text = "Cartesian (XYZ)", Location = new Point(300, 20), Size = new Size(140, 35), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = darkText, Cursor = Cursors.Hand };
            btnMode3.FlatAppearance.BorderSize = 0;

            Label lblSpeed = new Label { Text = "Speed:", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, Location = new Point(470, 25), AutoSize = true };
            TrackBar tbSpeed = new TrackBar { Minimum = 1, Maximum = 100, Value = 10, Location = new Point(530, 20), Size = new Size(150, 45), TickStyle = TickStyle.None };
            Label valSpeed = new Label { Text = "10 %", Font = new Font("Segoe UI", 10F), ForeColor = darkText, Location = new Point(690, 25), AutoSize = true };
            tbSpeed.Scroll += (s, e) => { valSpeed.Text = tbSpeed.Value.ToString() + " %"; };
            
            workspaceGuide.Controls.Add(lblSpeed);
            workspaceGuide.Controls.Add(tbSpeed);
            workspaceGuide.Controls.Add(valSpeed);

            Panel pnlMode1 = new Panel { Location = new Point(20, 80), Size = new Size(420, 370), Visible = true, BackColor = Color.White };
            pnlMode1.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlMode1.Width - 1, pnlMode1.Height - 1); };
            
            Panel pnlMode2 = new Panel { Location = new Point(20, 80), Size = new Size(420, 370), Visible = false, BackColor = Color.White };
            pnlMode2.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlMode2.Width - 1, pnlMode2.Height - 1); };

            Panel pnlMode3 = new Panel { Location = new Point(20, 80), Size = new Size(420, 370), Visible = false, BackColor = Color.White };
            pnlMode3.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlMode3.Width - 1, pnlMode3.Height - 1); };

            workspaceGuide.Controls.Add(btnMode1);
            workspaceGuide.Controls.Add(btnMode2);
            workspaceGuide.Controls.Add(btnMode3);
            workspaceGuide.Controls.Add(pnlMode1);
            workspaceGuide.Controls.Add(pnlMode2);
            workspaceGuide.Controls.Add(pnlMode3);
            
            TextBox[] txtAxes = new TextBox[4];
            TrackBar[] tbAxes = new TrackBar[4];
            TextBox[] txtCart = new TextBox[4]; // X, Y, Z, C

            // Title for panels
            pnlMode1.Controls.Add(new Label { Text = "Joint Control (Direct Input)", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = brandOrange, Location = new Point(20, 15), AutoSize = true });
            pnlMode2.Controls.Add(new Label { Text = "Joint Control (Slider Input)", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = brandOrange, Location = new Point(20, 15), AutoSize = true });
            pnlMode3.Controls.Add(new Label { Text = "Cartesian Control (XYZC)", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = brandOrange, Location = new Point(20, 15), AutoSize = true });

            // Build Mode 1 & 2 controls
            for (int i = 1; i <= 4; i++) {
                int y = 50 + (i - 1) * 50;
                
                // Mode 1: TextBox
                Label lbl1 = new Label { Text = "Axis " + i + ":", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, Location = new Point(40, y+5), AutoSize = true };
                TextBox txt = new TextBox { Text = "0.0", Font = new Font("Segoe UI", 11F), BackColor = Color.FromArgb(250,250,250), ForeColor = darkText, Location = new Point(110, y), Size = new Size(120, 27), TextAlign = HorizontalAlignment.Right };
                Label unit1 = new Label { Text = (i==3) ? "mm" : "deg", Font = new Font("Segoe UI", 10F), ForeColor = Color.FromArgb(150, 150, 150), Location = new Point(240, y+5), AutoSize = true };
                txtAxes[i-1] = txt;
                pnlMode1.Controls.Add(lbl1); pnlMode1.Controls.Add(txt); pnlMode1.Controls.Add(unit1);
                
                // Mode 2: Slider
                Label lbl2 = new Label { Text = "Axis " + i + ":", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, Location = new Point(20, y+5), AutoSize = true };
                TrackBar tb = new TrackBar { Minimum = -180, Maximum = 180, Value = 0, Location = new Point(90, y), Size = new Size(220, 45), TickStyle = TickStyle.None };
                if (i==3) { tb.Minimum = 0; tb.Maximum = 150; }
                Label val2 = new Label { Text = "0.0", Font = new Font("Segoe UI", 11F), ForeColor = darkText, Location = new Point(320, y+5), AutoSize = true };
                
                int axisNo = i;
                tb.Scroll += (s, e) => { val2.Text = tb.Value.ToString() + ".0"; };
                
                tb.MouseUp += (s, e) => { 
                    if (_robotService.State.IsConnected) {
                        try {
                            _robotService.EnsureWatchdogRunning();
                            _robotService.SetSpeed(tbSpeed.Value);
                            Task.Run(() => {
                                try { _robotService.MoveJointAxis(axisNo, tb.Value); } catch { }
                            });
                        } catch { }
                    }
                };
                
                tbAxes[i-1] = tb;
                pnlMode2.Controls.Add(lbl2); pnlMode2.Controls.Add(tb); pnlMode2.Controls.Add(val2);
            }

            // Build Mode 3 controls (Cartesian)
            string[] cartLabels = { "X (mm):", "Y (mm):", "Z (mm):", "C (deg):" };
            for(int i = 0; i < 4; i++) {
                int y = 60 + i * 55;
                Label lbl = new Label { Text = cartLabels[i], Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, Location = new Point(60, y+5), AutoSize = true };
                TextBox txt = new TextBox { Text = "0.0", Font = new Font("Segoe UI", 11F), BackColor = Color.FromArgb(250,250,250), ForeColor = darkText, Location = new Point(140, y), Size = new Size(150, 27), TextAlign = HorizontalAlignment.Right };
                txtCart[i] = txt;
                pnlMode3.Controls.Add(lbl); pnlMode3.Controls.Add(txt);
            }

            btnMode1.Click += (s, e) => { pnlMode1.Visible = true; pnlMode2.Visible = false; pnlMode3.Visible = false; btnMode1.BackColor = brandOrange; btnMode1.ForeColor = Color.White; btnMode2.BackColor = Color.White; btnMode2.ForeColor = darkText; btnMode3.BackColor = Color.White; btnMode3.ForeColor = darkText; };
            btnMode2.Click += (s, e) => { pnlMode2.Visible = true; pnlMode1.Visible = false; pnlMode3.Visible = false; btnMode2.BackColor = brandOrange; btnMode2.ForeColor = Color.White; btnMode1.BackColor = Color.White; btnMode1.ForeColor = darkText; btnMode3.BackColor = Color.White; btnMode3.ForeColor = darkText; };
            btnMode3.Click += (s, e) => { pnlMode3.Visible = true; pnlMode1.Visible = false; pnlMode2.Visible = false; btnMode3.BackColor = brandOrange; btnMode3.ForeColor = Color.White; btnMode1.BackColor = Color.White; btnMode1.ForeColor = darkText; btnMode2.BackColor = Color.White; btnMode2.ForeColor = darkText; };

            // Add ONE Run and Stop Button
            Button btnGuideRun = new Button { Text = "RUN", Location = new Point(470, 395), Size = new Size(120, 45), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, Font = new Font("Segoe UI", 12F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnGuideRun.FlatAppearance.BorderSize = 0;
            Button btnGuideStop = new Button { Text = "STOP", Location = new Point(610, 395), Size = new Size(120, 45), BackColor = Color.FromArgb(220, 53, 69), ForeColor = Color.White, Font = new Font("Segoe UI", 12F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnGuideStop.FlatAppearance.BorderSize = 0;
            
            // Sub-label for run
            Label lblRunHint = new Label { Text = "(Executes active tab)", Font = new Font("Segoe UI", 9F, FontStyle.Italic), ForeColor = Color.FromArgb(150, 150, 150), Location = new Point(470, 445), AutoSize = true };

            workspaceGuide.Controls.Add(btnGuideRun);
            workspaceGuide.Controls.Add(btnGuideStop);
            workspaceGuide.Controls.Add(lblRunHint);

            // Logic for Run
            btnGuideRun.Click += async (s, e) => {
                if (!_robotService.State.IsConnected) {
                    _logger.Log("[Guide] Cannot run: Robot is not connected.");
                    return;
                }
                try {
                    _robotService.EnsureWatchdogRunning();
                    _robotService.SetSpeed(tbSpeed.Value);
                    
                    if (pnlMode1.Visible || pnlMode2.Visible) {
                        _logger.Log($"[Guide] RUN Joint started at {tbSpeed.Value}% speed...");
                        bool isMode1 = pnlMode1.Visible;
                        
                        await Task.Run(() => {
                            for (int i = 0; i < 4; i++) {
                                double val = 0;
                                if (isMode1) {
                                    string textVal = txtAxes[i].Text.Replace(",", ".");
                                    double.TryParse(textVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val);
                                } else {
                                    val = tbAxes[i].Value;
                                }
                                _robotService.MoveJointAxis(i + 1, val);
                            }
                        });
                    } else if (pnlMode3.Visible) {
                        _logger.Log($"[Guide] RUN Cartesian started at {tbSpeed.Value}% speed...");
                        
                        double.TryParse(txtCart[0].Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double x);
                        double.TryParse(txtCart[1].Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double y);
                        double.TryParse(txtCart[2].Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double z);
                        double.TryParse(txtCart[3].Text.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double c);
                        
                        await Task.Run(() => {
                            _robotService.MoveCartesian(x, y, z, c);
                        });
                    }
                } catch (Exception ex) {
                    _logger.Log("[Guide] RUN Error: " + ex.Message);
                }
            };
            // Logic for Stop
            btnGuideStop.Click += (s, e) => {
                _robotService.StopMotion();
            };
            // --- Motion Profile Workspace ---
            Panel workspaceMotion = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 246, 250), Padding = new Padding(20), Visible = false };
            
            Label lblMotionTitle = new Label { Text = "Motion Profiles", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = brandOrange, AutoSize = true, Location = new Point(20, 20) };
            
            Panel pnlSCurve = new Panel { Location = new Point(20, 70), Size = new Size(200, 80), BackColor = Color.White };
            pnlSCurve.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlSCurve.Width - 1, pnlSCurve.Height - 1); };
            Label lblSCurve = new Label { Text = "S-Curve Profile", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, Location = new Point(15, 10), AutoSize = true };
            btnStartDemo = new Button { Text = "► START", Location = new Point(15, 35), Size = new Size(170, 32), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnStartDemo.FlatAppearance.BorderSize = 0;
            pnlSCurve.Controls.Add(lblSCurve);
            pnlSCurve.Controls.Add(btnStartDemo);
            
            Panel pnlTrap = new Panel { Location = new Point(240, 70), Size = new Size(200, 80), BackColor = Color.White };
            pnlTrap.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlTrap.Width - 1, pnlTrap.Height - 1); };
            Label lblTrap = new Label { Text = "Trapezoidal Profile", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = darkText, Location = new Point(15, 10), AutoSize = true };
            btnStartTrap = new Button { Text = "► START", Location = new Point(15, 35), Size = new Size(170, 32), BackColor = Color.FromArgb(0, 123, 255), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnStartTrap.FlatAppearance.BorderSize = 0;
            pnlTrap.Controls.Add(lblTrap);
            pnlTrap.Controls.Add(btnStartTrap);

            motionChart = new System.Windows.Forms.DataVisualization.Charting.Chart();
            motionChart.Location = new Point(20, 170);
            motionChart.Size = new Size(700, 400);
            
            var chartArea = new System.Windows.Forms.DataVisualization.Charting.ChartArea();
            chartArea.AxisX.Title = "Time (s)";
            chartArea.AxisX.Minimum = 0;
            chartArea.AxisX.LabelStyle.Format = "0.00";
            chartArea.AxisX.MajorGrid.LineColor = Color.LightGray;
            chartArea.AxisY.MajorGrid.LineColor = Color.LightGray;
            motionChart.ChartAreas.Add(chartArea);

            sPos = new System.Windows.Forms.DataVisualization.Charting.Series("Position");
            sPos.ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Line;
            sPos.BorderWidth = 2;
            
            sVel = new System.Windows.Forms.DataVisualization.Charting.Series("Velocity");
            sVel.ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Line;
            sVel.BorderWidth = 2;
            
            sAcc = new System.Windows.Forms.DataVisualization.Charting.Series("Acceleration");
            sAcc.ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Line;
            sAcc.BorderWidth = 2;

            motionChart.Series.Add(sPos);
            motionChart.Series.Add(sVel);
            motionChart.Series.Add(sAcc);
            
            var legend = new System.Windows.Forms.DataVisualization.Charting.Legend();
            legend.Docking = System.Windows.Forms.DataVisualization.Charting.Docking.Top;
            motionChart.Legends.Add(legend);

            workspaceMotion.Controls.Add(lblMotionTitle);
            workspaceMotion.Controls.Add(pnlSCurve);
            workspaceMotion.Controls.Add(pnlTrap);
            workspaceMotion.Controls.Add(motionChart);

                        btnStartDemo.Click += async (s, e) => {
                btnStartDemo.Enabled = false;
                btnStartTrap.Enabled = false;
                sPos.Points.Clear();
                sVel.Points.Clear();
                sAcc.Points.Clear();
                motionChart.ChartAreas[0].AxisX.StripLines.Clear();

                double startPos = _robotService.GetCurrentPosition()?.JointPosition.J1 ?? 0;
                
                _robotService.EnsureWatchdogRunning();
                _robotService.SetSpeed((int)Constants.DemoVMax);
                _robotService.MoveJointAxis(1, startPos + Constants.DemoDistance);

                var result = await _motionService.StartSCurveDemoAsync(startPos, Constants.DemoDistance, Constants.DemoVMax, Constants.DemoAMax, Constants.DemoJMax);
                
                motionChart.ChartAreas[0].AxisX.Maximum = result.xAxisMax;
                
                var stripLine = new System.Windows.Forms.DataVisualization.Charting.StripLine();
                stripLine.IntervalOffset = result.finishTime;
                stripLine.BorderColor = Color.Red;
                stripLine.BorderDashStyle = System.Windows.Forms.DataVisualization.Charting.ChartDashStyle.Dash;
                stripLine.BorderWidth = 2;
                stripLine.Text = $"End: {result.finishTime:F2}s";
                stripLine.TextAlignment = StringAlignment.Near;
                stripLine.TextLineAlignment = StringAlignment.Far;
                stripLine.ForeColor = Color.Red;
                stripLine.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                motionChart.ChartAreas[0].AxisX.StripLines.Add(stripLine);
                
                try {
                    string filename = System.IO.Path.Combine(Application.StartupPath, $"SCurve_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    motionChart.SaveImage(filename, System.Windows.Forms.DataVisualization.Charting.ChartImageFormat.Png);
                    _logger.Log($"[Motion] Saved S-Curve chart to {filename}");
                } catch (Exception ex) {
                    _logger.Log($"[Motion] Error saving chart: {ex.Message}");
                }
            };

            btnStartTrap.Click += async (s, e) => {
                btnStartDemo.Enabled = false;
                btnStartTrap.Enabled = false;
                sPos.Points.Clear();
                sVel.Points.Clear();
                sAcc.Points.Clear();
                motionChart.ChartAreas[0].AxisX.StripLines.Clear();

                double startPos = _robotService.GetCurrentPosition()?.JointPosition.J1 ?? 0;
                
                _robotService.EnsureWatchdogRunning();
                _robotService.SetSpeed((int)Constants.DemoVMax);
                _robotService.MoveJointAxis(1, startPos + Constants.DemoDistance);

                var result = await _motionService.StartTrapezoidalDemoAsync(startPos, Constants.DemoDistance, Constants.DemoVMax, Constants.DemoAMax, Constants.DemoJMax);
                
                motionChart.ChartAreas[0].AxisX.Maximum = result.xAxisMax;
                
                var stripLine = new System.Windows.Forms.DataVisualization.Charting.StripLine();
                stripLine.IntervalOffset = result.finishTime;
                stripLine.BorderColor = Color.Red;
                stripLine.BorderDashStyle = System.Windows.Forms.DataVisualization.Charting.ChartDashStyle.Dash;
                stripLine.BorderWidth = 2;
                stripLine.Text = $"End: {result.finishTime:F2}s";
                stripLine.TextAlignment = StringAlignment.Near;
                stripLine.TextLineAlignment = StringAlignment.Far;
                stripLine.ForeColor = Color.Red;
                stripLine.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                motionChart.ChartAreas[0].AxisX.StripLines.Add(stripLine);
                
                try {
                    string filename = System.IO.Path.Combine(Application.StartupPath, $"Trapezoidal_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    motionChart.SaveImage(filename, System.Windows.Forms.DataVisualization.Charting.ChartImageFormat.Png);
                    _logger.Log($"[Motion] Saved Trapezoidal chart to {filename}");
                } catch (Exception ex) {
                    _logger.Log($"[Motion] Error saving chart: {ex.Message}");
                }
            };
            // --- Shape Drawing Workspace ---
            Panel workspaceShapes = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 246, 250), Padding = new Padding(20), Visible = false };
            
            Label lblShapesTitle = new Label { Text = "Shape Drawing", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = brandOrange, AutoSize = true, Location = new Point(20, 20) };
            
            Panel pnlSquare = new Panel { Location = new Point(20, 70), Size = new Size(300, 150), BackColor = Color.White };
            pnlSquare.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlSquare.Width - 1, pnlSquare.Height - 1); };
            Label lblSquare = new Label { Text = "Draw Square", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = darkText, Location = new Point(15, 15), AutoSize = true };
            Label lblSide = new Label { Text = "Side Length (mm):", Font = new Font("Segoe UI", 10F), ForeColor = darkText, Location = new Point(15, 55), AutoSize = true };
            TextBox txtSide = new TextBox { Text = "100.0", Font = new Font("Segoe UI", 11F), Location = new Point(150, 50), Size = new Size(100, 27) };
            Button btnDrawSquare = new Button { Text = "► START SQUARE", Location = new Point(15, 95), Size = new Size(270, 35), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnDrawSquare.FlatAppearance.BorderSize = 0;
            pnlSquare.Controls.Add(lblSquare); pnlSquare.Controls.Add(lblSide); pnlSquare.Controls.Add(txtSide); pnlSquare.Controls.Add(btnDrawSquare);
            
            Panel pnlCircle = new Panel { Location = new Point(340, 70), Size = new Size(300, 150), BackColor = Color.White };
            pnlCircle.Paint += (s, e) => { e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlCircle.Width - 1, pnlCircle.Height - 1); };
            Label lblCircle = new Label { Text = "Draw Circle", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = darkText, Location = new Point(15, 15), AutoSize = true };
            Label lblRadius = new Label { Text = "Radius (mm):", Font = new Font("Segoe UI", 10F), ForeColor = darkText, Location = new Point(15, 55), AutoSize = true };
            TextBox txtRadius = new TextBox { Text = "50.0", Font = new Font("Segoe UI", 11F), Location = new Point(150, 50), Size = new Size(100, 27) };
            Button btnDrawCircle = new Button { Text = "► START CIRCLE", Location = new Point(15, 95), Size = new Size(270, 35), BackColor = Color.FromArgb(0, 123, 255), ForeColor = Color.White, Font = new Font("Segoe UI", 10F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnDrawCircle.FlatAppearance.BorderSize = 0;
            pnlCircle.Controls.Add(lblCircle); pnlCircle.Controls.Add(lblRadius); pnlCircle.Controls.Add(txtRadius); pnlCircle.Controls.Add(btnDrawCircle);

            workspaceShapes.Controls.Add(lblShapesTitle);
            workspaceShapes.Controls.Add(pnlSquare);
            workspaceShapes.Controls.Add(pnlCircle);

            btnDrawSquare.Click += async (s, e) => {
                if (double.TryParse(txtSide.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double side)) {
                    btnDrawSquare.Enabled = false;
                    await _robotService.DrawSquareAsync(side);
                    btnDrawSquare.Enabled = true;
                }
            };
            btnDrawCircle.Click += async (s, e) => {
                if (double.TryParse(txtRadius.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rad)) {
                    btnDrawCircle.Enabled = false;
                    await _robotService.DrawCircleAsync(rad);
                    btnDrawCircle.Enabled = true;
                }
            };

            // Navigation Events
            navBtns[0].Click += (s, e) => { workspace.Visible = true; workspaceGuide.Visible = false; workspaceMotion.Visible = false; workspaceShapes.Visible = false; activeIndicator.Location = new Point(0, 20); };
            navBtns[1].Click += (s, e) => { workspace.Visible = false; workspaceGuide.Visible = true; workspaceMotion.Visible = false; workspaceShapes.Visible = false; activeIndicator.Location = new Point(0, 65); };
            navBtns[2].Click += (s, e) => { workspace.Visible = false; workspaceGuide.Visible = false; workspaceMotion.Visible = true; workspaceShapes.Visible = false; activeIndicator.Location = new Point(0, 110); };
            navBtns[3].Click += (s, e) => { workspace.Visible = false; workspaceGuide.Visible = false; workspaceMotion.Visible = false; workspaceShapes.Visible = true; activeIndicator.Location = new Point(0, 155); };

            System.Windows.Forms.Timer dataTimer = new System.Windows.Forms.Timer();
            dataTimer.Interval = 500;
            dataTimer.Tick += (s, e) => {
                if (_robotService.State.IsConnected) {
                    try {
                        TsPointS pos = _robotService.GetCurrentPosition()?.WorldPosition; 
                        if (paramBoxes.ContainsKey("EEx:")) paramBoxes["EEx:"].Text = pos.X.ToString("F3");
                        if (paramBoxes.ContainsKey("EEy:")) paramBoxes["EEy:"].Text = pos.Y.ToString("F3");
                        if (paramBoxes.ContainsKey("EEz:")) paramBoxes["EEz:"].Text = pos.Z.ToString("F3");
                    } catch { }
                    try {
                        TsJointS jpos = _robotService.GetCurrentPosition()?.JointPosition;
                        if (paramBoxes.ContainsKey("Theta 1:")) paramBoxes["Theta 1:"].Text = jpos.J1.ToString("F3");
                        if (paramBoxes.ContainsKey("Theta 2:")) paramBoxes["Theta 2:"].Text = jpos.J2.ToString("F3");
                        if (paramBoxes.ContainsKey("Theta 3:")) paramBoxes["Theta 3:"].Text = jpos.J3.ToString("F3");
                        if (paramBoxes.ContainsKey("Theta 4:")) paramBoxes["Theta 4:"].Text = jpos.J4.ToString("F3");
                    } catch { }
                }
            };
            dataTimer.Start();

            // Add everything to form
            this.Controls.Add(workspaceShapes);
            this.Controls.Add(workspaceMotion);
            this.Controls.Add(workspaceGuide);
            this.Controls.Add(workspace);
            this.Controls.Add(rightPanel);
            this.Controls.Add(bottomPanel);
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




