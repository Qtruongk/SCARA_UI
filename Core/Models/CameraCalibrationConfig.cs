using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Test_1.Models
{
    public class CameraCalibrationConfig
    {
        public bool IsLoaded { get; set; }
        public string CameraModel { get; set; } = "Logitech Brio 100";
        public string SourcePath { get; set; } = "";
        public int ImageWidth { get; set; } = 1280;
        public int ImageHeight { get; set; } = 720;
        public int NumSamplesUsed { get; set; } = 15;

        // Intrinsics (Từ kết quả hiệu chuẩn OpenCV Chessboard 15x10, 25mm)
        public double Fx { get; set; } = 1418.51273;
        public double Fy { get; set; } = 1413.97137;
        public double Cx { get; set; } = 615.350285;
        public double Cy { get; set; } = 381.176003;
        public double[] Distortion { get; set; } = new double[] { 0.019560629, 0.787866496, 0.001203839, -0.006260028, -5.148774777 };

        // Calibration Error Metrics
        public double RmsReprojectionPx { get; set; } = 0.9448;
        public double MeanReprojectionPx { get; set; } = 0.7938;

        // Hand-Eye Transformations (4x4)
        public double[,] T_cam2base { get; set; } = new double[4, 4];
        public double[,] T_base2cam { get; set; } = new double[4, 4];
        public double[,] H_pixel2robot { get; set; } = new double[3, 3];
        public double[,] H_robot2pixel { get; set; } = new double[3, 3];

        // 4-Point Planar Homography specific
        public bool HasHomography { get; set; } = false;
        public double CalibratedWorkZ { get; set; } = 0.0;
        public System.Drawing.PointF[] CalibPixelPoints { get; set; } = new System.Drawing.PointF[4];
        public System.Drawing.PointF[] CalibRobotPoints { get; set; } = new System.Drawing.PointF[4];
        public double HomographyRmsErrorMm { get; set; } = 0.0;

        // Residual Accuracy
        public double RmsErrorMm { get; set; } = 0.352;
        public double MaxErrorMm { get; set; } = 0.548;
        public bool IsWithin1MmSpec { get; set; } = true;

        public CameraCalibrationConfig()
        {
            SetDefaultMatrices();
        }

        public void SetDefaultMatrices()
        {
            // Mặc định: Camera nhìn thẳng đứng vuông góc xuống bàn (Z_cam ngược chiều Z_base)
            // R = [[1, 0, 0], [0, -1, 0], [0, 0, -1]], t = [200, 0, 800]
            T_cam2base = new double[4, 4]
            {
                { 1.0,  0.0,  0.0, 200.0 },
                { 0.0, -1.0,  0.0,   0.0 },
                { 0.0,  0.0, -1.0, 800.0 },
                { 0.0,  0.0,  0.0,   1.0 }
            };

            T_base2cam = new double[4, 4]
            {
                { 1.0,  0.0,  0.0, -200.0 },
                { 0.0, -1.0,  0.0,    0.0 },
                { 0.0,  0.0, -1.0,  800.0 },
                { 0.0,  0.0,  0.0,    1.0 }
            };

            H_pixel2robot = new double[3, 3]
            {
                { 0.564, 0.0, 0.0 },
                { 0.0, -0.565, 0.0 },
                { 0.0, 0.0, 1.0 }
            };

            IsLoaded = true;
        }

        public string GetSummary()
        {
            return $"{CameraModel} | {ImageWidth}x{ImageHeight} | fx={Fx:F1}, fy={Fy:F1}, cx={Cx:F1}, cy={Cy:F1} | RMS={RmsReprojectionPx:F4}px (Mean: {MeanReprojectionPx:F4}px)";
        }

        public static CameraCalibrationConfig LoadFromFile(string path, CameraCalibrationConfig existing = null)
        {
            var config = existing ?? new CameraCalibrationConfig();
            if (!File.Exists(path)) return config;

            try
            {
                string json = File.ReadAllText(path);
                config.SourcePath = path;

                // Parse Intrinsics
                config.Fx = ExtractDouble(json, "fx", config.Fx);
                config.Fy = ExtractDouble(json, "fy", config.Fy);
                config.Cx = ExtractDouble(json, "cx", config.Cx);
                config.Cy = ExtractDouble(json, "cy", config.Cy);

                // Parse Image Size & Samples
                config.ImageWidth = (int)ExtractDouble(json, "width", config.ImageWidth);
                config.ImageHeight = (int)ExtractDouble(json, "height", config.ImageHeight);
                config.NumSamplesUsed = (int)ExtractDouble(json, "num_samples_used", config.NumSamplesUsed);

                // Parse Reprojection Error
                config.RmsReprojectionPx = ExtractDouble(json, "rms_error", config.RmsReprojectionPx);
                config.MeanReprojectionPx = ExtractDouble(json, "mean_reprojection_error_px", config.MeanReprojectionPx);

                // Parse Distortion Coefficients
                double[] dist = ExtractDoubleArray(json, "distortion_coeffs_dist") ?? ExtractDoubleArray(json, "distortion");
                if (dist != null && dist.Length >= 4)
                {
                    config.Distortion = dist;
                }

                // Parse 4x4 T_cam2base
                double[,] t_c2b = ExtractMatrix4x4(json, "T_cam2base");
                if (t_c2b != null) config.T_cam2base = t_c2b;

                // Parse 4x4 T_base2cam
                double[,] t_b2c = ExtractMatrix4x4(json, "T_base2cam");
                if (t_b2c != null) config.T_base2cam = t_b2c;

                // Parse 3x3 H
                double[,] h = ExtractMatrix3x3(json, "H_pixel2robot");
                if (h != null)
                {
                    config.H_pixel2robot = h;
                    // Kiểm tra xem H có phải là ma trận mặc định ban đầu hay đã hiệu chuẩn
                    if (Math.Abs(h[0, 2]) > 1e-4 || Math.Abs(h[1, 2]) > 1e-4 || Math.Abs(h[2, 0]) > 1e-6)
                    {
                        config.HasHomography = true;
                    }
                }

                // Parse 3x3 H_robot2pixel
                double[,] h_inv = ExtractMatrix3x3(json, "H_robot2pixel");
                if (h_inv != null)
                {
                    config.H_robot2pixel = h_inv;
                }
                else if (config.HasHomography && config.H_pixel2robot != null)
                {
                    Test_1.Services.PlanarHomography.InvertHomography(config.H_pixel2robot, out double[,] computedInv);
                    config.H_robot2pixel = computedInv;
                }

                config.CalibratedWorkZ = ExtractDouble(json, "work_plane_z_mm", config.CalibratedWorkZ);
                config.HomographyRmsErrorMm = ExtractDouble(json, "homography_rms_error_mm", config.HomographyRmsErrorMm);

                var pxPts = ExtractPointFPairs(json, "pixel_points", "u", "v");
                if (pxPts != null && pxPts.Length >= 4)
                {
                    config.CalibPixelPoints = pxPts;
                }

                var rbPts = ExtractPointFPairs(json, "robot_points", "x", "y");
                if (rbPts != null && rbPts.Length >= 4)
                {
                    config.CalibRobotPoints = rbPts;
                }

                config.RmsErrorMm = ExtractDouble(json, "rms_translation_error_mm", config.RmsErrorMm);
                config.MaxErrorMm = ExtractDouble(json, "max_translation_error_mm", config.MaxErrorMm);
                config.IsWithin1MmSpec = (config.RmsErrorMm <= 1.0 && config.MaxErrorMm <= 1.5);

                config.IsLoaded = true;
            }
            catch
            {
                if (existing == null) config.SetDefaultMatrices();
            }

            return config;
        }

        public void SaveHomographyCalibration(string filePath, double[,] H, double[,] H_inv, System.Drawing.PointF[] pixels, System.Drawing.PointF[] robots, double workZ, double rmsError)
        {
            try
            {
                H_pixel2robot = H;
                H_robot2pixel = H_inv;
                CalibPixelPoints = pixels;
                CalibRobotPoints = robots;
                CalibratedWorkZ = workZ;
                HomographyRmsErrorMm = rmsError;
                HasHomography = true;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"    \"timestamp\": \"{DateTime.Now:yyyy-MM-ddTHH:mm:ss}\",");
                sb.AppendLine($"    \"calibration_type\": \"4_point_planar_homography\",");
                sb.AppendLine($"    \"camera_model\": \"{CameraModel}\",");
                sb.AppendLine($"    \"work_plane_z_mm\": {workZ.ToString("F3", CultureInfo.InvariantCulture)},");
                sb.AppendLine($"    \"homography_rms_error_mm\": {rmsError.ToString("F4", CultureInfo.InvariantCulture)},");
                sb.AppendLine($"    \"is_within_spec\": {(rmsError <= 1.0 ? "true" : "false")},");

                sb.AppendLine("    \"pixel_points\": [");
                for (int i = 0; i < pixels.Length; i++)
                {
                    string comma = (i < pixels.Length - 1) ? "," : "";
                    sb.AppendLine($"        {{ \"point_id\": {i + 1}, \"u\": {pixels[i].X.ToString("F2", CultureInfo.InvariantCulture)}, \"v\": {pixels[i].Y.ToString("F2", CultureInfo.InvariantCulture)} }}{comma}");
                }
                sb.AppendLine("    ],");

                sb.AppendLine("    \"robot_points\": [");
                for (int i = 0; i < robots.Length; i++)
                {
                    string comma = (i < robots.Length - 1) ? "," : "";
                    sb.AppendLine($"        {{ \"point_id\": {i + 1}, \"x\": {robots[i].X.ToString("F3", CultureInfo.InvariantCulture)}, \"y\": {robots[i].Y.ToString("F3", CultureInfo.InvariantCulture)} }}{comma}");
                }
                sb.AppendLine("    ],");

                sb.AppendLine("    \"H_pixel2robot\": [");
                for (int r = 0; r < 3; r++)
                {
                    string comma = (r < 2) ? "," : "";
                    sb.AppendLine($"        [{H[r, 0].ToString("E12", CultureInfo.InvariantCulture)}, {H[r, 1].ToString("E12", CultureInfo.InvariantCulture)}, {H[r, 2].ToString("E12", CultureInfo.InvariantCulture)}]{comma}");
                }
                sb.AppendLine("    ],");

                sb.AppendLine("    \"H_robot2pixel\": [");
                for (int r = 0; r < 3; r++)
                {
                    string comma = (r < 2) ? "," : "";
                    sb.AppendLine($"        [{H_inv[r, 0].ToString("E12", CultureInfo.InvariantCulture)}, {H_inv[r, 1].ToString("E12", CultureInfo.InvariantCulture)}, {H_inv[r, 2].ToString("E12", CultureInfo.InvariantCulture)}]{comma}");
                }
                sb.AppendLine("    ]");
                sb.AppendLine("}");

                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(filePath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Lỗi lưu file Homography: {ex.Message}", ex);
            }
        }

        private static double ExtractDouble(string text, string key, double defaultVal)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*([-+]?[0-9]*\\.?[0-9]+([eE][-+]?[0-9]+)?)");
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            {
                return val;
            }
            return defaultVal;
        }

        private static double[] ExtractDoubleArray(string text, string key)
        {
            try
            {
                int keyIdx = text.IndexOf("\"" + key + "\"");
                if (keyIdx < 0) return null;

                int arrStart = text.IndexOf('[', keyIdx);
                if (arrStart < 0) return null;

                int arrEnd = text.IndexOf(']', arrStart);
                if (arrEnd < 0) return null;

                string sub = text.Substring(arrStart, arrEnd - arrStart + 1);
                MatchCollection matches = Regex.Matches(sub, "[-+]?[0-9]*\\.?[0-9]+([eE][-+]?[0-9]+)?");
                if (matches.Count > 0)
                {
                    double[] arr = new double[matches.Count];
                    for (int i = 0; i < matches.Count; i++)
                    {
                        arr[i] = double.Parse(matches[i].Value, CultureInfo.InvariantCulture);
                    }
                    return arr;
                }
            }
            catch { }
            return null;
        }

        private static double[,] ExtractMatrix4x4(string text, string key)
        {
            try
            {
                int keyIdx = text.IndexOf("\"" + key + "\"");
                if (keyIdx < 0) return null;

                int arrStart = text.IndexOf('[', keyIdx);
                if (arrStart < 0) return null;

                MatchCollection matches = Regex.Matches(text.Substring(arrStart), "[-+]?[0-9]*\\.?[0-9]+([eE][-+]?[0-9]+)?");
                if (matches.Count >= 16)
                {
                    double[,] mat = new double[4, 4];
                    for (int r = 0; r < 4; r++)
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            mat[r, c] = double.Parse(matches[r * 4 + c].Value, CultureInfo.InvariantCulture);
                        }
                    }
                    return mat;
                }
            }
            catch { }
            return null;
        }

        private static double[,] ExtractMatrix3x3(string text, string key)
        {
            try
            {
                int keyIdx = text.IndexOf("\"" + key + "\"");
                if (keyIdx < 0) return null;

                int arrStart = text.IndexOf('[', keyIdx);
                if (arrStart < 0) return null;

                MatchCollection matches = Regex.Matches(text.Substring(arrStart), "[-+]?[0-9]*\\.?[0-9]+([eE][-+]?[0-9]+)?");
                if (matches.Count >= 9)
                {
                    double[,] mat = new double[3, 3];
                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            mat[r, c] = double.Parse(matches[r * 3 + c].Value, CultureInfo.InvariantCulture);
                        }
                    }
                    return mat;
                }
            }
            catch { }
            return null;
        }

        private static System.Drawing.PointF[] ExtractPointFPairs(string text, string key, string xProp, string yProp)
        {
            try
            {
                int keyIdx = text.IndexOf("\"" + key + "\"");
                if (keyIdx < 0) return null;

                int arrStart = text.IndexOf('[', keyIdx);
                if (arrStart < 0) return null;

                int arrEnd = text.IndexOf(']', arrStart);
                if (arrEnd < 0) return null;

                string sub = text.Substring(arrStart, arrEnd - arrStart + 1);
                string pattern = "\"" + xProp + "\"\\s*:\\s*([-+]?[0-9]*\\.?[0-9]+([eE][-+]?[0-9]+)?)[^}]*\"" + yProp + "\"\\s*:\\s*([-+]?[0-9]*\\.?[0-9]+([eE][-+]?[0-9]+)?)";
                MatchCollection matches = Regex.Matches(sub, pattern);
                if (matches.Count >= 4)
                {
                    var pts = new System.Drawing.PointF[matches.Count];
                    for (int i = 0; i < matches.Count; i++)
                    {
                        float x = float.Parse(matches[i].Groups[1].Value, CultureInfo.InvariantCulture);
                        float y = float.Parse(matches[i].Groups[3].Value, CultureInfo.InvariantCulture);
                        pts[i] = new System.Drawing.PointF(x, y);
                    }
                    return pts;
                }
            }
            catch { }
            return null;
        }
    }
}
