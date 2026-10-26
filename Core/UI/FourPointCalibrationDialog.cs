using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Test_1.Services;

namespace Test_1.UI
{
    public class FourPointCalibrationDialog : Form
    {
        private readonly CameraTransformService _cameraService;
        private readonly RobotService _robotService;

        // 4 Điểm dữ liệu
        private readonly TextBox[] _txtPixelU = new TextBox[4];
        private readonly TextBox[] _txtPixelV = new TextBox[4];
        private readonly TextBox[] _txtRobotX = new TextBox[4];
        private readonly TextBox[] _txtRobotY = new TextBox[4];
        private readonly TextBox[] _txtRobotZ = new TextBox[4];
        private readonly Button[] _btnPickPixel = new Button[4];
        private readonly Button[] _btnGetRobotPos = new Button[4];
        private readonly Label[] _lblPointStatus = new Label[4];

        private Label lblRmsResult;
        private Label lblStatusEvaluation;
        private TextBox txtMatrixH;
        private TextBox txtWorkZ;
        private CheckBox chkShowOverlay;
        private Button btnCalculate;
        private Button btnApply;

        private double[,] _calculatedH = null;
        private double[,] _calculatedHInv = null;
        private double _calculatedRms = 0.0;

        public int ActivePickingPointIndex { get; private set; } = -1;
        public bool ShowOverlayOnCamera => chkShowOverlay != null && chkShowOverlay.Checked;

        public event Action<int> RequestPickPixelOnCamera;
        public event Action<bool> OverlayVisibilityChanged;
        public event Action CalibrationApplied;

        public FourPointCalibrationDialog(CameraTransformService cameraService, RobotService robotService)
        {
            _cameraService = cameraService;
            _robotService = robotService;

            InitializeUI();
            LoadCurrentCalibrationData();
        }

        private void InitializeUI()
        {
            this.Text = "Hiệu chuẩn nhanh 4 điểm (Planar Homography) - SCARA THL300 & Brio 100";
            this.Size = new Size(820, 680);
            this.MinimumSize = new Size(800, 650);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(245, 247, 250);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // 1. Header Banner
            Panel pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Color.FromArgb(26, 35, 126),
                Padding = new Padding(16, 10, 16, 10)
            };

            Label lblTitle = new Label
            {
                Text = "🎯 HIỆU CHUẨN NHANH 4 ĐIỂM (PLANAR HOMOGRAPHY)",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(14, 10),
                AutoSize = true
            };

            Label lblSub = new Label
            {
                Text = "Khắc phục triệt để sai lệch do góc nghiêng camera bằng phép biến đổi phẳng phối cảnh giữa ảnh Brio 100 và mặt bàn Robot THL300.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(197, 202, 233),
                Location = new Point(15, 36),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);

            // 2. Main Content
            Panel pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                AutoScroll = true
            };

            // Instruction Card
            Panel pnlGuide = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(232, 234, 246),
                Padding = new Padding(10, 8, 10, 8)
            };
            Label lblGuide = new Label
            {
                Text = "💡 Hướng dẫn 3 bước: \n" +
                       " 1. Bấm [🎯 Chấm Camera] rồi click chuột vào điểm mốc trên ảnh. " +
                       " 2. Jog Robot đến điểm mốc đó rồi bấm [📍 Lấy vị trí Robot]. " +
                       " 3. Đủ 4 điểm bấm [⚡ TÍNH TOÁN] rồi [💾 LƯU & ÁP DỤNG].",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(40, 53, 147),
                Dock = DockStyle.Fill
            };
            pnlGuide.Controls.Add(lblGuide);

            // 4 Points Group
            GroupBox grpPoints = new GroupBox
            {
                Text = "Danh Sách 4 Cặp Điểm Tương Ứng (Pixel Camera ↔ Robot Base)",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                Dock = DockStyle.Top,
                Height = 270,
                Padding = new Padding(10)
            };

            Color[] pointColors = {
                Color.FromArgb(220, 53, 69),   // P1 Đỏ
                Color.FromArgb(0, 123, 255),  // P2 Xanh dương
                Color.FromArgb(111, 66, 193), // P3 Tím
                Color.FromArgb(40, 167, 69)   // P4 Xanh lá
            };

            int startY = 26;
            int rowHeight = 54;

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                int y = startY + i * rowHeight;

                // Point Badge
                Label lblBadge = new Label
                {
                    Text = $"P{idx + 1}",
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = pointColors[idx],
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(34, 26),
                    Location = new Point(12, y + 6)
                };

                // Pixel Inputs
                Label lblU = new Label { Text = "U:", Location = new Point(56, y + 10), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
                _txtPixelU[idx] = new TextBox { Location = new Point(74, y + 7), Width = 60, Font = new Font("Consolas", 9F) };

                Label lblV = new Label { Text = "V:", Location = new Point(140, y + 10), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
                _txtPixelV[idx] = new TextBox { Location = new Point(158, y + 7), Width = 60, Font = new Font("Consolas", 9F) };

                _btnPickPixel[idx] = new Button
                {
                    Text = "🎯 Chấm Camera",
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    BackColor = Color.FromArgb(240, 244, 248),
                    ForeColor = pointColors[idx],
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(226, y + 6),
                    Size = new Size(110, 27),
                    Cursor = Cursors.Hand
                };
                _btnPickPixel[idx].FlatAppearance.BorderColor = pointColors[idx];
                _btnPickPixel[idx].Click += (s, e) => StartPickingPixel(idx);

                // Robot Base Inputs
                Label lblX = new Label { Text = "X:", Location = new Point(348, y + 10), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
                _txtRobotX[idx] = new TextBox { Location = new Point(366, y + 7), Width = 66, Font = new Font("Consolas", 9F) };

                Label lblY = new Label { Text = "Y:", Location = new Point(438, y + 10), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
                _txtRobotY[idx] = new TextBox { Location = new Point(456, y + 7), Width = 66, Font = new Font("Consolas", 9F) };

                Label lblZ = new Label { Text = "Z:", Location = new Point(528, y + 10), AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
                _txtRobotZ[idx] = new TextBox { Location = new Point(546, y + 7), Width = 56, Font = new Font("Consolas", 9F), Text = "0.0" };

                _btnGetRobotPos[idx] = new Button
                {
                    Text = "📍 Lấy vị trí Robot",
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    BackColor = Color.FromArgb(235, 248, 240),
                    ForeColor = Color.FromArgb(46, 125, 50),
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(610, y + 6),
                    Size = new Size(115, 27),
                    Cursor = Cursors.Hand
                };
                _btnGetRobotPos[idx].FlatAppearance.BorderColor = Color.FromArgb(46, 125, 50);
                _btnGetRobotPos[idx].Click += (s, e) => FillCurrentRobotPosition(idx);

                _lblPointStatus[idx] = new Label
                {
                    Text = "—",
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    ForeColor = Color.Gray,
                    Location = new Point(732, y + 11),
                    AutoSize = true
                };

                grpPoints.Controls.Add(lblBadge);
                grpPoints.Controls.Add(lblU);
                grpPoints.Controls.Add(_txtPixelU[idx]);
                grpPoints.Controls.Add(lblV);
                grpPoints.Controls.Add(_txtPixelV[idx]);
                grpPoints.Controls.Add(_btnPickPixel[idx]);

                grpPoints.Controls.Add(lblX);
                grpPoints.Controls.Add(_txtRobotX[idx]);
                grpPoints.Controls.Add(lblY);
                grpPoints.Controls.Add(_txtRobotY[idx]);
                grpPoints.Controls.Add(lblZ);
                grpPoints.Controls.Add(_txtRobotZ[idx]);
                grpPoints.Controls.Add(_btnGetRobotPos[idx]);
                grpPoints.Controls.Add(_lblPointStatus[idx]);
            }

            // Calculation Results Group
            GroupBox grpResult = new GroupBox
            {
                Text = "Kết Quả Tính Toán Ma Trận Homography H",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                Dock = DockStyle.Top,
                Height = 160,
                Padding = new Padding(10)
            };

            Label lblRmsTitle = new Label { Text = "Sai số tái chiếu (RMS Error):", Location = new Point(14, 25), AutoSize = true };
            lblRmsResult = new Label
            {
                Text = "Chưa tính",
                Font = new Font("Consolas", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(108, 117, 125),
                Location = new Point(188, 23),
                AutoSize = true
            };

            lblStatusEvaluation = new Label
            {
                Text = "Vui lòng nhập/chọn đủ 4 cặp điểm để giải toán.",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(108, 117, 125),
                Location = new Point(310, 25),
                AutoSize = true
            };

            Label lblZWork = new Label { Text = "Cao độ mặt phẳng Z (mm):", Location = new Point(14, 52), AutoSize = true };
            txtWorkZ = new TextBox { Text = "0.0", Location = new Point(188, 49), Width = 70, Font = new Font("Consolas", 9F) };

            Label lblMatH = new Label { Text = "Ma trận H (3x3):", Location = new Point(14, 78), AutoSize = true };
            txtMatrixH = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Location = new Point(188, 76),
                Size = new Size(570, 70),
                Font = new Font("Consolas", 8.5F),
                BackColor = Color.FromArgb(248, 249, 250),
                ScrollBars = ScrollBars.Vertical
            };

            grpResult.Controls.Add(lblRmsTitle);
            grpResult.Controls.Add(lblRmsResult);
            grpResult.Controls.Add(lblStatusEvaluation);
            grpResult.Controls.Add(lblZWork);
            grpResult.Controls.Add(txtWorkZ);
            grpResult.Controls.Add(lblMatH);
            grpResult.Controls.Add(txtMatrixH);

            // Bottom Buttons Bar
            Panel pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.White,
                Padding = new Padding(12)
            };
            pnlBottom.Paint += (s, e) => {
                e.Graphics.DrawLine(new Pen(Color.FromArgb(226, 232, 240), 1), 0, 0, pnlBottom.Width, 0);
            };

            chkShowOverlay = new CheckBox
            {
                Text = "Hiển thị 4 mốc Calib & Khung bao trên Camera Live",
                Checked = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 71, 103),
                Location = new Point(16, 18),
                AutoSize = true
            };
            chkShowOverlay.CheckedChanged += (s, e) => {
                OverlayVisibilityChanged?.Invoke(chkShowOverlay.Checked);
            };

            btnCalculate = new Button
            {
                Text = "⚡ TÍNH TOÁN MA TRẬN",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 123, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(170, 36),
                Location = new Point(410, 12),
                Cursor = Cursors.Hand
            };
            btnCalculate.FlatAppearance.BorderSize = 0;
            btnCalculate.Click += (s, e) => ExecuteCalculateHomography();

            btnApply = new Button
            {
                Text = "💾 LƯU & ÁP DỤNG NGAY",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(185, 36),
                Location = new Point(590, 12),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            btnApply.FlatAppearance.BorderSize = 0;
            btnApply.Click += (s, e) => ExecuteApplyCalibration();

            pnlBottom.Controls.Add(chkShowOverlay);
            pnlBottom.Controls.Add(btnCalculate);
            pnlBottom.Controls.Add(btnApply);

            // Assemble
            pnlContent.Controls.Add(grpResult);
            pnlContent.Controls.Add(grpPoints);
            pnlContent.Controls.Add(pnlGuide);

            this.Controls.Add(pnlContent);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlHeader);
        }

        private void LoadCurrentCalibrationData()
        {
            if (_cameraService == null || _cameraService.Config == null) return;
            var cfg = _cameraService.Config;

            if (cfg.HasHomography && cfg.H_pixel2robot != null)
            {
                _calculatedH = (double[,])cfg.H_pixel2robot.Clone();
                _calculatedHInv = (double[,])cfg.H_robot2pixel.Clone();
                _calculatedRms = cfg.HomographyRmsErrorMm;
                txtWorkZ.Text = cfg.CalibratedWorkZ.ToString("F2", CultureInfo.InvariantCulture);

                DisplayMatrixH(_calculatedH);
                lblRmsResult.Text = $"{_calculatedRms:F4} mm";
                lblRmsResult.ForeColor = Color.FromArgb(46, 125, 50);
                lblStatusEvaluation.Text = "✔ Đã có ma trận Homography hiệu chuẩn hợp lệ.";
                lblStatusEvaluation.ForeColor = Color.FromArgb(46, 125, 50);
                btnApply.Enabled = true;

                if (cfg.CalibPixelPoints != null && cfg.CalibRobotPoints != null &&
                    cfg.CalibPixelPoints.Length >= 4 && cfg.CalibRobotPoints.Length >= 4)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (cfg.CalibPixelPoints[i] != PointF.Empty)
                        {
                            _txtPixelU[i].Text = cfg.CalibPixelPoints[i].X.ToString("F1", CultureInfo.InvariantCulture);
                            _txtPixelV[i].Text = cfg.CalibPixelPoints[i].Y.ToString("F1", CultureInfo.InvariantCulture);
                        }
                        if (cfg.CalibRobotPoints[i] != PointF.Empty)
                        {
                            _txtRobotX[i].Text = cfg.CalibRobotPoints[i].X.ToString("F2", CultureInfo.InvariantCulture);
                            _txtRobotY[i].Text = cfg.CalibRobotPoints[i].Y.ToString("F2", CultureInfo.InvariantCulture);
                            _txtRobotZ[i].Text = cfg.CalibratedWorkZ.ToString("F2", CultureInfo.InvariantCulture);
                            _lblPointStatus[i].Text = "✔ Đã lưu";
                            _lblPointStatus[i].ForeColor = Color.FromArgb(46, 125, 50);
                        }
                    }
                }
            }
            else
            {
                // Nếu chưa có, điền mẫu theo cao độ robot hiện tại nếu đang kết nối
                var curPos = _robotService?.GetCurrentPosition()?.WorldPosition;
                if (curPos != null)
                {
                    txtWorkZ.Text = curPos.Z.ToString("F2", CultureInfo.InvariantCulture);
                    for (int i = 0; i < 4; i++)
                    {
                        _txtRobotZ[i].Text = curPos.Z.ToString("F2", CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        private void StartPickingPixel(int pointIndex)
        {
            ActivePickingPointIndex = pointIndex;

            for (int i = 0; i < 4; i++)
            {
                if (i == pointIndex)
                {
                    _btnPickPixel[i].Text = "👉 Click Camera...";
                    _btnPickPixel[i].BackColor = Color.FromArgb(255, 235, 238);
                    _btnPickPixel[i].ForeColor = Color.FromArgb(198, 40, 40);
                }
                else
                {
                    _btnPickPixel[i].Text = "🎯 Chấm Camera";
                    _btnPickPixel[i].BackColor = Color.FromArgb(240, 244, 248);
                    _btnPickPixel[i].ForeColor = Color.FromArgb(52, 71, 103);
                }
            }

            RequestPickPixelOnCamera?.Invoke(pointIndex);
        }

        public void SetPickedPixel(int pointIndex, float u, float v)
        {
            if (pointIndex < 0 || pointIndex >= 4) return;

            _txtPixelU[pointIndex].Text = u.ToString("F1", CultureInfo.InvariantCulture);
            _txtPixelV[pointIndex].Text = v.ToString("F1", CultureInfo.InvariantCulture);

            _btnPickPixel[pointIndex].Text = "✔ Đã chấm";
            _btnPickPixel[pointIndex].BackColor = Color.FromArgb(232, 245, 233);
            _btnPickPixel[pointIndex].ForeColor = Color.FromArgb(46, 125, 50);

            UpdatePointStatus(pointIndex);

            // Tự động chuyển con trỏ sang chấm điểm tiếp theo nếu chưa chấm
            if (pointIndex < 3 && string.IsNullOrWhiteSpace(_txtPixelU[pointIndex + 1].Text))
            {
                StartPickingPixel(pointIndex + 1);
            }
            else
            {
                ActivePickingPointIndex = -1;
            }
        }

        private void FillCurrentRobotPosition(int pointIndex)
        {
            if (_robotService == null || !_robotService.State.IsConnected)
            {
                MessageBox.Show("Robot chưa kết nối! Vui lòng kết nối Robot THL300 hoặc tự nhập tọa độ thủ công.", "Robot chưa kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var curPos = _robotService.GetCurrentPosition()?.WorldPosition;
            if (curPos == null)
            {
                MessageBox.Show("Chưa nhận được phản hồi tọa độ từ Robot!", "Lỗi đọc tọa độ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _txtRobotX[pointIndex].Text = curPos.X.ToString("F2", CultureInfo.InvariantCulture);
            _txtRobotY[pointIndex].Text = curPos.Y.ToString("F2", CultureInfo.InvariantCulture);
            _txtRobotZ[pointIndex].Text = curPos.Z.ToString("F2", CultureInfo.InvariantCulture);
            txtWorkZ.Text = curPos.Z.ToString("F2", CultureInfo.InvariantCulture);

            UpdatePointStatus(pointIndex);
        }

        private void UpdatePointStatus(int idx)
        {
            bool hasPixel = double.TryParse(_txtPixelU[idx].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                            double.TryParse(_txtPixelV[idx].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

            bool hasRobot = double.TryParse(_txtRobotX[idx].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                            double.TryParse(_txtRobotY[idx].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

            if (hasPixel && hasRobot)
            {
                _lblPointStatus[idx].Text = "✔ Đầy đủ";
                _lblPointStatus[idx].ForeColor = Color.FromArgb(46, 125, 50);
            }
            else if (hasPixel)
            {
                _lblPointStatus[idx].Text = "Thiếu Robot";
                _lblPointStatus[idx].ForeColor = Color.FromArgb(230, 81, 0);
            }
            else if (hasRobot)
            {
                _lblPointStatus[idx].Text = "Thiếu Pixel";
                _lblPointStatus[idx].ForeColor = Color.FromArgb(230, 81, 0);
            }
            else
            {
                _lblPointStatus[idx].Text = "—";
                _lblPointStatus[idx].ForeColor = Color.Gray;
            }
        }

        private void ExecuteCalculateHomography()
        {
            PointF[] pixelPts = new PointF[4];
            PointF[] robotPts = new PointF[4];

            for (int i = 0; i < 4; i++)
            {
                if (!double.TryParse(_txtPixelU[i].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double u) ||
                    !double.TryParse(_txtPixelV[i].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                {
                    MessageBox.Show($"Điểm P{i + 1} chưa có tọa độ Pixel (U, V) hợp lệ!", "Thiếu dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!double.TryParse(_txtRobotX[i].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double rx) ||
                    !double.TryParse(_txtRobotY[i].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double ry))
                {
                    MessageBox.Show($"Điểm P{i + 1} chưa có tọa độ Robot (X, Y) hợp lệ!", "Thiếu dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                pixelPts[i] = new PointF((float)u, (float)v);
                robotPts[i] = new PointF((float)rx, (float)ry);
            }

            if (!PlanarHomography.ComputeHomography(pixelPts, robotPts, out double[,] H, out double rms))
            {
                MessageBox.Show("Không thể tính ma trận Homography! Các điểm chọn có thể bị trùng nhau hoặc thẳng hàng.", "Lỗi tính toán", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!PlanarHomography.InvertHomography(H, out double[,] H_inv))
            {
                MessageBox.Show("Ma trận H bị suy biến không thể đảo nghịch!", "Lỗi tính toán", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _calculatedH = H;
            _calculatedHInv = H_inv;
            _calculatedRms = rms;

            DisplayMatrixH(H);
            lblRmsResult.Text = $"{rms:F4} mm";
            lblRmsResult.ForeColor = (rms <= 0.5) ? Color.FromArgb(46, 125, 50) : Color.FromArgb(230, 81, 0);

            string statusMsg = (rms <= 0.5)
                ? "✔ XUẤT SẮC: Sai số <= 0.5mm, đạt tiêu chuẩn điều khiển chính xác cao!"
                : (rms <= 1.0)
                    ? "✔ ĐẠT CHUẨN: Sai số <= 1.0mm, có thể sử dụng."
                    : "⚠ CẢNH BÁO: Sai số > 1.0mm, vui lòng kiểm tra lại độ chính xác khi bấm điểm.";

            lblStatusEvaluation.Text = statusMsg;
            lblStatusEvaluation.ForeColor = (rms <= 1.0) ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40);
            btnApply.Enabled = true;

            MessageBox.Show($"Tính toán ma trận Homography 4 điểm thành công!\n\n" +
                            $"• Sai số tái chiếu RMS: {rms:F4} mm\n" +
                            $"• Đánh giá: {(rms <= 1.0 ? "ĐẠT CHUẨN" : "CẦN CĂN LẠI")}\n\n" +
                            $"Vui lòng nhấn nút [LƯU & ÁP DỤNG NGAY] để bắt đầu điều khiển robot chính xác theo camera.",
                            "Kết quả tính toán", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DisplayMatrixH(double[,] H)
        {
            if (H == null) return;
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < 3; r++)
            {
                sb.AppendLine($"[ {H[r, 0],14:E6},  {H[r, 1],14:E6},  {H[r, 2],14:E6} ]");
            }
            txtMatrixH.Text = sb.ToString();
        }

        private void ExecuteApplyCalibration()
        {
            if (_calculatedH == null || _calculatedHInv == null)
            {
                MessageBox.Show("Vui lòng nhấn [TÍNH TOÁN MA TRẬN] trước khi áp dụng!", "Chưa tính toán", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PointF[] pixelPts = new PointF[4];
            PointF[] robotPts = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                float u = float.Parse(_txtPixelU[i].Text, CultureInfo.InvariantCulture);
                float v = float.Parse(_txtPixelV[i].Text, CultureInfo.InvariantCulture);
                float rx = float.Parse(_txtRobotX[i].Text, CultureInfo.InvariantCulture);
                float ry = float.Parse(_txtRobotY[i].Text, CultureInfo.InvariantCulture);
                pixelPts[i] = new PointF(u, v);
                robotPts[i] = new PointF(rx, ry);
            }

            double workZ = 0.0;
            double.TryParse(txtWorkZ.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out workZ);

            _cameraService.ApplyHomography(_calculatedH, _calculatedHInv, pixelPts, robotPts, workZ, _calculatedRms);

            CalibrationApplied?.Invoke();

            MessageBox.Show($"Đã lưu và áp dụng thành công ma trận Homography 4 điểm!\n\n" +
                            $"• Cao độ làm việc: Z = {workZ:F2} mm\n" +
                            $"• Sai số RMS: {_calculatedRms:F4} mm\n\n" +
                            $"Bây giờ mọi thao tác Click trên khung hình camera sẽ chuyển đổi chuẩn xác từng mm tới Robot Base!",
                            "Hiệu chuẩn thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);

            this.Close();
        }

        public PointF[] GetCurrentPixelPoints()
        {
            PointF[] pts = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                if (float.TryParse(_txtPixelU[i].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out float u) &&
                    float.TryParse(_txtPixelV[i].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out float v))
                {
                    pts[i] = new PointF(u, v);
                }
            }
            return pts;
        }

        public PointF[] GetCurrentRobotPoints()
        {
            PointF[] pts = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                if (float.TryParse(_txtRobotX[i].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out float rx) &&
                    float.TryParse(_txtRobotY[i].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out float ry))
                {
                    pts[i] = new PointF(rx, ry);
                }
            }
            return pts;
        }
    }
}
