using System;
using System.IO;
using Test_1.Models;

namespace Test_1.Services
{
    public class CameraTransformService
    {
        private readonly LoggingService _logger;
        public CameraCalibrationConfig Config { get; private set; }

        public event Action CalibrationUpdated;

        public CameraTransformService(LoggingService logger)
        {
            _logger = logger;
            Config = new CameraCalibrationConfig();
            TryLoadDefaultCalibration();
        }

        public void TryLoadDefaultCalibration()
        {
            bool loadedAny = false;

            // 1. Nạp bộ thông số hiệu chuẩn Camera (fx, fy, cx, cy, distortion, rms)
            string[] cameraCalibPaths = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\Tools\CameraCalibration\calib_output\camera_calibration.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Tools\CameraCalibration\calib_output\camera_calibration.json"),
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

            if (!loadedAny)
            {
                Config.SetDefaultMatrices();
                _logger?.Log($"[Camera] Using default calibrated parameters ({Config.GetSummary()})");
            }

            CalibrationUpdated?.Invoke();
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
            CalibrationUpdated?.Invoke();
        }

        /// <summary>
        /// Chuyển đổi từ tọa độ Pixel (u, v) trên ảnh Brio 100 sang tọa độ Robot Base (X, Y, Z)
        /// thông qua khử méo thấu kính và chiếu tia giao với mặt bàn Z = workPlaneZ_mm.
        /// </summary>
        public bool PixelToRobotBase(double u, double v, double workPlaneZ_mm, out double robotX, out double robotY, out double robotZ)
        {
            robotX = 0; robotY = 0; robotZ = workPlaneZ_mm;

            try
            {
                // 1. Khử méo pixel (u, v) sang tia chuẩn hóa (xn, yn) trên thấu kính lý tưởng
                UndistortPixel(u, v, out double xn, out double yn);

                // 2. Vector tia quang học trong hệ Camera (X_c, Y_c, Z_c)
                double vx_c = xn;
                double vy_c = yn;
                double vz_c = 1.0;

                // 3. Biến đổi vector tia sang hệ Robot Base qua ma trận xoay R_cam2base (3x3)
                double[,] T = Config.T_cam2base;
                double vx_b = T[0, 0] * vx_c + T[0, 1] * vy_c + T[0, 2] * vz_c;
                double vy_b = T[1, 0] * vx_c + T[1, 1] * vy_c + T[1, 2] * vz_c;
                double vz_b = T[2, 0] * vx_c + T[2, 1] * vy_c + T[2, 2] * vz_c;

                // Vector gốc đặt camera trong hệ Robot Base: t_cam2base
                double tx = T[0, 3];
                double ty = T[1, 3];
                double tz = T[2, 3];

                // 4. Tìm giao điểm của tia với mặt phẳng Z_base = workPlaneZ_mm:
                // Z_base = tz + lambda * vz_b = workPlaneZ_mm => lambda = (workPlaneZ_mm - tz) / vz_b
                if (Math.Abs(vz_b) < 1e-7)
                {
                    _logger?.Log("[Camera] Error: Ray is parallel to work plane.");
                    return false;
                }

                double lambda = (workPlaneZ_mm - tz) / vz_b;
                robotX = tx + lambda * vx_b;
                robotY = ty + lambda * vy_b;
                robotZ = workPlaneZ_mm;

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
        /// </summary>
        public bool RobotBaseToPixel(double robotX, double robotY, double robotZ, out double pixelU, out double pixelV, out bool isInFov)
        {
            pixelU = 0; pixelV = 0; isInFov = false;

            try
            {
                double[,] T_b2c = Config.T_base2cam;

                // 1. Chuyển điểm 3D từ Base sang hệ Camera: P_cam = T_base2cam * P_base
                double xc = T_b2c[0, 0] * robotX + T_b2c[0, 1] * robotY + T_b2c[0, 2] * robotZ + T_b2c[0, 3];
                double yc = T_b2c[1, 0] * robotX + T_b2c[1, 1] * robotY + T_b2c[1, 2] * robotZ + T_b2c[1, 3];
                double zc = T_b2c[2, 0] * robotX + T_b2c[2, 1] * robotY + T_b2c[2, 2] * robotZ + T_b2c[2, 3];

                if (zc <= 0) return false; // Nằm sau ống kính

                // 2. Chiếu phối cảnh chuẩn hóa
                double xn = xc / zc;
                double yn = yc / zc;

                // 3. Áp dụng hệ số méo thấu kính (Distortion Model)
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

                // 4. Nhân với tiêu cự Fx, Fy và cộng tâm quang học Cx, Cy
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
        /// Khử méo pixel lặp (Newton-Raphson inverse distortion).
        /// </summary>
        private void UndistortPixel(double u, double v, out double xn, out double yn)
        {
            // Tọa độ chuẩn hóa có méo ban đầu
            double xd = (u - Config.Cx) / Config.Fx;
            double yd = (v - Config.Cy) / Config.Fy;

            double[] d = Config.Distortion;
            double k1 = d.Length > 0 ? d[0] : 0;
            double k2 = d.Length > 1 ? d[1] : 0;
            double p1 = d.Length > 2 ? d[2] : 0;
            double p2 = d.Length > 3 ? d[3] : 0;
            double k3 = d.Length > 4 ? d[4] : 0;

            // Khởi tạo ước lượng ban đầu
            double x = xd;
            double y = yd;

            // Lặp 5 bước Newton-Raphson để giải x, y không méo
            for (int iter = 0; iter < 5; iter++)
            {
                double r2 = x * x + y * y;
                double r4 = r2 * r2;
                double r6 = r4 * r2;

                double radial = 1.0 + k1 * r2 + k2 * r4 + k3 * r6;
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
