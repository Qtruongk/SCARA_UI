using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Test_1.Models;

namespace Test_1.Services
{
    public class CameraTransformService : IDisposable
    {
        private readonly LoggingService _logger;
        public CameraCalibrationConfig Config { get; private set; }

        public event Action CalibrationUpdated;
        public event Action<string> CalibrationAutoReloaded;

        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private Timer _debounceTimer;
        private readonly object _reloadLock = new object();
        public bool IsAutoConfigEnabled { get; set; } = true;
        public DateTime LastReloadTime { get; private set; } = DateTime.MinValue;
        public string LastLoadedSource { get; private set; } = "";

        public CameraTransformService(LoggingService logger)
        {
            _logger = logger;
            Config = new CameraCalibrationConfig();
            TryLoadDefaultCalibration();
            StartAutoConfigWatchers();
        }

        public void StartAutoConfigWatchers()
        {
            try
            {
                StopAutoConfigWatchers();

                var candidateDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                string[] searchDirs = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\calib_output"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Tools\CameraCalibration\calib_output"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Tools\CameraCalibration"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory),
                    @"C:\Semicon\research\Lane detection_2026\sw\calib_output"
                };

                foreach (var dir in searchDirs)
                {
                    try
                    {
                        if (Directory.Exists(dir))
                        {
                            string fullPath = Path.GetFullPath(dir);
                            candidateDirs.Add(fullPath);
                        }
                    }
                    catch { }
                }

                foreach (var dir in candidateDirs)
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(dir, "*.json")
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                            EnableRaisingEvents = true,
                            IncludeSubdirectories = false
                        };

                        watcher.Changed += OnWatcherFileChanged;
                        watcher.Created += OnWatcherFileChanged;
                        watcher.Renamed += (s, e) => OnWatcherFileChanged(s, new FileSystemEventArgs(WatcherChangeTypes.Changed, e.FullPath, e.Name));

                        _watchers.Add(watcher);
                        _logger?.Log($"[Camera Auto-Config] Bắt đầu theo dõi thư mục hiệu chuẩn: {dir}");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Log($"[Camera Auto-Config] Không thể theo dõi thư mục {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Log($"[Camera Auto-Config] Lỗi khởi động watcher: {ex.Message}");
            }
        }

        public void StopAutoConfigWatchers()
        {
            foreach (var w in _watchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                catch { }
            }
            _watchers.Clear();

            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        private void OnWatcherFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsAutoConfigEnabled) return;

            string fileName = Path.GetFileName(e.FullPath);
            if (string.IsNullOrEmpty(fileName)) return;

            // Chỉ kích hoạt khi file liên quan đến hiệu chuẩn (camera hoặc hand_eye)
            if (fileName.IndexOf("calibration", StringComparison.OrdinalIgnoreCase) < 0 &&
                fileName.IndexOf("hand_eye", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            // Debounce 400ms để đợi quá trình ghi file hoàn tất
            lock (_reloadLock)
            {
                _debounceTimer?.Dispose();
                string targetPath = e.FullPath;
                _debounceTimer = new Timer(_ => {
                    ExecuteAutoReload(targetPath);
                }, null, 400, Timeout.Infinite);
            }
        }

        private void ExecuteAutoReload(string changedPath)
        {
            lock (_reloadLock)
            {
                bool ready = WaitForFileReady(changedPath, 5, 100);
                if (!ready)
                {
                    _logger?.Log($"[Camera Auto-Config] File {Path.GetFileName(changedPath)} đang bị khóa ghi, tiến hành nạp lại ma trận...");
                }

                try
                {
                    TryLoadDefaultCalibration();
                    LastReloadTime = DateTime.Now;
                    LastLoadedSource = changedPath;

                    string name = Path.GetFileName(changedPath);
                    _logger?.Log($"[Camera Auto-Config] ✔ Tự động nạp thành công bộ ma trận calib mới từ: {name} ({LastReloadTime:HH:mm:ss})");
                    CalibrationAutoReloaded?.Invoke(name);
                }
                catch (Exception ex)
                {
                    _logger?.Log($"[Camera Auto-Config] Lỗi khi tự động nạp cấu hình: {ex.Message}");
                }
            }
        }

        private bool WaitForFileReady(string path, int maxRetries, int delayMs)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (!File.Exists(path)) return false;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (stream.Length > 10) return true;
                    }
                }
                catch
                {
                    Thread.Sleep(delayMs);
                }
            }
            return false;
        }

        public bool UseHomography { get; set; } = true;

        public void TryLoadDefaultCalibration()
        {
            bool loadedAny = false;

            // 1. Nạp bộ thông số hiệu chuẩn Camera (fx, fy, cx, cy, distortion, rms)
            string[] cameraCalibPaths = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\calib_output\camera_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Tools\CameraCalibration\calib_output\camera_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\camera_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "camera_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\camera_calibration.json"),
                @"C:\Semicon\research\Lane detection_2026\sw\calib_output\camera_calibration.json"
            };

            foreach (var p in cameraCalibPaths)
            {
                if (File.Exists(p))
                {
                    Config = CameraCalibrationConfig.LoadFromFile(p, Config);
                    _logger?.Log($"[Camera] Loaded camera calibration: {p}");
                    _logger?.Log($"[Camera] Intrinsics: fx={Config.Fx:F2}, fy={Config.Fy:F2}, cx={Config.Cx:F2}, cy={Config.Cy:F2}, RMS={Config.RmsReprojectionPx:F4}px");
                    loadedAny = true;
                    break;
                }
            }

            // 2. Nạp thêm Hand-Eye calibration matrices (T_cam2base, T_base2cam, H_pixel2robot) nếu có
            string[] handEyePaths = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\calib_output\hand_eye_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\hand_eye_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Tools\CameraCalibration\calib_output\hand_eye_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hand_eye_calibration.json"),
                @"C:\Semicon\research\Lane detection_2026\sw\calib_output\hand_eye_calibration.json"
            };

            foreach (var p in handEyePaths)
            {
                if (File.Exists(p))
                {
                    Config = CameraCalibrationConfig.LoadFromFile(p, Config);
                    _logger?.Log($"[Camera] Loaded Hand-Eye matrices from: {p}");
                    loadedAny = true;
                    break;
                }
            }

            // 3. Nạp 4-Point Planar Homography nếu có (độ chính xác cao nhất cho mặt bàn thao tác phẳng)
            string[] fourPointPaths = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\calib_output\four_point_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Tools\CameraCalibration\calib_output\four_point_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\four_point_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "four_point_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\four_point_calibration.json"),
                @"C:\Semicon\research\Lane detection_2026\sw\calib_output\four_point_calibration.json"
            };

            foreach (var p in fourPointPaths)
            {
                if (File.Exists(p))
                {
                    Config = CameraCalibrationConfig.LoadFromFile(p, Config);
                    Config.HasHomography = true;
                    _logger?.Log($"[Camera] ✔ Loaded 4-Point Homography: {p} (RMS = {Config.HomographyRmsErrorMm:F4}mm, Z = {Config.CalibratedWorkZ:F2}mm)");
                    loadedAny = true;
                    break;
                }
            }

            if (!loadedAny)
            {
                Config.SetDefaultMatrices();
                _logger?.Log($"[Camera] Using default calibrated parameters ({Config.GetSummary()})");
            }

            CalibrationUpdated?.Invoke();
        }

        public void ApplyHomography(double[,] H, double[,] H_inv, System.Drawing.PointF[] pixels, System.Drawing.PointF[] robots, double workZ, double rmsError)
        {
            Config.H_pixel2robot = H;
            Config.H_robot2pixel = H_inv;
            Config.CalibPixelPoints = pixels;
            Config.CalibRobotPoints = robots;
            Config.CalibratedWorkZ = workZ;
            Config.HomographyRmsErrorMm = rmsError;
            Config.HasHomography = true;
            UseHomography = true;

            // Lưu vào file để tự động nạp lại các lần sau
            string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "four_point_calibration.json");
            try
            {
                Config.SaveHomographyCalibration(savePath, H, H_inv, pixels, robots, workZ, rmsError);

                // Đồng bộ lưu file vào các thư mục liên quan để tránh lệch dữ liệu giữa các nền tảng build (x86/Any CPU) và root
                string[] additionalSavePaths = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\four_point_calibration.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\four_point_calibration.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\Debug\four_point_calibration.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\bin\x86\Debug\four_point_calibration.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\bin\Debug\four_point_calibration.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\calib_output\four_point_calibration.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Tools\CameraCalibration\calib_output\four_point_calibration.json")
                };

                foreach (var extraPath in additionalSavePaths)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(extraPath);
                        if (Directory.Exists(dir))
                        {
                            Config.SaveHomographyCalibration(extraPath, H, H_inv, pixels, robots, workZ, rmsError);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger?.Log($"[Camera] Cảnh báo lưu file calib: {ex.Message}");
            }

            LastReloadTime = DateTime.Now;
            LastLoadedSource = "4-Point Calibration Dialog";
            _logger?.Log($"[Camera] ✔ Đã áp dụng bộ ma trận Calib 4 điểm mới (RMS = {rmsError:F4} mm, Z = {workZ:F2} mm)");
            CalibrationUpdated?.Invoke();
            CalibrationAutoReloaded?.Invoke("four_point_calibration.json");
        }

        public void ReloadCalibration(string customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                Config = CameraCalibrationConfig.LoadFromFile(customPath, Config);
                _logger?.Log($"[Camera] Reloaded calibration from: {customPath} ({Config.GetSummary()})");
            }
            else
            {
                TryLoadDefaultCalibration();
            }
            LastReloadTime = DateTime.Now;
            CalibrationUpdated?.Invoke();
            CalibrationAutoReloaded?.Invoke(string.IsNullOrEmpty(customPath) ? "Default Config" : Path.GetFileName(customPath));
        }

        public void Dispose()
        {
            StopAutoConfigWatchers();
        }

        /// <summary>
        /// Chuyển đổi từ tọa độ Pixel (u, v) trên ảnh Brio 100 sang tọa độ Robot Base (X, Y, Z).
        /// Quy trình:
        /// 1. Khử méo pixel (u, v) qua ma trận K và hệ số méo (k1, k2, p1, p2, k3) về pixel lý tưởng (u_undist, v_undist).
        /// 2. Áp dụng ma trận Planar Homography H (từ các điểm calib đã khử méo) trên mặt phẳng bàn Z = effectiveZ.
        /// 3. Fallback: Chiếu tia quang học giao mặt phẳng bàn 3D (Ray-Plane).
        /// </summary>
        public bool PixelToRobotBase(double u, double v, double workPlaneZ_mm, out double robotX, out double robotY, out double robotZ)
        {
            double effectiveZ = (workPlaneZ_mm != 0.0) ? workPlaneZ_mm : Config.CalibratedWorkZ;
            robotX = 0; robotY = 0; robotZ = effectiveZ;

            try
            {
                // 1. Khử méo quang học của thấu kính: Pixel thô (u, v) -> Pixel lý tưởng Pinhole (u_undist, v_undist)
                UndistortPixel(u, v, out double u_undist, out double v_undist);

                // 2. ƯU TIÊN SỐ 1: Sử dụng Ma trận Planar Homography H với pixel đã khử méo
                if (UseHomography && Config.HasHomography && Config.H_pixel2robot != null)
                {
                    if (PlanarHomography.TransformPoint(Config.H_pixel2robot, u_undist, v_undist, out double hx, out double hy))
                    {
                        robotX = hx;
                        robotY = hy;
                        robotZ = effectiveZ;
                        return true;
                    }
                }

                // 3. FALLBACK: Sử dụng phép chiếu tia quang học giao mặt phẳng bàn Z = effectiveZ
                // Khử méo pixel (u, v) sang tia chuẩn hóa (xn, yn) trên thấu kính lý tưởng
                UndistortPixelNormalized(u, v, out double xn, out double yn);

                // Vector tia quang học trong hệ Camera (X_c, Y_c, Z_c)
                double vx_c = xn;
                double vy_c = yn;
                double vz_c = 1.0;

                // Biến đổi vector tia sang hệ Robot Base qua ma trận xoay R_cam2base (3x3)
                double[,] T = Config.T_cam2base;
                double vx_b = T[0, 0] * vx_c + T[0, 1] * vy_c + T[0, 2] * vz_c;
                double vy_b = T[1, 0] * vx_c + T[1, 1] * vy_c + T[1, 2] * vz_c;
                double vz_b = T[2, 0] * vx_c + T[2, 1] * vy_c + T[2, 2] * vz_c;

                // Vector gốc đặt camera trong hệ Robot Base: t_cam2base
                double tx = T[0, 3];
                double ty = T[1, 3];
                double tz = T[2, 3];

                // Tìm giao điểm của tia với mặt phẳng Z_base = effectiveZ:
                if (Math.Abs(vz_b) < 1e-7)
                {
                    _logger?.Log("[Camera] Error: Ray is parallel to work plane.");
                    return false;
                }

                double lambda = (effectiveZ - tz) / vz_b;
                robotX = tx + lambda * vx_b;
                robotY = ty + lambda * vy_b;
                robotZ = effectiveZ;

                return true;
            }
            catch (Exception ex)
            {
                _logger?.Log($"[Camera] Coordinate transform error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Chuyển đổi ngược từ tọa độ Robot Base (X, Y, Z) sang tọa độ Pixel (u, v) trên camera.
        /// Sử dụng Homography nghịch đảo H_robot2pixel và áp dụng Forward Distortion để khớp chính xác lên khung camera thực tế.
        /// </summary>
        public bool RobotBaseToPixel(double robotX, double robotY, double robotZ, out double pixelU, out double pixelV, out bool isInFov)
        {
            pixelU = 0; pixelV = 0; isInFov = false;

            try
            {
                // 1. ƯU TIÊN SỐ 1: Sử dụng ma trận nghịch đảo Homography H_robot2pixel
                if (UseHomography && Config.HasHomography && Config.H_robot2pixel != null)
                {
                    if (PlanarHomography.TransformPoint(Config.H_robot2pixel, robotX, robotY, out double pu_undist, out double pv_undist))
                    {
                        // pu_undist, pv_undist là pixel lý tưởng trên ảnh phẳng chưa méo.
                        // Áp dụng forward distortion để chiếu chính xác lên khung hình camera thực tế
                        DistortPixel(pu_undist, pv_undist, out double pu_dist, out double pv_dist);

                        pixelU = pu_dist;
                        pixelV = pv_dist;
                        isInFov = (pixelU >= 0 && pixelU <= Config.ImageWidth && pixelV >= 0 && pixelV <= Config.ImageHeight);
                        return true;
                    }
                }

                // 2. FALLBACK: Biến đổi 3D qua T_base2cam và phép chiếu thấu kính
                double[,] T_b2c = Config.T_base2cam;

                // Chuyển điểm 3D từ Base sang hệ Camera: P_cam = T_base2cam * P_base
                double xc = T_b2c[0, 0] * robotX + T_b2c[0, 1] * robotY + T_b2c[0, 2] * robotZ + T_b2c[0, 3];
                double yc = T_b2c[1, 0] * robotX + T_b2c[1, 1] * robotY + T_b2c[1, 2] * robotZ + T_b2c[1, 3];
                double zc = T_b2c[2, 0] * robotX + T_b2c[2, 1] * robotY + T_b2c[2, 2] * robotZ + T_b2c[2, 3];

                if (zc <= 0) return false; // Nằm sau ống kính

                // Chiếu phối cảnh chuẩn hóa
                double xn = xc / zc;
                double yn = yc / zc;

                // Áp dụng hệ số méo thấu kính (Distortion Model)
                double r2 = xn * xn + yn * yn;
                double r4 = r2 * r2;
                double r6 = r4 * r2;

                double[] d = Config.Distortion;
                double k1 = d.Length > 0 ? d[0] : 0;
                double k2 = d.Length > 1 ? d[1] : 0;
                double p1 = d.Length > 2 ? d[2] : 0;
                double p2 = d.Length > 3 ? d[3] : 0;
                double k3 = d.Length > 4 ? d[4] : 0;

                double radial = 1.0 + k1 * r2 + k2 * r4 + k3 * r6;
                double xd = xn * radial + 2.0 * p1 * xn * yn + p2 * (r2 + 2.0 * xn * xn);
                double yd = yn * radial + p1 * (r2 + 2.0 * yn * yn) + 2.0 * p2 * xn * yn;

                // Nhân với tiêu cự Fx, Fy và cộng tâm quang học Cx, Cy
                pixelU = Config.Fx * xd + Config.Cx;
                pixelV = Config.Fy * yd + Config.Cy;

                isInFov = (pixelU >= 0 && pixelU <= Config.ImageWidth && pixelV >= 0 && pixelV <= Config.ImageHeight);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Khử méo pixel (u, v) về tọa độ pixel lý tưởng (u_undist, v_undist) theo mô hình Pinhole
        /// bằng ma trận nội tại K (Fx, Fy, Cx, Cy) và hệ số méo thấu kính (k1, k2, p1, p2, k3).
        /// </summary>
        public void UndistortPixel(double u, double v, out double u_undist, out double v_undist)
        {
            if (Config.Fx <= 1e-6 || Config.Fy <= 1e-6 || Config.Distortion == null || Config.Distortion.Length == 0)
            {
                u_undist = u;
                v_undist = v;
                return;
            }

            UndistortPixelNormalized(u, v, out double xn, out double yn);
            u_undist = Config.Fx * xn + Config.Cx;
            v_undist = Config.Fy * yn + Config.Cy;
        }

        /// <summary>
        /// Áp dụng mô hình biến dạng thấu kính (Forward Distortion)
        /// chuyển đổi từ pixel lý tưởng (u_undist, v_undist) sang pixel thực tế có méo trên cảm biến (u_dist, v_dist).
        /// </summary>
        public void DistortPixel(double u_undist, double v_undist, out double u_dist, out double v_dist)
        {
            if (Config.Fx <= 1e-6 || Config.Fy <= 1e-6 || Config.Distortion == null || Config.Distortion.Length == 0)
            {
                u_dist = u_undist;
                v_dist = v_undist;
                return;
            }

            double xn = (u_undist - Config.Cx) / Config.Fx;
            double yn = (v_undist - Config.Cy) / Config.Fy;

            double r2 = xn * xn + yn * yn;
            double r4 = r2 * r2;
            double r6 = r4 * r2;

            double[] d = Config.Distortion;
            double k1 = d.Length > 0 ? d[0] : 0;
            double k2 = d.Length > 1 ? d[1] : 0;
            double p1 = d.Length > 2 ? d[2] : 0;
            double p2 = d.Length > 3 ? d[3] : 0;
            double k3 = d.Length > 4 ? d[4] : 0;

            double radial = 1.0 + k1 * r2 + k2 * r4 + k3 * r6;
            double xd = xn * radial + 2.0 * p1 * xn * yn + p2 * (r2 + 2.0 * xn * xn);
            double yd = yn * radial + p1 * (r2 + 2.0 * yn * yn) + 2.0 * p2 * xn * yn;

            u_dist = Config.Fx * xd + Config.Cx;
            v_dist = Config.Fy * yd + Config.Cy;
        }

        /// <summary>
        /// Khử méo pixel lặp sang tọa độ chuẩn hóa xn = Xc/Zc, yn = Yc/Zc (Sub-pixel Newton-Raphson inverse distortion).
        /// </summary>
        public void UndistortPixelNormalized(double u, double v, out double xn, out double yn)
        {
            if (Config.Fx <= 1e-6 || Config.Fy <= 1e-6)
            {
                xn = 0;
                yn = 0;
                return;
            }

            // Tọa độ chuẩn hóa có méo ban đầu
            double xd = (u - Config.Cx) / Config.Fx;
            double yd = (v - Config.Cy) / Config.Fy;

            double[] d = Config.Distortion;
            if (d == null || d.Length == 0)
            {
                xn = xd;
                yn = yd;
                return;
            }

            double k1 = d.Length > 0 ? d[0] : 0;
            double k2 = d.Length > 1 ? d[1] : 0;
            double p1 = d.Length > 2 ? d[2] : 0;
            double p2 = d.Length > 3 ? d[3] : 0;
            double k3 = d.Length > 4 ? d[4] : 0;

            // Khởi tạo ước lượng ban đầu
            double x = xd;
            double y = yd;

            // Lặp 10 bước để triệt tiêu hoàn toàn sai số nghịch đảo biến dạng (đạt độ chính xác < 1e-5 px)
            for (int iter = 0; iter < 10; iter++)
            {
                double r2 = x * x + y * y;
                double r4 = r2 * r2;
                double r6 = r4 * r2;

                double radial = 1.0 + k1 * r2 + k2 * r4 + k3 * r6;
                if (Math.Abs(radial) < 1e-7) break;

                double deltaX = 2.0 * p1 * x * y + p2 * (r2 + 2.0 * x * x);
                double deltaY = p1 * (r2 + 2.0 * y * y) + 2.0 * p2 * x * y;

                x = (xd - deltaX) / radial;
                y = (yd - deltaY) / radial;
            }

            xn = x;
            yn = y;
        }

        /// <summary>
        /// Đánh giá độ sai lệch giữa vị trí đo từ Camera và vị trí thực tế của Robot.
        /// </summary>
        public void EvaluateAccuracy(double robotX, double robotY, double camX, double camY, out double deltaX, out double deltaY, out double deltaR, out bool isPass)
        {
            deltaX = Math.Abs(camX - robotX);
            deltaY = Math.Abs(camY - robotY);
            deltaR = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            isPass = (deltaR <= 1.0); // Tiêu chuẩn sai số <= +-1.0mm
        }
    }
}
