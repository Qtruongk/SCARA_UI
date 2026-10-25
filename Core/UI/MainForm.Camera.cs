using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
        private Label lblFps;
        private int _frameCount = 0;
        private DateTime _lastFpsTime = DateTime.Now;
        private readonly object _frameLock = new object();

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

            lblFps = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 125, 50),
                Location = new Point(608, 18),
                AutoSize = true
            };

            pnlTopBar.Resize += (s, e) => {
                lblFps.Location = new Point(Math.Max(608, pnlTopBar.Width - lblFps.Width - 15), 18);
            };

            pnlTopBar.Controls.Add(lblDev);
            pnlTopBar.Controls.Add(cmbCameraDevices);
            pnlTopBar.Controls.Add(lblRes);
            pnlTopBar.Controls.Add(cmbResolutions);
            pnlTopBar.Controls.Add(btnToggleCamera);
            pnlTopBar.Controls.Add(lblFps);

            // 2. Full Live Viewport (Takes up entire remaining screen)
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
                SizeMode = PictureBoxSizeMode.Zoom
            };
            picCameraLive.Paint += (s, e) => {
                if (picCameraLive.Image == null)
                {
                    string text1 = "LOGITECH BRIO 100 - LIVE CAMERA VIEW";
                    using (Font f1 = new Font("Segoe UI", 15F, FontStyle.Bold))
                    {
                        SizeF s1 = e.Graphics.MeasureString(text1, f1);
                        int cx = picCameraLive.Width / 2;
                        int cy = picCameraLive.Height / 2;
                        e.Graphics.DrawString(text1, f1, Brushes.Gray, cx - s1.Width / 2, cy - s1.Height / 2);
                    }
                }
            };

            pnlViewContainer.Controls.Add(picCameraLive);

            workspaceCamera.Controls.Add(pnlViewContainer);
            workspaceCamera.Controls.Add(pnlTopBar);

            // Refresh camera devices list on initialization
            PopulateCameraDevices();

            // Handle clean stop when form closes
            this.FormClosing += (s, e) => {
                StopCameraLive();
            };

            return workspaceCamera;
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

                _frameCount = 0;
                _lastFpsTime = DateTime.Now;

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
                lblFps.Text = "";

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

                // Tính toán FPS
                _frameCount++;
                double elapsed = (DateTime.Now - _lastFpsTime).TotalSeconds;
                int currentFps = 0;
                if (elapsed >= 1.0)
                {
                    currentFps = (int)Math.Round(_frameCount / elapsed);
                    _frameCount = 0;
                    _lastFpsTime = DateTime.Now;
                }

                if (picCameraLive != null && !picCameraLive.IsDisposed && picCameraLive.IsHandleCreated)
                {
                    picCameraLive.BeginInvoke(new Action(() => {
                        try
                        {
                            var old = picCameraLive.Image;
                            picCameraLive.Image = displayFrame;
                            old?.Dispose();

                            if (currentFps > 0)
                            {
                                lblFps.Text = $"{currentFps} FPS | {displayFrame.Width}x{displayFrame.Height}";
                            }
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
    }
}
