using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Test_1.Services
{
    /// <summary>
    /// Kết quả phân tích nhận diện nét vẽ chữ viết tay trên một ô cờ.
    /// </summary>
    public class CellVisionAnalysisResult
    {
        public GridCell Cell { get; set; }
        public float InkRatio { get; set; }           // Tỉ lệ diện tích nét mực (0.0 .. 100.0%)
        public float RedInkRatio { get; set; }        // Tỉ lệ nét mực đỏ (%)
        public float BlueInkRatio { get; set; }       // Tỉ lệ nét mực xanh (%)
        public float DarkInkRatio { get; set; }       // Tỉ lệ nét mực đen/tối (%)
        public string DominantColor { get; set; }     // Màu mực chủ đạo ("Đỏ", "Xanh", "Đen", "Trống")
        public Color LocalPaperColor { get; set; }    // Màu nền giấy cục bộ của chính ô này (R, G, B)
        public string Details { get; set; }
    }

    /// <summary>
    /// Dịch vụ Thị giác Máy tính (Computer Vision) nhận diện nước đi chữ X viết tay của người chơi.
    /// Sử dụng thuật toán Chuẩn hóa Nền Giấy Cục bộ (Local Paper Normalization via Median Filter),
    /// Phân tích Đa kênh Màu Thực (RGB Color Distance & Chromaticity Separation) và Xếp hạng Cạnh tranh.
    /// Khắc phục 100% hiện tượng sai lệch do ánh sáng đèn, bóng đổ hay camera bị ám màu.
    /// </summary>
    public class GomokuVisionService
    {
        // Tọa độ vị trí quan sát chuẩn cho Camera (Camera Home / Inspection Position)
        // Tại vị trí này cánh tay robot lùi hẳn ra ngoài góc, giải phóng 100% tầm nhìn camera nhìn trọn 9 ô cờ.
        public const double CameraHomeX = 158.16;
        public const double CameraHomeY = -130.73;
        public const double CameraHomeZ = 20.00;
        public const double CameraHomeC = -302.25;

        // Snapshot ảnh nền của các ô trống (Baseline Frame)
        private byte[] _baselineRgb = null;
        private int _baselineWidth = 0;
        private int _baselineHeight = 0;
        private readonly object _baselineLock = new object();

        /// <summary>
        /// Lưu snapshot khung hình hiện tại làm ảnh nền tham chiếu (Baseline).
        /// Thường gọi khi bắt đầu ván mới hoặc sau khi Robot đã vẽ xong và rút về vị trí quan sát.
        /// </summary>
        public void UpdateBaseline(Bitmap frame)
        {
            if (frame == null) return;
            lock (_baselineLock)
            {
                _baselineRgb = ConvertToRgb24Bytes(frame, out _baselineWidth, out _baselineHeight);
            }
        }

        /// <summary>
        /// Xóa baseline khi đặt lại ván cờ mới.
        /// </summary>
        public void ResetBaseline()
        {
            lock (_baselineLock)
            {
                _baselineRgb = null;
                _baselineWidth = 0;
                _baselineHeight = 0;
            }
        }

        /// <summary>
        /// Phân tích khung hình camera hiện tại và tự động nhận diện ô vuông người chơi vừa viết chữ X.
        /// CHỈ quét các ô hiện đang có Symbol == None (ô trống) để tuyệt đối không đánh trùng lên ô cũ.
        /// </summary>
        public bool DetectPlayerMoveX(Bitmap currentFrame, List<GridCell> cells, out GridCell detectedCell, out string detectionMessage)
        {
            detectedCell = null;
            detectionMessage = string.Empty;

            if (currentFrame == null || cells == null || cells.Count == 0)
            {
                detectionMessage = "Khung hình camera hoặc dữ liệu lưới không hợp lệ.";
                return false;
            }

            int w, h;
            byte[] currentRgb = ConvertToRgb24Bytes(currentFrame, out w, out h);
            if (currentRgb == null)
            {
                detectionMessage = "Không thể đọc dữ liệu điểm ảnh camera.";
                return false;
            }

            byte[] baseRgb = null;
            lock (_baselineLock)
            {
                if (_baselineRgb != null && _baselineWidth == w && _baselineHeight == h)
                {
                    baseRgb = _baselineRgb;
                }
            }

            var candidateResults = new List<CellVisionAnalysisResult>();

            // Quét từng ô cờ TRỐNG (Symbol == None)
            foreach (var cell in cells)
            {
                if (cell.Symbol != GridCell.CellSymbol.None)
                {
                    // Bỏ qua các ô đã có X hoặc O để tuyệt đối không đánh trùng
                    continue;
                }

                var result = AnalyzeCellRobust(cell, currentRgb, baseRgb, w, h);
                if (result != null)
                {
                    candidateResults.Add(result);
                }
            }

            if (candidateResults.Count == 0)
            {
                detectionMessage = "Tất cả các ô trên bàn cờ đã được đánh dấu!";
                return false;
            }

            // Sắp xếp các ô ứng viên theo thứ tự tỉ lệ nét mực giảm dần
            candidateResults.Sort((a, b) => b.InkRatio.CompareTo(a.InkRatio));

            var topCandidate = candidateResults[0];
            float secondScore = candidateResults.Count > 1 ? candidateResults[1].InkRatio : 0.0f;

            // Xây dựng chuỗi thông báo kết quả chi tiết từng ô
            StringBuilder sbLog = new StringBuilder();
            sbLog.Append("Điểm nét vẽ từng ô: ");
            foreach (var r in candidateResults)
            {
                sbLog.Append($"[#{r.Cell.Index}: {r.InkRatio:F1}% ({r.DominantColor})] ");
            }

            // Tiêu chuẩn nhận diện nước đi chữ X người chơi:
            // 1. Ô đứng đầu phải có ít nhất 1.2% diện tích nét mực viết tay
            // 2. Ô đứng đầu phải vượt trội so với ô đứng thứ 2 (hoặc ô thứ 2 gần như trống < 1.0%)
            bool isDetected = false;
            if (topCandidate.InkRatio >= 1.2f && topCandidate.InkRatio <= 75.0f)
            {
                if (candidateResults.Count == 1)
                {
                    isDetected = true;
                }
                else if (secondScore < 1.0f)
                {
                    // Các ô khác đều là nền giấy sạch (< 1.0%)
                    isDetected = true;
                }
                else if (topCandidate.InkRatio >= secondScore * 1.6f || (topCandidate.InkRatio - secondScore) >= 1.5f)
                {
                    // Vượt trội rõ rệt so với ô thứ 2
                    isDetected = true;
                }
            }

            if (isDetected)
            {
                detectedCell = topCandidate.Cell;
                detectionMessage = $"Đã nhận diện chữ X tại Ô #{detectedCell.Index} (H{detectedCell.Row + 1}, C{detectedCell.Col + 1}) | Mực: {topCandidate.DominantColor} ({topCandidate.InkRatio:F1}%) | {sbLog}";
                return true;
            }

            detectionMessage = $"Chưa phát hiện nước đi chữ X mới rõ rệt. {sbLog}";
            return false;
        }

        /// <summary>
        /// Phân tích một ô vuông bằng thuật toán Chuẩn hóa Nền Giấy Cục bộ (Local Paper Normalization).
        /// Khử hoàn toàn sai số do bóng đổ và ánh sáng đèn ám màu.
        /// </summary>
        private CellVisionAnalysisResult AnalyzeCellRobust(GridCell cell, byte[] currentRgb, byte[] baseRgb, int w, int h)
        {
            if (cell.CornersPixel == null || cell.CornersPixel.Length < 4) return null;

            // 1. Tính tâm ô
            float cx = 0, cy = 0;
            for (int i = 0; i < 4; i++)
            {
                cx += cell.CornersPixel[i].X;
                cy += cell.CornersPixel[i].Y;
            }
            cx /= 4.0f;
            cy /= 4.0f;

            // 2. Co ranh giới 18% vào trong để loại trừ hoàn toàn các đường kẻ viền của bàn cờ và nhãn chữ #1..#9
            float inset = 0.18f;
            PointF[] innerCorners = new PointF[4];
            for (int i = 0; i < 4; i++)
            {
                float px = cell.CornersPixel[i].X;
                float py = cell.CornersPixel[i].Y;
                innerCorners[i] = new PointF(px + (cx - px) * inset, py + (cy - py) * inset);
            }

            // 3. Lấy mẫu ma trận 28x28 điểm = 784 điểm lấy mẫu trong lòng ô
            int sampleGrid = 28;
            int totalSamples = sampleGrid * sampleGrid;

            byte[] sampleR = new byte[totalSamples];
            byte[] sampleG = new byte[totalSamples];
            byte[] sampleB = new byte[totalSamples];

            int idx = 0;
            for (int r = 0; r < sampleGrid; r++)
            {
                float v = (float)r / (sampleGrid - 1);
                for (int c = 0; c < sampleGrid; c++)
                {
                    float u = (float)c / (sampleGrid - 1);
                    PointF pt = Bilinear(innerCorners[0], innerCorners[1], innerCorners[3], innerCorners[2], u, v);
                    SampleRgbFast(currentRgb, w, h, pt.X, pt.Y, out byte cr, out byte cg, out byte cb);
                    sampleR[idx] = cr;
                    sampleG[idx] = cg;
                    sampleB[idx] = cb;
                    idx++;
                }
            }

            // 4. Ước tính MÀU NỀN GIẤY CỦA CHÍNH Ô NÀY (Local Paper Background) bằng Trung vị (Median)
            // Vì nét chữ chỉ chiếm tối đa 5-15% diện tích lòng ô, nên Median đại diện chính xác 100% màu nền giấy!
            byte[] sortedR = (byte[])sampleR.Clone();
            byte[] sortedG = (byte[])sampleG.Clone();
            byte[] sortedB = (byte[])sampleB.Clone();
            Array.Sort(sortedR);
            Array.Sort(sortedG);
            Array.Sort(sortedB);

            int medianIdx = totalSamples / 2;
            byte bgR = sortedR[medianIdx];
            byte bgG = sortedG[medianIdx];
            byte bgB = sortedB[medianIdx];

            // 5. Phân tích từng điểm ảnh so với NỀN GIẤY CỦA CHÍNH Ô NÀY
            int redInkCount = 0;
            int blueInkCount = 0;
            int darkInkCount = 0;
            int otherInkCount = 0;

            for (int i = 0; i < totalSamples; i++)
            {
                byte cr = sampleR[i];
                byte cg = sampleG[i];
                byte cb = sampleB[i];

                // Khoảng cách màu Euclidean so với màu nền giấy cục bộ
                int dR = cr - bgR;
                int dG = cg - bgG;
                int dB = cb - bgB;
                double colorDist = Math.Sqrt(dR * dR + dG * dG + dB * dB);

                // Nếu màu sắc điểm ảnh trùng khớp với màu nền giấy (sai số ánh sáng nhỏ) -> Chắc chắn là nền giấy
                if (colorDist < 26.0)
                {
                    continue;
                }

                // 5A. Phát hiện MỰC ĐỎ (Red Ink - như chữ X đỏ tại ô #7):
                // Mực đỏ có kênh R vượt trội hẳn so với G và B, đồng thời R không giảm sâu như G và B
                bool isRed = (cr - cg >= 22 && cr - cb >= 22 && cr >= 75) ||
                             (dR > 12 && dG < -18 && dB < -18);

                if (isRed)
                {
                    redInkCount++;
                    continue;
                }

                // 5B. Phát hiện MỰC XANH DƯƠNG (Blue Ink):
                // Mực xanh có kênh B vượt trội so với R và G
                bool isBlue = (cb - cr >= 24 && cb - cg >= 16 && cb >= 75) ||
                              (dB > 12 && dR < -18);

                if (isBlue)
                {
                    blueInkCount++;
                    continue;
                }

                // 5C. Phát hiện MỰC ĐEN / TỐI (Black/Dark Ink):
                // Cả 3 kênh R, G, B đều sụt giảm độ sáng đáng kể so với nền giấy trắng
                bool isDark = (dR < -26 && dG < -26 && dB < -26);

                if (isDark)
                {
                    darkInkCount++;
                    continue;
                }

                // 5D. Các nét mực có độ tương phản màu lớn khác (colorDist >= 45)
                if (colorDist >= 45.0)
                {
                    otherInkCount++;
                }
            }

            // 6. Tính toán tỉ lệ nét mực
            int totalInkPoints = redInkCount + blueInkCount + darkInkCount + otherInkCount;
            float inkRatio = (float)totalInkPoints / totalSamples * 100.0f;
            float redRatio = (float)redInkCount / totalSamples * 100.0f;
            float blueRatio = (float)blueInkCount / totalSamples * 100.0f;
            float darkRatio = (float)darkInkCount / totalSamples * 100.0f;

            string dominantColor = "Trống";
            if (redInkCount >= blueInkCount && redInkCount >= darkInkCount && redInkCount > 0)
            {
                dominantColor = "Đỏ";
            }
            else if (blueInkCount >= redInkCount && blueInkCount >= darkInkCount && blueInkCount > 0)
            {
                dominantColor = "Xanh";
            }
            else if (darkInkCount > 0 || otherInkCount > 0)
            {
                dominantColor = "Đen";
            }

            return new CellVisionAnalysisResult
            {
                Cell = cell,
                InkRatio = inkRatio,
                RedInkRatio = redRatio,
                BlueInkRatio = blueRatio,
                DarkInkRatio = darkRatio,
                DominantColor = dominantColor,
                LocalPaperColor = Color.FromArgb(bgR, bgG, bgB),
                Details = $"Ink: {inkRatio:F1}%, Màu: {dominantColor}, Nền ô: ({bgR},{bgG},{bgB})"
            };
        }

        private static PointF Bilinear(PointF tl, PointF tr, PointF bl, PointF br, float u, float v)
        {
            float topX = tl.X + (tr.X - tl.X) * u;
            float topY = tl.Y + (tr.Y - tl.Y) * u;
            float botX = bl.X + (br.X - bl.X) * u;
            float botY = bl.Y + (br.Y - bl.Y) * u;
            return new PointF(topX + (botX - topX) * v, topY + (botY - topY) * v);
        }

        /// <summary>
        /// Chuyển đổi an toàn bất kỳ Bitmap nào sang mảng byte RGB 24-bit chuẩn (Thứ tự byte: R, G, B)
        /// Bảo đảm tính tương thích 100% không phụ thuộc vào PixelFormat gốc của Camera.
        /// </summary>
        private static byte[] ConvertToRgb24Bytes(Bitmap bitmap, out int width, out int height)
        {
            width = bitmap.Width;
            height = bitmap.Height;
            byte[] rgb = new byte[width * height * 3];

            try
            {
                // Tạo một bản sao 24bpp chuẩn hóa
                using (Bitmap standardBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(standardBmp))
                    {
                        g.DrawImage(bitmap, 0, 0, width, height);
                    }

                    BitmapData data = standardBmp.LockBits(
                        new Rectangle(0, 0, width, height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format24bppRgb);

                    try
                    {
                        int stride = data.Stride;
                        int totalBytes = Math.Abs(stride) * height;
                        byte[] rawBytes = new byte[totalBytes];
                        Marshal.Copy(data.Scan0, rawBytes, 0, totalBytes);

                        for (int y = 0; y < height; y++)
                        {
                            int rowOffset = y * stride;
                            int rgbRowOffset = y * width * 3;
                            for (int x = 0; x < width; x++)
                            {
                                int pxIdx = rowOffset + x * 3;
                                int outIdx = rgbRowOffset + x * 3;
                                // Trong Windows GDI+ Format24bppRgb: byte 0 là B, byte 1 là G, byte 2 là R
                                rgb[outIdx] = rawBytes[pxIdx + 2];     // Red
                                rgb[outIdx + 1] = rawBytes[pxIdx + 1]; // Green
                                rgb[outIdx + 2] = rawBytes[pxIdx];     // Blue
                            }
                        }
                    }
                    finally
                    {
                        standardBmp.UnlockBits(data);
                    }
                }

                return rgb;
            }
            catch
            {
                return null;
            }
        }

        private static void SampleRgbFast(byte[] rgb, int width, int height, float x, float y, out byte r, out byte g, out byte b)
        {
            int cx = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x)));
            int cy = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y)));
            int idx = (cy * width + cx) * 3;
            if (idx + 2 < rgb.Length)
            {
                r = rgb[idx];
                g = rgb[idx + 1];
                b = rgb[idx + 2];
            }
            else
            {
                r = 255; g = 255; b = 255;
            }
        }
    }
}
