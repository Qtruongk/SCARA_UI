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
using Test_1.Services;

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
        private Button btnDrawX;
        private Button btnDrawO;
        private Button btnClearSymbol;
        private bool _isCameraMoving = false;
        private Label lblAutoConfigBadge;
        private Button btnFourPointCalib;
        private FourPointCalibrationDialog _fourPointDialog = null;
        private bool _showCalibOverlay = true;

        // Grid Scanner Controls
        private readonly GridScannerService _gridService = new GridScannerService();
        private Button btnToggleGrid;
        private Button btnGridSettings;
        private Button btnSnapGrid;

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

            // 1. Top Control Bar (2-Row Responsive Layout to avoid getting cut off on various screen sizes)
            Panel pnlTopBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.White,
                Padding = new Padding(12, 8, 12, 8)
            };
            pnlTopBar.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(borderColor, 1), 0, pnlTopBar.Height - 1, pnlTopBar.Width, pnlTopBar.Height - 1);
            };

            // --- HÀNG 1: Thiết bị Camera & Độ phân giải & Bật/Tắt Live ---
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
                Location = new Point(70, 14),
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
                Location = new Point(252, 17),
                AutoSize = true
            };

            cmbResolutions = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(334, 14),
                Width = 105
            };

            btnToggleCamera = new Button
            {
                Text = "Bật Camera",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(22, 101, 52),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(452, 12),
                Size = new Size(110, 30),
                Cursor = Cursors.Hand
            };
            btnToggleCamera.FlatAppearance.BorderSize = 0;
            btnToggleCamera.Click += (s, e) => {
                ToggleCameraLive();
            };

            lblAutoConfigBadge = new Label
            {
                Text = "Đồng bộ Calib: Bật",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(575, 18),
                AutoSize = true
            };

            // --- HÀNG 2: Các công cụ Hiệu chuẩn & Lưới ô cờ ---
            btnFourPointCalib = new Button
            {
                Text = "Hiệu chuẩn 4 điểm",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(12, 50),
                Size = new Size(140, 30),
                Cursor = Cursors.Hand
            };
            btnFourPointCalib.FlatAppearance.BorderSize = 0;
            btnFourPointCalib.Click += (s, e) => {
                OpenFourPointCalibrationDialog();
            };

            btnToggleGrid = new Button
            {
                Text = _gridService.IsGridEnabled ? "Lưới: BẬT" : "Lưới: TẮT",
                Font = new Font("Segoe UI", 9F),
                BackColor = _gridService.IsGridEnabled ? Color.FromArgb(241, 245, 249) : Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(30, 41, 59),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(158, 50),
                Size = new Size(95, 30),
                Cursor = Cursors.Hand
            };
            btnToggleGrid.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnToggleGrid.Click += (s, e) => {
                _gridService.IsGridEnabled = !_gridService.IsGridEnabled;
                btnToggleGrid.Text = _gridService.IsGridEnabled ? "Lưới: BẬT" : "Lưới: TẮT";
                btnToggleGrid.BackColor = _gridService.IsGridEnabled ? Color.FromArgb(241, 245, 249) : Color.FromArgb(248, 250, 252);
                picCameraLive?.Invalidate();
            };

            btnGridSettings = new Button
            {
                Text = "Cài đặt lưới",
                Font = new Font("Segoe UI", 9F),
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(51, 65, 85),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(259, 50),
                Size = new Size(95, 30),
                Cursor = Cursors.Hand
            };
            btnGridSettings.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnGridSettings.Click += (s, e) => {
                ShowGridConfigDialog();
            };

            btnSnapGrid = new Button
            {
                Text = "Khớp nét ô",
                Font = new Font("Segoe UI", 9F),
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(51, 65, 85),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(360, 50),
                Size = new Size(100, 30),
                Cursor = Cursors.Hand
            };
            btnSnapGrid.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnSnapGrid.Click += (s, e) => {
                Bitmap frame = picCameraLive?.Image as Bitmap;
                UpdateGridFromCalibration(frame);
                if (_gridService.IsVisionSnapped)
                {
                    btnSnapGrid.Text = "Đã khớp nét";
                    btnSnapGrid.ForeColor = Color.FromArgb(22, 101, 52);
                    btnSnapGrid.FlatAppearance.BorderColor = Color.FromArgb(187, 247, 208);
                    _logger?.Log($"[Vision Grid] {_gridService.SnapStatusMessage}");
                    MessageBox.Show($"Xác định biên thành công:\n\n" +
                                    $"• Số đường khớp: {_gridService.SnappedLineCount}/{_gridService.TotalLineCount}\n" +
                                    $"• Sai số trung bình: {_gridService.AverageResidualPx:F2} px\n" +
                                    $"• Đã khóa biên các ô theo nét kẻ thực tế.",
                                    "Khớp nét ô", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    btnSnapGrid.Text = "Khớp nét ô";
                    btnSnapGrid.ForeColor = Color.FromArgb(51, 65, 85);
                    btnSnapGrid.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                    _logger?.Log($"[Vision Grid] {_gridService.SnapStatusMessage}");
                    MessageBox.Show($"Không nhận diện được nét kẻ rõ ràng.\nTiếp tục sử dụng lưới nội suy hình học.",
                                    "Khớp nét ô", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            pnlTopBar.Controls.Add(lblDev);
            pnlTopBar.Controls.Add(cmbCameraDevices);
            pnlTopBar.Controls.Add(lblRes);
            pnlTopBar.Controls.Add(cmbResolutions);
            pnlTopBar.Controls.Add(btnToggleCamera);
            pnlTopBar.Controls.Add(lblAutoConfigBadge);
            pnlTopBar.Controls.Add(btnFourPointCalib);
            pnlTopBar.Controls.Add(btnToggleGrid);
            pnlTopBar.Controls.Add(btnGridSettings);
            pnlTopBar.Controls.Add(btnSnapGrid);

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

                // 1. Draw Selected Target Reticle (Hồng tâm mục tiêu khi chọn điểm tự do)
                if (_selectedPixel.HasValue && _targetRobotX.HasValue && _targetRobotY.HasValue && _gridService.SelectedCell == null)
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

                // 2. Draw 4-Point Homography Calibration Overlay (nếu có và bật hiển thị)
                if (_showCalibOverlay)
                {
                    PointF[] calibPixels = _fourPointDialog?.GetCurrentPixelPoints() ?? _cameraService?.Config?.CalibPixelPoints;
                    PointF[] calibRobots = _fourPointDialog?.GetCurrentRobotPoints() ?? _cameraService?.Config?.CalibRobotPoints;

                    Color[] pColors = {
                        Color.FromArgb(220, 53, 69),   // P1 Đỏ
                        Color.FromArgb(0, 123, 255),  // P2 Xanh dương
                        Color.FromArgb(111, 66, 193), // P3 Tím
                        Color.FromArgb(40, 167, 69)   // P4 Xanh lá
                    };

                    if (calibPixels != null)
                    {
                        PointF[] screenPoints = new PointF[4];
                        int validCount = 0;

                        for (int i = 0; i < 4; i++)
                        {
                            if (i < calibPixels.Length && calibPixels[i] != PointF.Empty && calibPixels[i].X > 0 && calibPixels[i].Y > 0)
                            {
                                if (GetScreenPointFromImagePixel(calibPixels[i].X, calibPixels[i].Y, out float sx, out float sy))
                                {
                                    screenPoints[i] = new PointF(sx, sy);
                                    validCount++;

                                    // Vẽ marker mốc P[i]
                                    Color c = pColors[i];
                                    using (Brush bPoint = new SolidBrush(c))
                                    using (Pen pCircle = new Pen(Color.White, 2f))
                                    using (Font fP = new Font("Segoe UI", 8F, FontStyle.Bold))
                                    {
                                        g.FillEllipse(bPoint, sx - 6, sy - 6, 12, 12);
                                        g.DrawEllipse(pCircle, sx - 6, sy - 6, 12, 12);

                                        string pName = $"P{i + 1}";
                                        if (calibRobots != null && i < calibRobots.Length && calibRobots[i] != PointF.Empty)
                                        {
                                            pName += $" ({calibRobots[i].X:F0}, {calibRobots[i].Y:F0})";
                                        }
                                        SizeF sName = g.MeasureString(pName, fP);
                                        using (Brush bBg = new SolidBrush(Color.FromArgb(210, 15, 17, 26)))
                                        using (Pen pBorder = new Pen(c, 1))
                                        {
                                            g.FillRectangle(bBg, sx + 8, sy - sName.Height / 2, sName.Width + 4, sName.Height);
                                            g.DrawRectangle(pBorder, sx + 8, sy - sName.Height / 2, sName.Width + 4, sName.Height);
                                        }
                                        g.DrawString(pName, fP, Brushes.White, sx + 10, sy - sName.Height / 2);
                                    }
                                }
                            }
                        }

                        // Nếu có đủ 4 điểm trên màn hình, vẽ đa giác bao nối 4 điểm
                        if (validCount == 4)
                        {
                            using (Pen pPoly = new Pen(Color.FromArgb(180, 0, 229, 255), 1.5f) { DashStyle = DashStyle.Dash })
                            using (Brush bPoly = new SolidBrush(Color.FromArgb(25, 0, 229, 255)))
                            {
                                g.FillPolygon(bPoly, screenPoints);
                                g.DrawPolygon(pPoly, screenPoints);
                            }
                        }
                    }

                    // Hiển thị Banner nếu đang ở chế độ picking điểm từ Dialog
                    if (_fourPointDialog != null && !_fourPointDialog.IsDisposed && _fourPointDialog.ActivePickingPointIndex >= 0)
                    {
                        int pickingIdx = _fourPointDialog.ActivePickingPointIndex;
                        string pickMsg = $"Chế độ chọn điểm hiệu chuẩn: Nhấp chuột trên ảnh để xác định tọa độ Điểm P{pickingIdx + 1}";
                        using (Font fPick = new Font("Segoe UI", 9.5F, FontStyle.Bold))
                        {
                            SizeF sPick = g.MeasureString(pickMsg, fPick);
                            float bannerX = (picCameraLive.Width - sPick.Width - 24) / 2f;
                            using (Brush bBg = new SolidBrush(Color.FromArgb(235, 217, 83, 79)))
                            using (Pen pBorder = new Pen(Color.White, 1.5f))
                            {
                                g.FillRectangle(bBg, bannerX, 10, sPick.Width + 24, sPick.Height + 10);
                                g.DrawRectangle(pBorder, bannerX, 10, sPick.Width + 24, sPick.Height + 10);
                            }
                            g.DrawString(pickMsg, fPick, Brushes.White, bannerX + 12, 15);
                        }
                    }
                }

                // 3. Draw Perspective Grid Ô Vuông (Quét và vẽ các ô vuông trong phạm vi 4 điểm)
                _gridService.DrawGrid(g, (u, v) => GetScreenPointFromImagePixel(u, v, out float sx, out float sy) ? Tuple.Create(sx, sy) : null);

                // 4. Draw Real-time Hover HUD (Tọa độ con trỏ thời gian thực)
                if (_hoverPixel.HasValue && _hoverRobotX.HasValue && _hoverRobotY.HasValue)
                {
                    string modeTag = (_cameraService != null && _cameraService.UseHomography && _cameraService.Config.HasHomography) ? " [HOMOGRAPHY]" : "";
                    string hud = $"Con trỏ: ({_hoverPixel.Value.X:F0}, {_hoverPixel.Value.Y:F0}) px | Robot: X={_hoverRobotX:F1}, Y={_hoverRobotY:F1} mm (R={_hoverRobotRadius:F1} mm){modeTag}";
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
                    double curZ = _robotService?.GetCurrentPosition()?.WorldPosition?.Z ?? (_cameraService?.Config?.CalibratedWorkZ ?? 0.0);
                    if (_cameraService != null && _cameraService.PixelToRobotBase(u, v, curZ, out double rx, out double ry, out double rz))
                    {
                        _hoverRobotX = rx;
                        _hoverRobotY = ry;
                        _hoverRobotRadius = Math.Sqrt(rx * rx + ry * ry);
                    }

                    // Hover Grid Cell
                    var prevHover = _gridService.HoveredCell;
                    _gridService.HoveredCell = _gridService.FindCellAtPixel(u, v);
                    if (_gridService.HoveredCell != prevHover)
                    {
                        picCameraLive.Invalidate();
                    }
                    else
                    {
                        picCameraLive.Invalidate();
                    }
                }
                else
                {
                    if (_hoverPixel.HasValue || _gridService.HoveredCell != null)
                    {
                        _hoverPixel = null;
                        _gridService.HoveredCell = null;
                        picCameraLive.Invalidate();
                    }
                }
            };

            picCameraLive.MouseLeave += (s, e) => {
                bool needRepaint = _hoverPixel.HasValue || _gridService.HoveredCell != null;
                _hoverPixel = null;
                _gridService.HoveredCell = null;
                if (needRepaint)
                {
                    picCameraLive.Invalidate();
                }
            };

            picCameraLive.MouseDown += (s, e) => {
                if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
                if (!GetImagePixelFromMouse(e.X, e.Y, out float u, out float v)) return;

                // Chuột phải (Right-Click): Đổi luân phiên ký hiệu X / O / Trống trên ô vuông
                if (e.Button == MouseButtons.Right)
                {
                    if (_gridService.IsGridEnabled)
                    {
                        var clickedCell = _gridService.FindCellAtPixel(u, v);
                        if (clickedCell != null)
                        {
                            _gridService.SelectedCell = clickedCell;
                            _selectedPixel = clickedCell.CenterPixel;
                            _targetRobotX = clickedCell.CenterRobot.X;
                            _targetRobotY = clickedCell.CenterRobot.Y;
                            double r = Math.Sqrt(_targetRobotX.Value * _targetRobotX.Value + _targetRobotY.Value * _targetRobotY.Value);
                            _targetRobotRadius = r;
                            _isTargetValidRadius = (r <= 300.0);

                            // Luân phiên ký hiệu: None -> X -> O -> None
                            if (clickedCell.Symbol == GridCell.CellSymbol.None)
                                clickedCell.Symbol = GridCell.CellSymbol.X;
                            else if (clickedCell.Symbol == GridCell.CellSymbol.X)
                                clickedCell.Symbol = GridCell.CellSymbol.O;
                            else
                                clickedCell.Symbol = GridCell.CellSymbol.None;

                            string symName = clickedCell.Symbol == GridCell.CellSymbol.None ? "(Trống)" : clickedCell.Symbol.ToString();
                            lblTargetCoords.Text = $"Ô #{clickedCell.Index} (H{clickedCell.Row + 1}, C{clickedCell.Col + 1})  |  Ký hiệu: {symName}  |  Kích thước: {clickedCell.WidthMm:F1}x{clickedCell.HeightMm:F1} mm";
                            lblTargetCoords.ForeColor = Color.FromArgb(30, 41, 59);

                            lblTargetStatus.Text = _isTargetValidRadius ? "Hợp lệ (R ≤ 300 mm)" : "Vượt giới hạn (R > 300 mm)";
                            lblTargetStatus.BackColor = _isTargetValidRadius ? Color.FromArgb(240, 253, 244) : Color.FromArgb(254, 242, 242);
                            lblTargetStatus.ForeColor = _isTargetValidRadius ? Color.FromArgb(22, 101, 52) : Color.FromArgb(185, 28, 28);

                            if (btnDrawX != null) btnDrawX.Enabled = true;
                            if (btnDrawO != null) btnDrawO.Enabled = true;
                            if (btnClearSymbol != null) btnClearSymbol.Enabled = true;
                            if (btnMoveRobotToTarget != null) btnMoveRobotToTarget.Enabled = _isTargetValidRadius && !_isCameraMoving;

                            _logger?.Log($"[Grid] Đổi ký hiệu Ô #{clickedCell.Index} thành: {clickedCell.Symbol}");
                            picCameraLive.Invalidate();
                            return;
                        }
                    }
                    return;
                }

                // 1. Kiểm tra nếu đang trong chế độ chấm điểm mốc của Calib 4 Điểm
                if (_fourPointDialog != null && !_fourPointDialog.IsDisposed && _fourPointDialog.ActivePickingPointIndex >= 0)
                {
                    int activeIdx = _fourPointDialog.ActivePickingPointIndex;
                    _fourPointDialog.SetPickedPixel(activeIdx, u, v);
                    _logger?.Log($"[Calib 4 Điểm] Đã chấm Điểm P{activeIdx + 1}: Pixel ({u:F1}, {v:F1})");
                    picCameraLive.Invalidate();
                    return;
                }

                // 2. Kiểm tra nếu click chọn một ô vuông trong Lưới Grid (Khóa ranh giới ô vuông)
                if (_gridService.IsGridEnabled)
                {
                    var clickedCell = _gridService.FindCellAtPixel(u, v);
                    if (clickedCell != null)
                    {
                        _gridService.SelectedCell = clickedCell;
                        _selectedPixel = clickedCell.CenterPixel;
                        _targetRobotX = clickedCell.CenterRobot.X;
                        _targetRobotY = clickedCell.CenterRobot.Y;
                        double r = Math.Sqrt(_targetRobotX.Value * _targetRobotX.Value + _targetRobotY.Value * _targetRobotY.Value);
                        _targetRobotRadius = r;
                        _isTargetValidRadius = (r <= 300.0);

                        string symName = clickedCell.Symbol == GridCell.CellSymbol.None ? "(Trống)" : clickedCell.Symbol.ToString();
                        lblTargetCoords.Text = $"Ô #{clickedCell.Index} (H{clickedCell.Row + 1}, C{clickedCell.Col + 1})  |  Ký hiệu: {symName}  |  Robot TL: ({clickedCell.CornersRobot[0].X:F1}, {clickedCell.CornersRobot[0].Y:F1}) mm  |  Kích thước: {clickedCell.WidthMm:F1}x{clickedCell.HeightMm:F1} mm";
                        lblTargetCoords.ForeColor = Color.FromArgb(30, 41, 59);

                        lblTargetStatus.Text = _isTargetValidRadius ? "Hợp lệ (R ≤ 300 mm)" : "Vượt giới hạn (R > 300 mm)";
                        lblTargetStatus.BackColor = _isTargetValidRadius ? Color.FromArgb(240, 253, 244) : Color.FromArgb(254, 242, 242);
                        lblTargetStatus.ForeColor = _isTargetValidRadius ? Color.FromArgb(22, 101, 52) : Color.FromArgb(185, 28, 28);

                        if (btnDrawX != null) btnDrawX.Enabled = true;
                        if (btnDrawO != null) btnDrawO.Enabled = true;
                        if (btnClearSymbol != null) btnClearSymbol.Enabled = true;
                        if (btnMoveRobotToTarget != null) btnMoveRobotToTarget.Enabled = _isTargetValidRadius && !_isCameraMoving;

                        _logger?.Log($"[Grid] Đã chọn và khóa ranh giới Ô #{clickedCell.Index} (Hàng {clickedCell.Row + 1}, Cột {clickedCell.Col + 1}) - Ký hiệu: {clickedCell.Symbol} - Kích thước: {clickedCell.WidthMm:F1}x{clickedCell.HeightMm:F1} mm, 4 đỉnh góc ranh giới đã sẵn sàng.");
                        picCameraLive.Invalidate();
                        return;
                    }
                }

                // 3. Chế độ click chọn mục tiêu tự do trên ảnh
                double curZ = _robotService?.GetCurrentPosition()?.WorldPosition?.Z ?? (_cameraService?.Config?.CalibratedWorkZ ?? 0.0);
                if (_cameraService != null && _cameraService.PixelToRobotBase(u, v, curZ, out double rx, out double ry, out double rz))
                {
                    double radius = Math.Sqrt(rx * rx + ry * ry);
                    bool isValid = (radius <= 300.0);

                    _gridService.SelectedCell = null;
                    if (btnDrawX != null) btnDrawX.Enabled = false;
                    if (btnDrawO != null) btnDrawO.Enabled = false;
                    if (btnClearSymbol != null) btnClearSymbol.Enabled = false;

                    _selectedPixel = new PointF(u, v);
                    _targetRobotX = rx;
                    _targetRobotY = ry;
                    _targetRobotZ = rz;
                    _targetRobotRadius = radius;
                    _isTargetValidRadius = isValid;

                    UpdateTargetDisplay();
                    picCameraLive.Invalidate();

                    string method = (_cameraService.UseHomography && _cameraService.Config.HasHomography) ? "Homography 4-Điểm" : "Ray-Plane 3D";
                    _logger?.Log($"[Camera Point] [{method}] Đã chọn Pixel ({u:F1}, {v:F1}) ➜ Robot Base: X={rx:F2} mm, Y={ry:F2} mm (R={radius:F2} mm) - {(isValid ? "HỢP LỆ (R <= 300mm)" : "CẢNH BÁO: VƯỢT TẦM VỚI 300mm")}");
                }
            };

            pnlViewContainer.Controls.Add(picCameraLive);

            // 3. Combined Bottom Area: Readout Bar + Move Control Bar
            Panel pnlBottomArea = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 154,
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

            // Target Point Coordinates & Status Badge
            Label lblTargetIcon = new Label
            {
                Text = "Mục tiêu:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                Location = new Point(12, 12),
                AutoSize = true
            };

            lblTargetCoords = new Label
            {
                Text = "Chưa chọn mục tiêu",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(80, 12),
                AutoSize = true
            };

            lblTargetStatus = new Label
            {
                Text = "—",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.Transparent,
                Location = new Point(480, 11),
                Padding = new Padding(4, 2, 4, 2),
                AutoSize = true
            };

            btnClearTarget = new Button
            {
                Text = "Bỏ chọn",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(71, 85, 105),
                BackColor = Color.FromArgb(248, 250, 252),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(68, 26),
                Cursor = Cursors.Hand
            };
            btnClearTarget.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnClearTarget.Click += (s, e) => {
                _selectedPixel = null;
                _targetRobotX = null;
                _targetRobotY = null;
                _targetRobotZ = null;
                _targetRobotRadius = null;
                _isTargetValidRadius = false;
                _gridService.SelectedCell = null;
                if (btnDrawX != null) btnDrawX.Enabled = false;
                if (btnDrawO != null) btnDrawO.Enabled = false;
                if (btnClearSymbol != null) btnClearSymbol.Enabled = false;
                UpdateTargetDisplay();
                picCameraLive.Invalidate();
            };

            Label lblSpeed = new Label
            {
                Text = "Tốc độ:",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85),
                AutoSize = true
            };

            tbCameraMoveSpeed = new TrackBar
            {
                Minimum = 1,
                Maximum = 100,
                Value = 10,
                Width = 85,
                TickStyle = TickStyle.None
            };

            lblCameraMoveSpeedVal = new Label
            {
                Text = "10%",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                AutoSize = true
            };
            tbCameraMoveSpeed.Scroll += (s, e) => {
                lblCameraMoveSpeedVal.Text = tbCameraMoveSpeed.Value + "%";
            };

            // Khởi tạo các nút thao tác ký hiệu ô vuông & di chuyển robot
            btnDrawX = new Button
            {
                Text = "Vẽ X",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(185, 28, 28),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(75, 30),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnDrawX.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);

            btnDrawO = new Button
            {
                Text = "Vẽ O",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(22, 101, 52),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(75, 30),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnDrawO.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);

            btnClearSymbol = new Button
            {
                Text = "Xóa ký hiệu",
                Font = new Font("Segoe UI", 8.5F),
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(71, 85, 105),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(95, 30),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnClearSymbol.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);

            btnMoveRobotToTarget = new Button
            {
                Text = "Di chuyển Robot",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(130, 30),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnMoveRobotToTarget.FlatAppearance.BorderSize = 0;

            btnStopCameraMove = new Button
            {
                Text = "DỪNG KHẨN",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                BackColor = Color.FromArgb(185, 28, 28),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(95, 30),
                Cursor = Cursors.Hand,
                Enabled = true
            };
            btnStopCameraMove.FlatAppearance.BorderSize = 0;

            // Xử lý sự kiện bấm nút Vẽ Chữ X
            btnDrawX.Click += async (s, e) => {
                var cell = _gridService.SelectedCell;
                if (cell == null)
                {
                    MessageBox.Show("Vui lòng chọn một ô vuông trong lưới trước khi thực hiện.", "Chưa chọn ô", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                cell.Symbol = GridCell.CellSymbol.X;
                lblTargetCoords.Text = $"Ô #{cell.Index} (H{cell.Row + 1}, C{cell.Col + 1})  |  Ký hiệu: X  |  Kích thước: {cell.WidthMm:F1}x{cell.HeightMm:F1} mm";
                lblTargetCoords.ForeColor = Color.FromArgb(30, 41, 59);
                picCameraLive.Invalidate();

                if (_robotService == null || !_robotService.State.IsConnected || !_robotService.State.IsServoOn)
                {
                    _logger?.Log($"[Grid] Đã đánh dấu ký hiệu X tại ô #{cell.Index} (Robot chưa sẵn sàng vẽ thực tế)");
                    MessageBox.Show($"Đã đánh dấu ký hiệu X tại ô #{cell.Index}.\nĐể Robot thực hiện vẽ thực tế, vui lòng kết nối Robot và kích hoạt Servo.", "Ký hiệu X", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (cell.CornersRobot == null || cell.CornersRobot.Length < 4)
                {
                    MessageBox.Show("Ô vuông chưa có dữ liệu 4 đỉnh tọa độ Robot hợp lệ.", "Lỗi dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                for (int i = 0; i < 4; i++)
                {
                    double r = Math.Sqrt(cell.CornersRobot[i].X * cell.CornersRobot[i].X + cell.CornersRobot[i].Y * cell.CornersRobot[i].Y);
                    if (r > 300.0)
                    {
                        MessageBox.Show($"Đỉnh #{i + 1} của ô #{cell.Index} có bán kính R = {r:F1} mm vượt giới hạn làm việc 300.0 mm.", "Vượt giới hạn an toàn", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                double workZ = _cameraService?.Config?.CalibratedWorkZ ?? 0.0;
                var curPos = _robotService.GetCurrentPosition()?.WorldPosition;
                if (curPos != null && Math.Abs(workZ) < 0.001) workZ = curPos.Z;

                int speed = tbCameraMoveSpeed.Value;
                string confirmMsg = $"Xác nhận điều khiển Robot THL300 vẽ ký hiệu X vào ô #{cell.Index}:\n\n" +
                                   $" • Kích thước ô: {cell.WidthMm:F1} x {cell.HeightMm:F1} mm\n" +
                                   $" • Cao độ vẽ (Z_work): {workZ:F1} mm\n" +
                                   $" • Cao độ an toàn (Z_safe): {workZ + 10.0:F1} mm\n" +
                                   $" • Tốc độ: {speed} %\n\n" +
                                   $"Thực hiện chu trình vẽ nét?";

                if (MessageBox.Show(confirmMsg, "Xác nhận vẽ ký hiệu", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                try
                {
                    _isCameraMoving = true;
                    btnDrawX.Enabled = false;
                    btnDrawO.Enabled = false;
                    btnClearSymbol.Enabled = false;
                    btnMoveRobotToTarget.Enabled = false;

                    _logger?.Log($"[Robot Draw] Bắt đầu vẽ chữ X vào ô #{cell.Index} (Tốc độ: {speed}%)...");
                    bool ok = await _robotService.DrawXInCellAsync(cell.CornersRobot, workZ, speed);
                    if (ok)
                    {
                        _logger?.Log($"[Robot Draw] Hoàn thành vẽ chữ X vào ô #{cell.Index}.");
                        MessageBox.Show($"Robot THL300 đã hoàn thành vẽ chữ X vào ô #{cell.Index}.", "Hoàn thành", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Log($"[Robot Draw] Lỗi: {ex.Message}");
                    MessageBox.Show("Lỗi thực thi: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _isCameraMoving = false;
                    if (!IsDisposed && !Disposing)
                    {
                        btnDrawX.Enabled = true;
                        btnDrawO.Enabled = true;
                        btnClearSymbol.Enabled = true;
                        btnMoveRobotToTarget.Enabled = _isTargetValidRadius;
                    }
                }
            };

            // Xử lý sự kiện bấm nút Vẽ Chữ O
            btnDrawO.Click += async (s, e) => {
                var cell = _gridService.SelectedCell;
                if (cell == null)
                {
                    MessageBox.Show("Vui lòng chọn một ô vuông trong lưới trước khi thực hiện.", "Chưa chọn ô", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                cell.Symbol = GridCell.CellSymbol.O;
                lblTargetCoords.Text = $"Ô #{cell.Index} (H{cell.Row + 1}, C{cell.Col + 1})  |  Ký hiệu: O  |  Kích thước: {cell.WidthMm:F1}x{cell.HeightMm:F1} mm";
                lblTargetCoords.ForeColor = Color.FromArgb(30, 41, 59);
                picCameraLive.Invalidate();

                if (_robotService == null || !_robotService.State.IsConnected || !_robotService.State.IsServoOn)
                {
                    _logger?.Log($"[Grid] Đã đánh dấu ký hiệu O tại ô #{cell.Index} (Robot chưa sẵn sàng vẽ thực tế)");
                    MessageBox.Show($"Đã đánh dấu ký hiệu O tại ô #{cell.Index}.\nĐể Robot thực hiện vẽ thực tế, vui lòng kết nối Robot và kích hoạt Servo.", "Ký hiệu O", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (cell.CornersRobot == null || cell.CornersRobot.Length < 4)
                {
                    MessageBox.Show("Ô vuông chưa có dữ liệu 4 đỉnh tọa độ Robot hợp lệ.", "Lỗi dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                for (int i = 0; i < 4; i++)
                {
                    double r = Math.Sqrt(cell.CornersRobot[i].X * cell.CornersRobot[i].X + cell.CornersRobot[i].Y * cell.CornersRobot[i].Y);
                    if (r > 300.0)
                    {
                        MessageBox.Show($"Đỉnh #{i + 1} của ô #{cell.Index} có bán kính R = {r:F1} mm vượt giới hạn làm việc 300.0 mm.", "Vượt giới hạn an toàn", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                double workZ = _cameraService?.Config?.CalibratedWorkZ ?? 0.0;
                var curPos = _robotService.GetCurrentPosition()?.WorldPosition;
                if (curPos != null && Math.Abs(workZ) < 0.001) workZ = curPos.Z;

                int speed = tbCameraMoveSpeed.Value;
                string confirmMsg = $"Xác nhận điều khiển Robot THL300 vẽ ký hiệu O vào ô #{cell.Index}:\n\n" +
                                   $" • Kích thước ô: {cell.WidthMm:F1} x {cell.HeightMm:F1} mm\n" +
                                   $" • Cao độ vẽ (Z_work): {workZ:F1} mm\n" +
                                   $" • Cao độ an toàn (Z_safe): {workZ + 10.0:F1} mm\n" +
                                   $" • Tốc độ: {speed} %\n\n" +
                                   $"Thực hiện chu trình vẽ cung tròn?";

                if (MessageBox.Show(confirmMsg, "Xác nhận vẽ ký hiệu", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                try
                {
                    _isCameraMoving = true;
                    btnDrawX.Enabled = false;
                    btnDrawO.Enabled = false;
                    btnClearSymbol.Enabled = false;
                    btnMoveRobotToTarget.Enabled = false;

                    _logger?.Log($"[Robot Draw] Bắt đầu vẽ chữ O vào ô #{cell.Index} (Tốc độ: {speed}%)...");
                    bool ok = await _robotService.DrawOInCellAsync(cell.CornersRobot, workZ, speed);
                    if (ok)
                    {
                        _logger?.Log($"[Robot Draw] Hoàn thành vẽ chữ O vào ô #{cell.Index}.");
                        MessageBox.Show($"Robot THL300 đã hoàn thành vẽ chữ O vào ô #{cell.Index}.", "Hoàn thành", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Log($"[Robot Draw] Lỗi: {ex.Message}");
                    MessageBox.Show("Lỗi thực thi: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _isCameraMoving = false;
                    if (!IsDisposed && !Disposing)
                    {
                        btnDrawX.Enabled = true;
                        btnDrawO.Enabled = true;
                        btnClearSymbol.Enabled = true;
                        btnMoveRobotToTarget.Enabled = _isTargetValidRadius;
                    }
                }
            };

            // Xử lý sự kiện Xóa ký hiệu trên ô
            btnClearSymbol.Click += (s, e) => {
                var cell = _gridService.SelectedCell;
                if (cell == null)
                {
                    MessageBox.Show("Vui lòng chọn một ô vuông trong lưới trước.", "Chưa chọn ô", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                cell.Symbol = GridCell.CellSymbol.None;
                lblTargetCoords.Text = $"Ô #{cell.Index} (H{cell.Row + 1}, C{cell.Col + 1})  |  Ký hiệu: (Trống)  |  Kích thước: {cell.WidthMm:F1}x{cell.HeightMm:F1} mm";
                lblTargetCoords.ForeColor = Color.FromArgb(30, 41, 59);
                _logger?.Log($"[Grid] Đã xóa ký hiệu tại ô #{cell.Index}.");
                picCameraLive.Invalidate();
            };

            // Responsive layout cho thanh điều khiển 2 hàng bên dưới
            Action updateBottomLayout = () => {
                int w = pnlBottomControl.ClientSize.Width;
                int rightMargin = 12;

                // --- HÀNG 1: THÔNG TIN MỤC TIÊU & TỐC ĐỘ ROBOT ---
                lblTargetIcon.Location = new Point(12, 11);
                lblTargetCoords.Location = new Point(80, 11);

                lblCameraMoveSpeedVal.Location = new Point(w - rightMargin - lblCameraMoveSpeedVal.Width, 11);
                tbCameraMoveSpeed.Location = new Point(lblCameraMoveSpeedVal.Left - 4 - tbCameraMoveSpeed.Width, 6);
                lblSpeed.Location = new Point(tbCameraMoveSpeed.Left - 4 - lblSpeed.Width, 11);
                btnClearTarget.Location = new Point(lblSpeed.Left - 16 - btnClearTarget.Width, 8);
                lblTargetStatus.Location = new Point(btnClearTarget.Left - 10 - lblTargetStatus.Width, 10);

                // --- HÀNG 2: THAO TÁC KÝ HIỆU & CHUYỂN ĐỘNG ROBOT ---
                btnDrawX.Location = new Point(12, 42);
                btnDrawO.Location = new Point(btnDrawX.Right + 8, 42);
                btnClearSymbol.Location = new Point(btnDrawO.Right + 8, 42);

                btnStopCameraMove.Location = new Point(w - rightMargin - btnStopCameraMove.Width, 42);
                btnMoveRobotToTarget.Location = new Point(btnStopCameraMove.Left - 8 - btnMoveRobotToTarget.Width, 42);
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
                                   $"Thực thi chuyển động?";

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

            pnlBottomControl.Controls.Add(btnDrawX);
            pnlBottomControl.Controls.Add(btnDrawO);
            pnlBottomControl.Controls.Add(btnClearSymbol);

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

            // Wire Auto-Config calibration reload event
            if (_cameraService != null)
            {
                _cameraService.CalibrationAutoReloaded += (fileName) => {
                    if (IsDisposed || Disposing) return;
                    if (InvokeRequired)
                    {
                        try { BeginInvoke(new Action(() => OnCalibrationAutoReloaded(fileName))); } catch { }
                    }
                    else
                    {
                        OnCalibrationAutoReloaded(fileName);
                    }
                };
                _cameraService.CalibrationUpdated += () => {
                    UpdateGridFromCalibration();
                };
            }

            // Tự động khởi tạo lưới Grid từ dữ liệu calib 4 điểm nếu có
            UpdateGridFromCalibration();

            // Handle clean stop when form closes
            this.FormClosing += (s, e) => {
                StopCameraLive();
                _cameraService?.Dispose();
            };

            return workspaceCamera;
        }

        private void OnCalibrationAutoReloaded(string fileName)
        {
            if (lblAutoConfigBadge != null && !lblAutoConfigBadge.IsDisposed)
            {
                lblAutoConfigBadge.Text = $"✔ Auto-Config: Đã nạp {fileName} ({DateTime.Now:HH:mm:ss})";
                lblAutoConfigBadge.ForeColor = Color.FromArgb(0, 120, 215);
            }

            UpdateGridFromCalibration();

            // Nếu đang chọn một điểm trên khung hình camera -> tự động tính lại tọa độ Robot Base với ma trận mới
            if (_selectedPixel.HasValue)
            {
                double workZ = 0.0;
                if (_cameraService != null && _cameraService.PixelToRobotBase(_selectedPixel.Value.X, _selectedPixel.Value.Y, workZ, out double rx, out double ry, out double rz))
                {
                    double radius = Math.Sqrt(rx * rx + ry * ry);
                    _targetRobotX = rx;
                    _targetRobotY = ry;
                    _targetRobotZ = rz;
                    _targetRobotRadius = radius;
                    _isTargetValidRadius = (radius <= 300.0);
                    UpdateTargetDisplay();
                }
            }

            picCameraLive?.Invalidate();
            _logger?.Log($"[Camera UI] Auto-Config: Đã tự động cập nhật ma trận mới từ file: {fileName}");
        }

        private void UpdateTargetDisplay()
        {
            if (!_selectedPixel.HasValue || !_targetRobotX.HasValue || !_targetRobotY.HasValue)
            {
                lblTargetCoords.Text = "Chưa chọn mục tiêu";
                lblTargetCoords.ForeColor = Color.FromArgb(100, 116, 139);
                lblTargetStatus.Text = "—";
                lblTargetStatus.BackColor = Color.Transparent;
                lblTargetStatus.ForeColor = Color.FromArgb(100, 116, 139);
                btnMoveRobotToTarget.Enabled = false;
                return;
            }

            lblTargetCoords.Text = $"Pixel: ({_selectedPixel.Value.X:F0}, {_selectedPixel.Value.Y:F0}) | Robot: X = {_targetRobotX:F1} mm, Y = {_targetRobotY:F1} mm | R = {_targetRobotRadius:F1} mm";
            lblTargetCoords.ForeColor = Color.FromArgb(30, 41, 59);

            if (_isTargetValidRadius)
            {
                lblTargetStatus.Text = "Hợp lệ (R ≤ 300 mm)";
                lblTargetStatus.BackColor = Color.FromArgb(240, 253, 244);
                lblTargetStatus.ForeColor = Color.FromArgb(22, 101, 52);
                btnMoveRobotToTarget.Enabled = !_isCameraMoving;
            }
            else
            {
                lblTargetStatus.Text = "Vượt giới hạn (R > 300 mm)";
                lblTargetStatus.BackColor = Color.FromArgb(254, 242, 242);
                lblTargetStatus.ForeColor = Color.FromArgb(185, 28, 28);
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

        private void OpenFourPointCalibrationDialog()
        {
            if (_fourPointDialog == null || _fourPointDialog.IsDisposed)
            {
                _fourPointDialog = new FourPointCalibrationDialog(_cameraService, _robotService);
                _fourPointDialog.RequestPickPixelOnCamera += (pointIdx) => {
                    _logger?.Log($"[Calib 4 Điểm] Bắt đầu chấm Điểm P{pointIdx + 1} trên khung hình camera...");
                    picCameraLive?.Invalidate();
                };
                _fourPointDialog.OverlayVisibilityChanged += (visible) => {
                    _showCalibOverlay = visible;
                    picCameraLive?.Invalidate();
                };
                _fourPointDialog.CalibrationApplied += () => {
                    picCameraLive?.Invalidate();
                    UpdateTargetDisplay();
                    UpdateGridFromCalibration();
                    if (lblAutoConfigBadge != null && !lblAutoConfigBadge.IsDisposed)
                    {
                        lblAutoConfigBadge.Text = $"✔ Homography: {_cameraService.Config.HomographyRmsErrorMm:F3}mm";
                        lblAutoConfigBadge.ForeColor = Color.FromArgb(0, 120, 215);
                    }
                };
                _fourPointDialog.FormClosed += (s, e) => {
                    _fourPointDialog = null;
                    picCameraLive?.Invalidate();
                };
                _fourPointDialog.Show(this);
            }
            else
            {
                _fourPointDialog.BringToFront();
            }
        }

        private void UpdateGridFromCalibration(Bitmap frame = null)
        {
            try
            {
                var cfg = _cameraService?.Config;
                if (cfg == null) return;

                PointF[] pixels = _fourPointDialog?.GetCurrentPixelPoints() ?? cfg.CalibPixelPoints;
                PointF[] robots = _fourPointDialog?.GetCurrentRobotPoints() ?? cfg.CalibRobotPoints;

                if (pixels != null && pixels.Length >= 4 && pixels[0] != PointF.Empty && pixels[1] != PointF.Empty && pixels[2] != PointF.Empty && pixels[3] != PointF.Empty)
                {
                    Bitmap currentFrame = frame ?? (picCameraLive?.Image as Bitmap);
                    _gridService.GenerateGrid(pixels, robots, cfg.H_pixel2robot, cfg.H_robot2pixel, currentFrame);
                    picCameraLive?.Invalidate();
                }
            }
            catch { }
        }

        private void ShowGridConfigDialog()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "Cấu hình Lưới Grid Ô Vuông & Xử lý ảnh bám nét";
                dlg.Size = new Size(420, 480);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = Color.FromArgb(245, 247, 250);
                dlg.Font = new Font("Segoe UI", 9F);

                Label lblRows = new Label { Text = "Số Hàng (Rows):", Location = new Point(24, 25), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
                NumericUpDown numRows = new NumericUpDown { Location = new Point(180, 22), Width = 80, Minimum = 1, Maximum = 60, Value = _gridService.Rows };

                Label lblCols = new Label { Text = "Số Cột (Cols):", Location = new Point(24, 62), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
                NumericUpDown numCols = new NumericUpDown { Location = new Point(180, 59), Width = 80, Minimum = 1, Maximum = 60, Value = _gridService.Cols };

                Label lblSize = new Label { Text = "Kích thước ô (mm):", Location = new Point(24, 99), AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
                NumericUpDown numSize = new NumericUpDown { Location = new Point(180, 96), Width = 80, Minimum = 5, Maximum = 200, DecimalPlaces = 1, Value = (decimal)_gridService.SquareSizeMm };

                Button btnAuto = new Button
                {
                    Text = "Ước lượng theo kích thước (25mm)",
                    Font = new Font("Segoe UI", 8.5F),
                    BackColor = Color.FromArgb(248, 250, 252),
                    ForeColor = Color.FromArgb(30, 41, 59),
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(24, 135),
                    Size = new Size(240, 28),
                    Cursor = Cursors.Hand
                };
                btnAuto.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                btnAuto.Click += (s, e) => {
                    var robots = _fourPointDialog?.GetCurrentRobotPoints() ?? _cameraService?.Config?.CalibRobotPoints;
                    _gridService.AutoDetectGridDimensions(robots, (double)numSize.Value);
                    numRows.Value = Math.Max(numRows.Minimum, Math.Min(numRows.Maximum, _gridService.Rows));
                    numCols.Value = Math.Max(numCols.Minimum, Math.Min(numCols.Maximum, _gridService.Cols));
                };

                CheckBox chkLines = new CheckBox
                {
                    Text = "Vẽ đường ranh giới ô",
                    Checked = _gridService.ShowGridLines,
                    Location = new Point(28, 178),
                    AutoSize = true
                };

                CheckBox chkLabels = new CheckBox
                {
                    Text = "Hiển thị nhãn chỉ số ô (#1..#N)",
                    Checked = _gridService.ShowCellLabels,
                    Location = new Point(28, 208),
                    AutoSize = true
                };

                CheckBox chkVisionSnap = new CheckBox
                {
                    Text = "Bám sát nét mực bằng thị giác (Vision Snapping)",
                    Checked = _gridService.EnableVisionSnapping,
                    Location = new Point(28, 240),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = Color.FromArgb(30, 41, 59)
                };

                CheckBox chkCorners = new CheckBox
                {
                    Text = "Hiển thị chữ thập tại giao điểm (+)",
                    Checked = _gridService.ShowCornerIntersections,
                    Location = new Point(28, 272),
                    AutoSize = true
                };

                Label lblRadius = new Label { Text = "Bán kính tìm nét (px):", Location = new Point(28, 306), AutoSize = true };
                NumericUpDown numRadius = new NumericUpDown { Location = new Point(180, 304), Width = 80, Minimum = 6, Maximum = 40, Value = _gridService.ValleySearchRadiusPx };

                Button btnSave = new Button
                {
                    Text = "Cập nhật lưới",
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    BackColor = Color.FromArgb(30, 41, 59),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(24, 348),
                    Size = new Size(356, 38),
                    Cursor = Cursors.Hand
                };
                btnSave.FlatAppearance.BorderSize = 0;
                btnSave.Click += (s, e) => {
                    _gridService.Rows = (int)numRows.Value;
                    _gridService.Cols = (int)numCols.Value;
                    _gridService.SquareSizeMm = (double)numSize.Value;
                    _gridService.ShowGridLines = chkLines.Checked;
                    _gridService.ShowCellLabels = chkLabels.Checked;
                    _gridService.EnableVisionSnapping = chkVisionSnap.Checked;
                    _gridService.ShowCornerIntersections = chkCorners.Checked;
                    _gridService.ValleySearchRadiusPx = (int)numRadius.Value;
                    _gridService.IsGridEnabled = true;

                    if (btnToggleGrid != null)
                    {
                        btnToggleGrid.Text = "Lưới: BẬT";
                        btnToggleGrid.BackColor = Color.FromArgb(241, 245, 249);
                        btnToggleGrid.ForeColor = Color.FromArgb(30, 41, 59);
                    }

                    UpdateGridFromCalibration(picCameraLive?.Image as Bitmap);
                    _logger?.Log($"[Grid] Đã cập nhật lưới: {_gridService.Rows} hàng x {_gridService.Cols} cột ({_gridService.Cells.Count} ô vuông) - {_gridService.SnapStatusMessage}");
                    dlg.Close();
                };

                dlg.Controls.Add(lblRows);
                dlg.Controls.Add(numRows);
                dlg.Controls.Add(lblCols);
                dlg.Controls.Add(numCols);
                dlg.Controls.Add(lblSize);
                dlg.Controls.Add(numSize);
                dlg.Controls.Add(btnAuto);
                dlg.Controls.Add(chkLines);
                dlg.Controls.Add(chkLabels);
                dlg.Controls.Add(chkVisionSnap);
                dlg.Controls.Add(chkCorners);
                dlg.Controls.Add(lblRadius);
                dlg.Controls.Add(numRadius);
                dlg.Controls.Add(btnSave);
                dlg.ShowDialog(this);
            }
        }
    }
}
