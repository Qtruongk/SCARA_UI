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
                if (h != null) config.H_pixel2robot = h;

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
    }
}
