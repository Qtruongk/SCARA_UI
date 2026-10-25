using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;
using Test_1.Models;

namespace Test_1.UI
{
    public partial class MainForm
    {
        // Live Camera Controls (AForge DirectShow)
        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _videoSource;
        private PictureBox picCameraLive;
        private ComboBox cmbCameraDevices;
        private ComboBox cmbResolutions;
        private Button btnToggleCamera;
        private readonly object _frameLock = new object();

        // Point-and-Click Target & Move controls
        private PointF? _selectedPixel = null;
        private double? _targetRobotX = null;
        private double? _targetRobotY = null;
        private double? _targetRobotZ = null;
        private double? _targetRobotRadius = null;
        private bool _isTargetValidRadius = false;

        private PointF? _hoverPixel = null;
        private double? _hoverRobotX = null;
        private double? _hoverRobotY = null;
        private double? _hoverRobotRadius = null;

        private Panel pnlBottomControl;
        private Label lblTargetCoords;
        private Label lblTargetStatus;
        private Button btnClearTarget;
        private TrackBar tbCameraMoveSpeed;
        private Label lblCameraMoveSpeedVal;
        private Button btnMoveRobotToTarget;
        private Button btnStopCameraMove;
        private bool _isCameraMoving = false;

        // Current Robot Position Readout controls for Camera Tab
        private Panel pnlRobotReadout;
        private Label lblReadoutWorldTitle;
        private Label lblReadoutJointTitle;
        private Label[] worldHeaders;
        private Label[] worldValues;
        private Label[] jointHeaders;
        private Label[] jointValues;
        private Label lblCameraRobotStatus;

        private readonly double[] _cameraLastWorld = new double[4];
        private readonly double[] _cameraLastJoints = new double[4];
        private bool _cameraConnected = false;
        private bool _cameraHasFeedback = false;
        private bool _cameraStale = true;
        private bool _cameraWaitingForFeedback = false;

        private Panel InitializeCameraWorkspace(Color primaryColor, Color darkText, Color borderColor)
        {
            Panel workspaceCamera = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding = new Padding(12),
                Visible = false
            };

            // 1. Top Control Bar (Clean, Minimal, Professional)
            Panel pnlTopBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.White,
                Padding = new Padding(12, 8, 12, 8)
            };
            pnlTopBar.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, pnlTopBar.Height - 1, pnlTopBar.Width, pnlTopBar.Height - 1);
            };

            Label lblDev = new Label
            {
                Text = "Camera:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = darkText,
                Location = new Point(12, 17),
                AutoSize = true
            };

            cmbCameraDevices = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(68, 14),
                Width = 170
            };
            cmbCameraDevices.SelectedIndexChanged += (s, e) => {
                UpdateResolutionList();
            };

            Label lblRes = new Label
            {
                Text = "Độ phân giải:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = darkText,
                Location = new Point(246, 17),
                AutoSize = true
            };

            cmbResolutions = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(328, 14),
                Width = 105
            };

            btnToggleCamera = new Button
            {
                Text = "▶ BẬT CAMERA (LIVE)",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(442, 12),
                Size = new Size(150, 32),
                Cursor = Cursors.Hand
            };
            btnToggleCamera.FlatAppearance.BorderSize = 0;
            btnToggleCamera.Click += (s, e) => {
                ToggleCameraLive();
            };

            pnlTopBar.Controls.Add(lblDev);
            pnlTopBar.Controls.Add(cmbCameraDevices);
            pnlTopBar.Controls.Add(lblRes);
            pnlTopBar.Controls.Add(cmbResolutions);
            pnlTopBar.Controls.Add(btnToggleCamera);

            // 2. Full Live Viewport (Takes up entire center area)
            Panel pnlViewContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 17, 26),
                Padding = new Padding(0)
            };

            picCameraLive = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 17, 26),
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Cross
            };

            // Custom Paint on PictureBox: Draw Placeholder, Target Reticle, and Hover HUD
            picCameraLive.Paint += (s, e) => {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                if (picCameraLive.Image == null)
                {
                    string text1 = "LOGITECH BRIO 100 - LIVE CAMERA VIEW";
                    using (Font f1 = new Font("Segoe UI", 15F, FontStyle.Bold))
                    {
                        SizeF s1 = g.MeasureString(text1, f1);
                        int cx = picCameraLive.Width / 2;
                        int cy = picCameraLive.Height / 2;
                        g.DrawString(text1, f1, Brushes.Gray, cx - s1.Width / 2, cy - s1.Height / 2);
                    }
                    return;
                }

                // 1. Draw Selected Target Reticle (Hồng tâm mục tiêu)
                if (_selectedPixel.HasValue && _targetRobotX.HasValue && _targetRobotY.HasValue)
                {
                    if (GetScreenPointFromImagePixel(_selectedPixel.Value.X, _selectedPixel.Value.Y, out float sx, out float sy))
                    {
                        Color crossColor = _isTargetValidRadius ? Color.FromArgb(0, 230, 118) : Color.FromArgb(255, 23, 68);
                        using (Pen pCircle = new Pen(crossColor, 2))
                        using (Pen pTick = new Pen(crossColor, 1.5f))
                        using (Brush bCenter = new SolidBrush(crossColor))
                        using (Brush bFill = new SolidBrush(Color.FromArgb(45, crossColor)))
                        {
                            float rOuter = 16f;
                            g.FillEllipse(bFill, sx - rOuter, sy - rOuter, rOuter * 2, rOuter * 2);
                            g.DrawEllipse(pCircle, sx - rOuter, sy - rOuter, rOuter * 2, rOuter * 2);
                            g.FillEllipse(bCenter, sx - 2.5f, sy - 2.5f, 5f, 5f);

                            // 4 crosshair lines
                            g.DrawLine(pTick, sx - 26, sy, sx - rOuter - 2, sy);
                            g.DrawLine(pTick, sx + rOuter + 2, sy, sx + 26, sy);
                            g.DrawLine(pTick, sx, sy - 26, sx, sy - rOuter - 2);
                            g.DrawLine(pTick, sx, sy + rOuter + 2, sx, sy + 26);

                            // Target label tag next to reticle
                            string tag = $"TARGET: X={_targetRobotX:F1}, Y={_targetRobotY:F1} (R={_targetRobotRadius:F1}mm)";
                            using (Font fTag = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                            {
                                SizeF tagSize = g.MeasureString(tag, fTag);
                                float tagX = sx + 30;
                                float tagY = sy - tagSize.Height / 2;

                                if (tagX + tagSize.Width > picCameraLive.Width - 10)
                                    tagX = sx - 30 - tagSize.Width;
                                if (tagY < 10) tagY = 10;
                                if (tagY + tagSize.Height > picCameraLive.Height - 10)
                                    tagY = picCameraLive.Height - 10 - tagSize.Height;

                                using (Brush bgTag = new SolidBrush(Color.FromArgb(200, 15, 17, 26)))
                                using (Pen pTag = new Pen(crossColor, 1))
                                {
                                    g.FillRectangle(bgTag, tagX - 4, tagY - 2, tagSize.Width + 8, tagSize.Height + 4);
                                    g.DrawRectangle(pTag, tagX - 4, tagY - 2, tagSize.Width + 8, tagSize.Height + 4);
                                }
                                g.DrawString(tag, fTag, Brushes.White, tagX, tagY);
                            }
                        }
                    }
                }

                // 2. Draw Real-time Hover HUD (Tọa độ con trỏ thời gian thực)
                if (_hoverPixel.HasValue && _hoverRobotX.HasValue && _hoverRobotY.HasValue)
                {
                    string hud = $"Con trỏ: Pixel ({_hoverPixel.Value.X:F0}, {_hoverPixel.Value.Y:F0}) ➜ Robot Base: X={_hoverRobotX:F1}, Y={_hoverRobotY:F1} mm (R={_hoverRobotRadius:F1} mm)";
                    using (Font fHud = new Font("Consolas", 8.5F, FontStyle.Bold))
                    {
                        SizeF sHud = g.MeasureString(hud, fHud);
                        using (Brush bgHud = new SolidBrush(Color.FromArgb(170, 10, 12, 18)))
                        {
                            g.FillRectangle(bgHud, 10, 10, sHud.Width + 12, sHud.Height + 6);
                        }
                        g.DrawString(hud, fHud, Brushes.LightGreen, 16, 13);
                    }
                }
            };

            // Mouse events on Live PictureBox for coordinate tracking and clicking
            picCameraLive.MouseMove += (s, e) => {
                if (GetImagePixelFromMouse(e.X, e.Y, out float u, out float v))
                {
                    _hoverPixel = new PointF(u, v);
                    double workZ = 0.0;
                    if (_cameraService != null && _cameraService.PixelToRobotBase(u, v, workZ, out double rx, out double ry, out double rz))
                    {
                        _hoverRobotX = rx;
                        _hoverRobotY = ry;
                        _hoverRobotRadius = Math.Sqrt(rx * rx + ry * ry);
                    }
                    picCameraLive.Invalidate();
                }
                else
                {
                    if (_hoverPixel.HasValue)
                    {
                        _hoverPixel = null;
                        picCameraLive.Invalidate();
                    }
                }
            };

            picCameraLive.MouseLeave += (s, e) => {
                if (_hoverPixel.HasValue)
                {
                    _hoverPixel = null;
                    picCameraLive.Invalidate();
                }
            };

            picCameraLive.MouseDown += (s, e) => {
                if (e.Button != MouseButtons.Left) return;
                if (!GetImagePixelFromMouse(e.X, e.Y, out float u, out float v)) return;

                double workZ = 0.0;
                if (_cameraService != null && _cameraService.PixelToRobotBase(u, v, workZ, out double rx, out double ry, out double rz))
                {
                    double radius = Math.Sqrt(rx * rx + ry * ry);
                    bool isValid = (radius <= 300.0);

                    _selectedPixel = new PointF(u, v);
                    _targetRobotX = rx;
                    _targetRobotY = ry;
                    _targetRobotZ = rz;
                    _targetRobotRadius = radius;
                    _isTargetValidRadius = isValid;

                    UpdateTargetDisplay();
                    picCameraLive.Invalidate();

                    _logger?.Log($"[Camera Point] Đã chọn Pixel ({u:F1}, {v:F1}) ➜ Robot Base: X={rx:F2} mm, Y={ry:F2} mm (R={radius:F2} mm) - {(isValid ? "HỢP LỆ (R <= 300mm)" : "CẢNH BÁO: VƯỢT TẦM VỚI 300mm")}");
                }
            };

            pnlViewContainer.Controls.Add(picCameraLive);

            // 3. Combined Bottom Area: Readout Bar + Move Control Bar
            Panel pnlBottomArea = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 126,
                BackColor = Color.White
            };

            // 3A. Robot Position Readout (TCP · WORLD and JOINTS)
            pnlRobotReadout = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.White,
                Padding = new Padding(12, 6, 12, 6)
            };
            pnlRobotReadout.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(221, 228, 237), 1), 0, 0, pnlRobotReadout.Width, 0);
            };

            lblReadoutWorldTitle = new Label
            {
                Text = "TCP · WORLD",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(76, 91, 111),
                AutoSize = true
            };
            pnlRobotReadout.Controls.Add(lblReadoutWorldTitle);

            lblReadoutJointTitle = new Label
            {
                Text = "JOINTS",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(76, 91, 111),
                AutoSize = true
            };
            pnlRobotReadout.Controls.Add(lblReadoutJointTitle);

            string[] wNames = { "X (mm)", "Y (mm)", "Z (mm)", "C (°)" };
            worldHeaders = new Label[4];
            worldValues = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                worldHeaders[i] = new Label
                {
                    Text = wNames[i],
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = Color.FromArgb(111, 124, 142),
                    AutoSize = true
                };
                worldValues[i] = new Label
                {
                    Text = "—",
                    Font = new Font("Consolas", 11.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(38, 54, 77),
                    AutoSize = true
                };
                pnlRobotReadout.Controls.Add(worldHeaders[i]);
                pnlRobotReadout.Controls.Add(worldValues[i]);
            }

            string[] jNames = { "J1 (°)", "J2 (°)", "J3 (mm)", "J4 (°)" };
            jointHeaders = new Label[4];
            jointValues = new Label[4];
            for (int i = 0; i < 4; i++)
            {
                jointHeaders[i] = new Label
                {
                    Text = jNames[i],
                    Font = new Font("Segoe UI", 8F),
                    ForeColor = Color.FromArgb(111, 124, 142),
                    AutoSize = true
                };
                jointValues[i] = new Label
                {
                    Text = "—",
                    Font = new Font("Consolas", 11.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(38, 54, 77),
                    AutoSize = true
                };
                pnlRobotReadout.Controls.Add(jointHeaders[i]);
                pnlRobotReadout.Controls.Add(jointValues[i]);
            }

            lblCameraRobotStatus = new Label
            {
                Text = "OFFLINE · Chưa có dữ liệu",
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(113, 125, 143),
                AutoSize = true
            };
            pnlRobotReadout.Controls.Add(lblCameraRobotStatus);

            Action arrangeReadout = () => {
                int w = pnlRobotReadout.ClientSize.Width;
                if (w < 100) return;
                int padLeft = 12;
                int midGap = 24;
                int availW = w - padLeft * 2 - midGap;
                int groupW = Math.Max(200, availW / 2);

                lblReadoutWorldTitle.Location = new Point(padLeft, 5);
                int colW1 = groupW / 4;
                for (int i = 0; i < 4; i++)
                {
                    int cx = padLeft + i * colW1;
                    worldHeaders[i].Location = new Point(cx, 21);
                    worldValues[i].Location = new Point(cx, 34);
                }

                int rightStartX = padLeft + groupW + midGap;
                lblReadoutJointTitle.Location = new Point(rightStartX, 5);
                int colW2 = groupW / 4;
                for (int i = 0; i < 4; i++)
                {
                    int cx = rightStartX + i * colW2;
                    jointHeaders[i].Location = new Point(cx, 21);
                    jointValues[i].Location = new Point(cx, 34);
                }

                lblCameraRobotStatus.Location = new Point(padLeft, 53);
            };
            pnlRobotReadout.Resize += (s, e) => arrangeReadout();

            // 3B. Bottom Target & Move Control Bar (Clean 56px Bar)
            pnlBottomControl = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 56,
                BackColor = Color.White,
                Padding = new Padding(12, 8, 12, 8)
            };
            pnlBottomControl.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, 0, pnlBottomControl.Width, 0);
            };

            // Target Point Coordinates & Status Badge
            Label lblTargetIcon = new Label
            {
                Text = "🎯 ĐIỂM CHỌN:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = primaryColor,
                Location = new Point(12, 18),
                AutoSize = true
            };

            lblTargetCoords = new Label
            {
                Text = "Chưa chọn điểm nào (Click chuột lên khung hình camera để lấy tọa độ)",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(108, 117, 125),
                Location = new Point(115, 18),
                AutoSize = true
            };

            lblTargetStatus = new Label
            {
                Text = " CHƯA CHỌN ",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 110, 120),
                BackColor = Color.FromArgb(235, 238, 242),
                Location = new Point(540, 16),
                Padding = new Padding(4, 2, 4, 2),
                AutoSize = true
            };

            btnClearTarget = new Button
            {
                Text = "✖ Xóa điểm",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(108, 117, 125),
                BackColor = Color.FromArgb(245, 247, 250),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(85, 26),
                Cursor = Cursors.Hand
            };
            btnClearTarget.FlatAppearance.BorderColor = borderColor;
            btnClearTarget.Click += (s, e) => {
                _selectedPixel = null;
                _targetRobotX = null;
                _targetRobotY = null;
                _targetRobotZ = null;
                _targetRobotRadius = null;
                _isTargetValidRadius = false;
                UpdateTargetDisplay();
                picCameraLive.Invalidate();
            };

            Label lblSpeed = new Label
            {
                Text = "Tốc độ:",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = darkText,
                AutoSize = true
            };

            tbCameraMoveSpeed = new TrackBar
            {
                Minimum = 1,
                Maximum = 100,
                Value = 10,
                Width = 90,
                TickStyle = TickStyle.None
            };

            lblCameraMoveSpeedVal = new Label
            {
                Text = "10%",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = primaryColor,
                AutoSize = true
            };
            tbCameraMoveSpeed.Scroll += (s, e) => {
                lblCameraMoveSpeedVal.Text = tbCameraMoveSpeed.Value + "%";
            };

            btnMoveRobotToTarget = new Button
            {
                Text = "► DI CHUYỂN ROBOT TỚI ĐIỂM",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(215, 34),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnMoveRobotToTarget.FlatAppearance.BorderSize = 0;

            btnStopCameraMove = new Button
            {
                Text = "⏹ STOP",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(198, 40, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(75, 34),
                Cursor = Cursors.Hand,
                Enabled = true
            };
            btnStopCameraMove.FlatAppearance.BorderSize = 0;

            // Responsive layout for bottom bar
            Action updateBottomLayout = () => {
                int w = pnlBottomControl.ClientSize.Width;
                int rightMargin = 12;
                btnStopCameraMove.Location = new Point(w - rightMargin - btnStopCameraMove.Width, 11);
                btnMoveRobotToTarget.Location = new Point(btnStopCameraMove.Left - 8 - btnMoveRobotToTarget.Width, 11);
                lblCameraMoveSpeedVal.Location = new Point(btnMoveRobotToTarget.Left - 8 - lblCameraMoveSpeedVal.Width, 18);
                tbCameraMoveSpeed.Location = new Point(lblCameraMoveSpeedVal.Left - 4 - tbCameraMoveSpeed.Width, 13);
                lblSpeed.Location = new Point(tbCameraMoveSpeed.Left - 4 - lblSpeed.Width, 18);
                btnClearTarget.Location = new Point(lblSpeed.Left - 16 - btnClearTarget.Width, 15);
                lblTargetStatus.Location = new Point(btnClearTarget.Left - 10 - lblTargetStatus.Width, 16);
            };
            pnlBottomControl.Resize += (s, e) => updateBottomLayout();

            // Wire up Robot Move Execution
            btnMoveRobotToTarget.Click += async (s, e) => {
                if (_selectedPixel == null || !_targetRobotX.HasValue || !_targetRobotY.HasValue)
                {
                    MessageBox.Show("Vui lòng click chọn một điểm trên khung hình camera trước!", "Chưa chọn điểm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!_isTargetValidRadius)
                {
                    MessageBox.Show($"Điểm đã chọn có bán kính R = {_targetRobotRadius:F1} mm, vượt quá giới hạn làm việc 300.0 mm của Robot THL300!\nVui lòng chọn lại điểm khác trong tầm với của robot.", "Vượt bán kính an toàn", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!_robotService.State.IsConnected)
                {
                    MessageBox.Show("Robot chưa kết nối! Vui lòng nhấn CONNECT và bật SERVO ON trên bảng điều khiển trước khi di chuyển.", "Robot chưa kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!_robotService.State.IsServoOn)
                {
                    MessageBox.Show("Servo đang TẮT! Vui lòng bật SERVO ON trước khi điều khiển robot di chuyển.", "Servo OFF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                double rx = _targetRobotX.Value;
                double ry = _targetRobotY.Value;
                int speed = tbCameraMoveSpeed.Value;

                string confirmMsg = $"Xác nhận di chuyển Robot THL300 tới tọa độ đích:\n\n" +
                                   $" • Robot Base: X = {rx:F2} mm, Y = {ry:F2} mm\n" +
                                   $" • Bán kính: R = {_targetRobotRadius:F2} mm (Giới hạn: ≤ 300 mm)\n" +
                                   $" • Tốc độ: {speed} %\n\n" +
                                   $"Bạn có chắc chắn muốn thực thi chuyển động này?";

                var confirmResult = MessageBox.Show(confirmMsg, "Xác nhận di chuyển Robot", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirmResult != DialogResult.Yes) return;

                try
                {
                    _isCameraMoving = true;
                    btnMoveRobotToTarget.Enabled = false;

                    _logger?.Log($"[Camera Move] Bắt đầu di chuyển Robot tới X={rx:F2}, Y={ry:F2} mm ở tốc độ {speed}%...");

                    await Task.Run(async () => {
                        _robotService.EnsureWatchdogRunning();
                        _robotService.SetSpeed(speed);

                        var curPos = _robotService.GetCurrentPosition()?.WorldPosition;
                        double curZ = curPos?.Z ?? 0.0;
                        double curC = curPos?.C ?? 0.0;

                        _robotService.MoveCartesian(rx, ry, curZ, curC);
                        await WaitUntilReachPosition(rx, ry, curZ, 3.0);
                    });

                    _logger?.Log($"[Camera Move] Hoàn thành di chuyển tới điểm đích: ({rx:F2}, {ry:F2}) mm.");
                }
                catch (Exception ex)
                {
                    _logger?.Log($"[Camera Move] Lỗi khi di chuyển: {ex.Message}");
                    MessageBox.Show("Lỗi di chuyển: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _isCameraMoving = false;
                    if (!IsDisposed && !Disposing)
                    {
                        btnMoveRobotToTarget.Enabled = _isTargetValidRadius;
                    }
                }
            };

            btnStopCameraMove.Click += async (s, e) => {
                _isCameraMoving = false;
                await Task.Run(() => _robotService.StopMotion());
                _logger?.Log("[Camera Move] Đã gửi lệnh STOP dừng khẩn cấp chuyển động robot.");
            };

            pnlBottomControl.Controls.Add(lblTargetIcon);
            pnlBottomControl.Controls.Add(lblTargetCoords);
            pnlBottomControl.Controls.Add(lblTargetStatus);
            pnlBottomControl.Controls.Add(btnClearTarget);
            pnlBottomControl.Controls.Add(lblSpeed);
            pnlBottomControl.Controls.Add(tbCameraMoveSpeed);
            pnlBottomControl.Controls.Add(lblCameraMoveSpeedVal);
            pnlBottomControl.Controls.Add(btnMoveRobotToTarget);
            pnlBottomControl.Controls.Add(btnStopCameraMove);

            updateBottomLayout();

            // Add Readout and Control bar to Bottom Area
            pnlBottomArea.Controls.Add(pnlBottomControl); // Fill
            pnlBottomArea.Controls.Add(pnlRobotReadout);   // Top

            // Add containers in correct docking order
            workspaceCamera.Controls.Add(pnlViewContainer);   // Fill
            workspaceCamera.Controls.Add(pnlBottomArea);       // Bottom
            workspaceCamera.Controls.Add(pnlTopBar);          // Top

            // Initial readout layout & state sync
            arrangeReadout();
            UpdateCameraReadouts();
            if (_robotService != null && _robotService.State.IsConnected)
            {
                SetCameraConnectionState(true);
                var curPos = _robotService.GetCurrentPosition();
                if (curPos != null)
                {
                    ApplyCameraFeedback(curPos);
                }
            }

            // Refresh camera devices list on initialization
            PopulateCameraDevices();

            // Handle clean stop when form closes
            this.FormClosing += (s, e) => {
                StopCameraLive();
            };

            return workspaceCamera;
        }

        private void UpdateTargetDisplay()
        {
            if (!_selectedPixel.HasValue || !_targetRobotX.HasValue || !_targetRobotY.HasValue)
            {
                lblTargetCoords.Text = "Chưa chọn điểm nào (Click chuột lên khung hình camera để lấy tọa độ)";
                lblTargetCoords.ForeColor = Color.FromArgb(108, 117, 125);
                lblTargetStatus.Text = " CHƯA CHỌN ";
                lblTargetStatus.BackColor = Color.FromArgb(235, 238, 242);
                lblTargetStatus.ForeColor = Color.FromArgb(100, 110, 120);
                btnMoveRobotToTarget.Enabled = false;
                return;
            }

            lblTargetCoords.Text = $"Pixel: ({_selectedPixel.Value.X:F0}, {_selectedPixel.Value.Y:F0})  |  Robot Base: X = {_targetRobotX:F1} mm,  Y = {_targetRobotY:F1} mm  |  Bán kính: R = {_targetRobotRadius:F1} mm";
            lblTargetCoords.ForeColor = Color.FromArgb(33, 37, 41);

            if (_isTargetValidRadius)
            {
                lblTargetStatus.Text = " ✔ TRONG TẦM VỚI (R ≤ 300mm) ";
                lblTargetStatus.BackColor = Color.FromArgb(232, 245, 233);
                lblTargetStatus.ForeColor = Color.FromArgb(46, 125, 50);
                btnMoveRobotToTarget.Enabled = !_isCameraMoving;
            }
            else
            {
                lblTargetStatus.Text = " ✖ VƯỢT TẦM VỚI (R > 300mm) ";
                lblTargetStatus.BackColor = Color.FromArgb(255, 235, 238);
                lblTargetStatus.ForeColor = Color.FromArgb(198, 40, 40);
                btnMoveRobotToTarget.Enabled = false;
            }
        }

        private bool GetImagePixelFromMouse(int mouseX, int mouseY, out float u, out float v)
        {
            u = 0; v = 0;
            if (picCameraLive == null || picCameraLive.Image == null) return false;

            int imgW = picCameraLive.Image.Width;
            int imgH = picCameraLive.Image.Height;
            int pbW = picCameraLive.ClientSize.Width;
            int pbH = picCameraLive.ClientSize.Height;

            if (imgW <= 0 || imgH <= 0 || pbW <= 0 || pbH <= 0) return false;

            float imgAspect = (float)imgW / imgH;
            float pbAspect = (float)pbW / pbH;

            float drawW, drawH, drawX, drawY;

            if (pbAspect > imgAspect)
            {
                drawH = pbH;
                drawW = pbH * imgAspect;
                drawX = (pbW - drawW) / 2f;
                drawY = 0f;
            }
            else
            {
                drawW = pbW;
                drawH = pbW / imgAspect;
                drawX = 0f;
                drawY = (pbH - drawH) / 2f;
            }

            if (mouseX < drawX || mouseX > drawX + drawW || mouseY < drawY || mouseY > drawY + drawH)
            {
                return false;
            }

            u = (mouseX - drawX) * (imgW / drawW);
            v = (mouseY - drawY) * (imgH / drawH);
            return true;
        }

        private bool GetScreenPointFromImagePixel(float u, float v, out float screenX, out float screenY)
        {
            screenX = 0; screenY = 0;
            if (picCameraLive == null || picCameraLive.Image == null) return false;

            int imgW = picCameraLive.Image.Width;
            int imgH = picCameraLive.Image.Height;
            int pbW = picCameraLive.ClientSize.Width;
            int pbH = picCameraLive.ClientSize.Height;

            if (imgW <= 0 || imgH <= 0 || pbW <= 0 || pbH <= 0) return false;

            float imgAspect = (float)imgW / imgH;
            float pbAspect = (float)pbW / pbH;

            float drawW, drawH, drawX, drawY;

            if (pbAspect > imgAspect)
            {
                drawH = pbH;
                drawW = pbH * imgAspect;
                drawX = (pbW - drawW) / 2f;
                drawY = 0f;
            }
            else
            {
                drawW = pbW;
                drawH = pbW / imgAspect;
                drawX = 0f;
                drawY = (pbH - drawH) / 2f;
            }

            screenX = drawX + u * (drawW / imgW);
            screenY = drawY + v * (drawH / imgH);
            return true;
        }

        private async Task WaitUntilReachPosition(double targetX, double targetY, double targetZ, double toleranceMm = 3.0)
        {
            int timeoutMs = 15000;
            int elapsedMs = 0;
            while (elapsedMs < timeoutMs && _isCameraMoving)
            {
                var pos = _robotService.GetCurrentPosition()?.WorldPosition;
                if (pos != null)
                {
                    double dx = pos.X - targetX;
                    double dy = pos.Y - targetY;
                    double dz = pos.Z - targetZ;
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dist <= toleranceMm)
                    {
                        break;
                    }
                }
                await Task.Delay(50);
                elapsedMs += 50;
            }
        }

        private void PopulateCameraDevices()
        {
            try
            {
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                cmbCameraDevices.Items.Clear();

                if (_videoDevices.Count == 0)
                {
                    cmbCameraDevices.Items.Add("Không tìm thấy Camera!");
                    cmbCameraDevices.SelectedIndex = 0;
                    btnToggleCamera.Enabled = false;
                    return;
                }

                int preferredIndex = 0;
                for (int i = 0; i < _videoDevices.Count; i++)
                {
                    string name = _videoDevices[i].Name;
                    cmbCameraDevices.Items.Add(name);

                    // Ưu tiên chọn Logitech Brio 100 nếu có
                    if (name.ToLower().Contains("brio") || name.ToLower().Contains("logitech"))
                    {
                        preferredIndex = i;
                    }
                }

                cmbCameraDevices.SelectedIndex = preferredIndex;
                btnToggleCamera.Enabled = true;
            }
            catch (Exception ex)
            {
                _logger?.Log($"[Camera] Error detecting devices: {ex.Message}");
            }
        }

        private void UpdateResolutionList()
        {
            cmbResolutions.Items.Clear();
            if (_videoDevices == null || cmbCameraDevices.SelectedIndex < 0 || cmbCameraDevices.SelectedIndex >= _videoDevices.Count)
                return;

            try
            {
                string moniker = _videoDevices[cmbCameraDevices.SelectedIndex].MonikerString;
                var tempSource = new VideoCaptureDevice(moniker);

                int preferredResIdx = 0;
                for (int i = 0; i < tempSource.VideoCapabilities.Length; i++)
                {
                    var cap = tempSource.VideoCapabilities[i];
                    string resText = $"{cap.FrameSize.Width} x {cap.FrameSize.Height}";
                    if (!cmbResolutions.Items.Contains(resText))
                    {
                        cmbResolutions.Items.Add(resText);
                        // Ưu tiên chọn 1280x720 (đúng độ phân giải hiệu chuẩn của Brio 100)
                        if (cap.FrameSize.Width == 1280 && cap.FrameSize.Height == 720)
                        {
                            preferredResIdx = cmbResolutions.Items.Count - 1;
                        }
                    }
                }

                if (cmbResolutions.Items.Count > 0)
                {
                    cmbResolutions.SelectedIndex = preferredResIdx;
                }
            }
            catch { }
        }

        private void ToggleCameraLive()
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                StopCameraLive();
            }
            else
            {
                StartCameraLive();
            }
        }

        private void StartCameraLive()
        {
            if (_videoDevices == null || cmbCameraDevices.SelectedIndex < 0 || cmbCameraDevices.SelectedIndex >= _videoDevices.Count)
                return;

            try
            {
                // 1. Tự động nạp bộ thông số hiệu chuẩn camera mới nhất mỗi khi bật camera
                _cameraService?.ReloadCalibration();
                var calib = _cameraService?.Config ?? new CameraCalibrationConfig();

                // 2. Ưu tiên tự động chọn độ phân giải chuẩn của calibration (1280 x 720)
                for (int i = 0; i < cmbResolutions.Items.Count; i++)
                {
                    string resStr = cmbResolutions.Items[i].ToString();
                    if (resStr.Contains(calib.ImageWidth.ToString()) && resStr.Contains(calib.ImageHeight.ToString()))
                    {
                        cmbResolutions.SelectedIndex = i;
                        break;
                    }
                }

                string moniker = _videoDevices[cmbCameraDevices.SelectedIndex].MonikerString;
                _videoSource = new VideoCaptureDevice(moniker);

                // Thiết lập độ phân giải mong muốn
                if (cmbResolutions.SelectedItem != null)
                {
                    string selectedRes = cmbResolutions.SelectedItem.ToString();
                    foreach (var cap in _videoSource.VideoCapabilities)
                    {
                        if ($"{cap.FrameSize.Width} x {cap.FrameSize.Height}" == selectedRes)
                        {
                            _videoSource.VideoResolution = cap;
                            break;
                        }
                    }
                }

                _videoSource.NewFrame += VideoSource_NewFrame;
                _videoSource.Start();

                btnToggleCamera.Text = "⏹ TẮT CAMERA";
                btnToggleCamera.BackColor = Color.FromArgb(220, 53, 69);
                cmbCameraDevices.Enabled = false;
                cmbResolutions.Enabled = false;

                _logger?.Log($"[Camera] Live started using calibrated intrinsics: fx={calib.Fx:F2}, fy={calib.Fy:F2}, cx={calib.Cx:F2}, cy={calib.Cy:F2}, RMS={calib.RmsReprojectionPx:F4}px ({cmbResolutions.SelectedItem})");
            }
            catch (Exception ex)
            {
                _logger?.Log($"[Camera] Failed to start camera: {ex.Message}");
                MessageBox.Show("Không thể khởi động camera: " + ex.Message, "Camera Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StopCameraLive();
            }
        }

        private void StopCameraLive()
        {
            try
            {
                if (_videoSource != null)
                {
                    if (_videoSource.IsRunning)
                    {
                        _videoSource.SignalToStop();
                        _videoSource.WaitForStop();
                    }
                    _videoSource.NewFrame -= VideoSource_NewFrame;
                    _videoSource = null;
                }

                if (picCameraLive != null && !picCameraLive.IsDisposed)
                {
                    var oldImg = picCameraLive.Image;
                    picCameraLive.Image = null;
                    oldImg?.Dispose();
                    picCameraLive.Invalidate();
                }

                btnToggleCamera.Text = "▶ BẬT CAMERA (LIVE)";
                btnToggleCamera.BackColor = Color.FromArgb(40, 167, 69);
                cmbCameraDevices.Enabled = true;
                cmbResolutions.Enabled = true;

                _logger?.Log("[Camera] Stopped live camera.");
            }
            catch (Exception ex)
            {
                _logger?.Log($"[Camera] Error stopping camera: {ex.Message}");
            }
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                Bitmap displayFrame = (Bitmap)eventArgs.Frame.Clone();

                if (picCameraLive != null && !picCameraLive.IsDisposed && picCameraLive.IsHandleCreated)
                {
                    picCameraLive.BeginInvoke(new Action(() => {
                        try
                        {
                            var old = picCameraLive.Image;
                            picCameraLive.Image = displayFrame;
                            old?.Dispose();
                        }
                        catch { }
                    }));
                }
                else
                {
                    displayFrame.Dispose();
                }
            }
            catch { }
        }

        public void SetCameraConnectionState(bool connected)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => SetCameraConnectionState(connected))); } catch { }
                return;
            }

            _cameraConnected = connected;
            _cameraStale = true;
            _cameraWaitingForFeedback = connected;
            UpdateCameraReadouts();
        }

        public void SetCameraFeedbackUnavailable()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(SetCameraFeedbackUnavailable)); } catch { }
                return;
            }

            _cameraStale = true;
            _cameraWaitingForFeedback = false;
            UpdateCameraReadouts();
        }

        public void ApplyCameraFeedback(RobotPositionData position)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => ApplyCameraFeedback(position))); } catch { }
                return;
            }

            if (position == null || position.JointPosition == null || position.WorldPosition == null)
            {
                SetCameraFeedbackUnavailable();
                return;
            }

            _cameraLastWorld[0] = position.WorldPosition.X;
            _cameraLastWorld[1] = position.WorldPosition.Y;
            _cameraLastWorld[2] = position.WorldPosition.Z;
            _cameraLastWorld[3] = position.WorldPosition.C;

            _cameraLastJoints[0] = position.JointPosition.J1;
            _cameraLastJoints[1] = position.JointPosition.J2;
            _cameraLastJoints[2] = position.JointPosition.J3;
            _cameraLastJoints[3] = position.JointPosition.J4;

            _cameraHasFeedback = true;
            _cameraStale = false;
            _cameraWaitingForFeedback = false;
            UpdateCameraReadouts();
        }

        private void UpdateCameraReadouts()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(UpdateCameraReadouts)); } catch { }
                return;
            }

            if (worldValues == null || jointValues == null || lblCameraRobotStatus == null) return;

            for (int i = 0; i < 4; i++)
            {
                if (_cameraHasFeedback)
                {
                    worldValues[i].Text = _cameraLastWorld[i].ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    jointValues[i].Text = _cameraLastJoints[i].ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    worldValues[i].ForeColor = (_cameraConnected && !_cameraStale) ? Color.FromArgb(38, 54, 77) : Color.FromArgb(140, 150, 165);
                    jointValues[i].ForeColor = (_cameraConnected && !_cameraStale) ? Color.FromArgb(38, 54, 77) : Color.FromArgb(140, 150, 165);
                }
                else
                {
                    worldValues[i].Text = "—";
                    jointValues[i].Text = "—";
                    worldValues[i].ForeColor = Color.FromArgb(140, 150, 165);
                    jointValues[i].ForeColor = Color.FromArgb(140, 150, 165);
                }
            }

            string state = !_cameraConnected ? "OFFLINE" : _cameraHasFeedback && !_cameraStale ? "LIVE" : _cameraWaitingForFeedback ? "WAITING" : "STALE";
            string detail = state == "LIVE" ? "Đang cập nhật" : state == "WAITING" ? "Đang chờ phản hồi" :
                state == "STALE" ? (_cameraHasFeedback ? "Dữ liệu cũ · Mất phản hồi" : "Chưa nhận được dữ liệu") :
                (_cameraHasFeedback ? "Mất kết nối · Giữ giá trị cuối" : "Chưa có dữ liệu");

            lblCameraRobotStatus.Text = state + " · " + detail;
            lblCameraRobotStatus.ForeColor = state == "LIVE" ? Color.FromArgb(38, 132, 90) :
                state == "STALE" ? Color.FromArgb(175, 111, 31) : Color.FromArgb(113, 125, 143);
        }
    }
}
