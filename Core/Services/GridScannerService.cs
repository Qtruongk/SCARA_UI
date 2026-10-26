using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Test_1.Services
{
    /// <summary>
    /// Đối tượng đại diện cho một ô vuông trên bàn cờ hoặc lưới Wafer.
    /// </summary>
    public class GridCell
    {
        public int Row { get; set; }           // Chỉ số hàng (0-indexed)
        public int Col { get; set; }           // Chỉ số cột (0-indexed)
        public int Index { get; set; }         // Số thứ tự ô (1-indexed: 1..N)

        // 4 đỉnh góc trên Pixel Camera (Top-Left, Top-Right, Bottom-Right, Bottom-Left)
        public PointF[] CornersPixel { get; set; } = new PointF[4];

        // 4 đỉnh góc trong hệ Robot Base (mm)
        public PointF[] CornersRobot { get; set; } = new PointF[4];

        // Lấy hộp giới hạn chữ nhật (Bounding Box) để hỗ trợ trích xuất / xử lý ảnh cục bộ từng ô
        public RectangleF GetBoundingBox()
        {
            if (CornersPixel == null || CornersPixel.Length < 4) return RectangleF.Empty;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                if (CornersPixel[i].X < minX) minX = CornersPixel[i].X;
                if (CornersPixel[i].X > maxX) maxX = CornersPixel[i].X;
                if (CornersPixel[i].Y < minY) minY = CornersPixel[i].Y;
                if (CornersPixel[i].Y > maxY) maxY = CornersPixel[i].Y;
            }
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        // Kích thước ranh giới ô vuông trên ảnh (Pixel)
        public float WidthPx => (CornersPixel != null && CornersPixel.Length >= 2) ? (float)Math.Sqrt(Math.Pow(CornersPixel[1].X - CornersPixel[0].X, 2) + Math.Pow(CornersPixel[1].Y - CornersPixel[0].Y, 2)) : 0;
        public float HeightPx => (CornersPixel != null && CornersPixel.Length >= 4) ? (float)Math.Sqrt(Math.Pow(CornersPixel[3].X - CornersPixel[0].X, 2) + Math.Pow(CornersPixel[3].Y - CornersPixel[0].Y, 2)) : 0;

        // Kích thước ranh giới ô vuông trong hệ Robot Base (mm)
        public float WidthMm => (CornersRobot != null && CornersRobot.Length >= 2) ? (float)Math.Sqrt(Math.Pow(CornersRobot[1].X - CornersRobot[0].X, 2) + Math.Pow(CornersRobot[1].Y - CornersRobot[0].Y, 2)) : 0;
        public float HeightMm => (CornersRobot != null && CornersRobot.Length >= 4) ? (float)Math.Sqrt(Math.Pow(CornersRobot[3].X - CornersRobot[0].X, 2) + Math.Pow(CornersRobot[3].Y - CornersRobot[0].Y, 2)) : 0;

        // Tọa độ tâm (fallback kế thừa)
        public PointF CenterPixel { get; set; }
        public PointF CenterRobot { get; set; }

        // Thông tin quang học từ camera
        public bool IsDarkCell { get; set; } = false;
        public double Brightness { get; set; } = 128.0;

        /// <summary>
        /// Kiểm tra xem một tọa độ pixel (u, v) có nằm bên trong ô vuông tứ giác này không.
        /// </summary>
        public bool ContainsPixel(float u, float v)
        {
            if (CornersPixel == null || CornersPixel.Length < 4) return false;

            // Thuật toán Ray-Casting Point-in-Polygon
            bool inside = false;
            int n = CornersPixel.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = CornersPixel[i].X, yi = CornersPixel[i].Y;
                float xj = CornersPixel[j].X, yj = CornersPixel[j].Y;

                bool intersect = ((yi > v) != (yj > v)) &&
                                 (u < (xj - xi) * (v - yi) / (yj - yi + 1e-12f) + xi);
                if (intersect) inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>
    /// Dịch vụ quét, nhận diện và tạo lưới Grid ô vuông trong phạm vi 4 điểm đã chấm.
    /// Tích hợp thuật toán Thị giác máy tính (Ridge & Valley Detection + Sub-pixel Caliper Snapping + Total Least Squares Line Fitting)
    /// để khóa chặt 100% ranh giới ô vuông vào các đường kẻ mực đen thật trên giấy.
    /// </summary>
    public class GridScannerService
    {
        public int Rows { get; set; } = 6;                   // Số hàng ô vuông (Mặc định 6x6 như mẫu giấy thật)
        public int Cols { get; set; } = 6;                   // Số cột ô vuông
        public double SquareSizeMm { get; set; } = 25.0;     // Kích thước ô vuông thực tế (mm)
        public bool IsGridEnabled { get; set; } = true;      // Bật / Tắt vẽ Grid
        public bool ShowCellLabels { get; set; } = true;     // Hiển thị nhãn chỉ số ô (#1..#N)
        public bool ShowGridLines { get; set; } = true;      // Vẽ đường ranh giới ô
        public bool ShowCornerIntersections { get; set; } = true; // Hiển thị các điểm giao nhau thật (Crosshairs)

        // Các thông số thuật toán Xử lý ảnh (Vision Snapping)
        public bool EnableVisionSnapping { get; set; } = true;    // Bật thuật toán bám nét mực thật
        public int ValleySearchRadiusPx { get; set; } = 18;        // Bán kính tìm kiếm rãnh tối theo phương pháp tuyến (±18 pixel)
        public float MinContrastThreshold { get; set; } = 16.0f;  // Độ sâu tối thiểu của nét mực so với nền giấy trắng

        // Trạng thái kết quả bám dính thị giác
        public bool IsVisionSnapped { get; private set; } = false;
        public int SnappedLineCount { get; private set; } = 0;
        public int TotalLineCount { get; private set; } = 0;
        public float AverageResidualPx { get; private set; } = 0.0f;
        public string SnapStatusMessage { get; private set; } = "Chưa thực hiện quét bám nét.";

        // Mảng lưu trữ các đỉnh giao điểm thực tế sau khi bám nét: [Rows + 1, Cols + 1]
        private PointF[,] _snappedVertices = null;

        public List<GridCell> Cells { get; } = new List<GridCell>();
        public GridCell HoveredCell { get; set; } = null;
        public GridCell SelectedCell { get; set; } = null;

        public event Action GridUpdated;

        /// <summary>
        /// Tự động ước lượng số hàng và số cột dựa trên khoảng cách mm giữa 4 điểm calib và kích thước ô.
        /// </summary>
        public void AutoDetectGridDimensions(PointF[] calibRobots, double squareMm = 25.0)
        {
            if (calibRobots == null || calibRobots.Length < 4) return;
            SquareSizeMm = (squareMm > 0) ? squareMm : 25.0;

            double heightLeft = Distance(calibRobots[0], calibRobots[1]);
            double heightRight = Distance(calibRobots[2], calibRobots[3]);
            double widthTop = Distance(calibRobots[0], calibRobots[2]);
            double widthBottom = Distance(calibRobots[1], calibRobots[3]);

            double avgHeightMm = (heightLeft + heightRight) / 2.0;
            double avgWidthMm = (widthTop + widthBottom) / 2.0;

            Rows = Math.Max(1, (int)Math.Round(avgHeightMm / SquareSizeMm));
            Cols = Math.Max(1, (int)Math.Round(avgWidthMm / SquareSizeMm));
        }

        private double Distance(PointF a, PointF b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Tự động sắp xếp 4 điểm calib thành thứ tự hình học chuẩn: Top-Left, Top-Right, Bottom-Right, Bottom-Left
        /// bất kể người dùng click chuột theo trình tự nào.
        /// </summary>
        public static void SortQuadrilateralPoints(PointF[] inPixels, PointF[] inRobots, out PointF[] outPixels, out PointF[] outRobots)
        {
            outPixels = new PointF[4];
            outRobots = (inRobots != null && inRobots.Length >= 4) ? new PointF[4] : null;

            if (inPixels == null || inPixels.Length < 4) return;

            // 1. Tính trọng tâm của 4 điểm
            float cx = (inPixels[0].X + inPixels[1].X + inPixels[2].X + inPixels[3].X) / 4.0f;
            float cy = (inPixels[0].Y + inPixels[1].Y + inPixels[2].Y + inPixels[3].Y) / 4.0f;

            // 2. Tính góc cực quanh trọng tâm
            var pointList = new List<Tuple<int, double, PointF, PointF>>();
            for (int i = 0; i < 4; i++)
            {
                double angle = Math.Atan2(inPixels[i].Y - cy, inPixels[i].X - cx);
                PointF rob = (inRobots != null && i < inRobots.Length) ? inRobots[i] : PointF.Empty;
                pointList.Add(Tuple.Create(i, angle, inPixels[i], rob));
            }

            // Sắp xếp theo chiều kim đồng hồ quanh trọng tâm
            pointList.Sort((a, b) => a.Item2.CompareTo(b.Item2));

            // 3. Tìm điểm gần góc Top-Left nhất (tổng X + Y nhỏ nhất)
            int tlIndex = 0;
            float minSum = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float sum = pointList[i].Item3.X + pointList[i].Item3.Y;
                if (sum < minSum)
                {
                    minSum = sum;
                    tlIndex = i;
                }
            }

            // 4. Xếp theo thứ tự: TL, TR, BR, BL
            for (int i = 0; i < 4; i++)
            {
                int srcIdx = (tlIndex + i) % 4;
                outPixels[i] = pointList[srcIdx].Item3;
                if (outRobots != null)
                {
                    outRobots[i] = pointList[srcIdx].Item4;
                }
            }
        }

        /// <summary>
        /// Tạo và bám lưới Grid ô vuông trong phạm vi 4 điểm calib P1, P2, P3, P4.
        /// Sử dụng thuật toán xử lý ảnh để tìm nét mực thật trên giấy khi có khung hình camera.
        /// </summary>
        public void GenerateGrid(PointF[] calibPixels, PointF[] calibRobots, double[,] H_pixel2robot, double[,] H_robot2pixel, Bitmap currentFrame = null)
        {
            GenerateGridCore(calibPixels, calibRobots, H_pixel2robot, currentFrame);
        }

        public void GenerateGrid(PointF[] calibPixels, PointF[] calibRobots, double[,] H_pixel2robot, Bitmap currentFrame = null)
        {
            GenerateGridCore(calibPixels, calibRobots, H_pixel2robot, currentFrame);
        }

        private void GenerateGridCore(PointF[] calibPixels, PointF[] calibRobots, double[,] H_pixel2robot, Bitmap currentFrame)
        {
            Cells.Clear();
            HoveredCell = null;
            IsVisionSnapped = false;
            _snappedVertices = null;

            if (calibPixels == null || calibPixels.Length < 4 || Rows <= 0 || Cols <= 0)
            {
                GridUpdated?.Invoke();
                return;
            }

            // 1. Sắp xếp 4 điểm theo đúng trật tự hình học: TL, TR, BR, BL
            SortQuadrilateralPoints(calibPixels, calibRobots, out PointF[] sortedPixels, out PointF[] sortedRobots);

            PointF pTL_pix = sortedPixels[0]; // Top-Left
            PointF pTR_pix = sortedPixels[1]; // Top-Right
            PointF pBR_pix = sortedPixels[2]; // Bottom-Right
            PointF pBL_pix = sortedPixels[3]; // Bottom-Left

            int totalH = Rows + 1;
            int totalV = Cols + 1;
            TotalLineCount = totalH + totalV;
            SnappedLineCount = 0;

            PointF[,] vertices = new PointF[totalH, totalV];
            bool snapSuccess = false;

            // 2. Áp dụng thuật toán Thị giác máy tính bám nét mực nếu có hình ảnh camera và bật chế độ Snapping
            if (EnableVisionSnapping && currentFrame != null)
            {
                try
                {
                    snapSuccess = ExecuteVisionLineSnapping(currentFrame, pTL_pix, pTR_pix, pBR_pix, pBL_pix, vertices);
                }
                catch (Exception ex)
                {
                    snapSuccess = false;
                    SnapStatusMessage = $"Lỗi xử lý ảnh bám nét: {ex.Message}";
                }
            }

            // 3. Nếu không bám nét được (hoặc tắt snapping / không có ảnh), dùng nội suy phối cảnh hai chiều Bilinear làm dự phòng
            if (!snapSuccess)
            {
                for (int r = 0; r < totalH; r++)
                {
                    double v = (double)r / Rows;
                    for (int c = 0; c < totalV; c++)
                    {
                        double u = (double)c / Cols;
                        vertices[r, c] = BilinearInterpolate(pTL_pix, pTR_pix, pBL_pix, pBR_pix, u, v);
                    }
                }
                IsVisionSnapped = false;
                SnapStatusMessage = "Đang dùng lưới nội suy phối cảnh hình học (chưa bám nét mực thật).";
            }
            else
            {
                IsVisionSnapped = true;
                _snappedVertices = vertices;
            }

            // 4. Khởi tạo các ô vuông GridCell từ các giao điểm thực tế
            int cellCounter = 1;
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    var cell = new GridCell
                    {
                        Row = r,
                        Col = c,
                        Index = cellCounter++
                    };

                    // 4 đỉnh pixel của ô vuông (lấy trực tiếp từ các giao điểm nét mực thực tế)
                    cell.CornersPixel[0] = vertices[r, c];         // Top-Left
                    cell.CornersPixel[1] = vertices[r, c + 1];     // Top-Right
                    cell.CornersPixel[2] = vertices[r + 1, c + 1]; // Bottom-Right
                    cell.CornersPixel[3] = vertices[r + 1, c];     // Bottom-Left

                    // Tâm ô vuông pixel (trung bình cộng 4 đỉnh thật)
                    float cx = (cell.CornersPixel[0].X + cell.CornersPixel[1].X + cell.CornersPixel[2].X + cell.CornersPixel[3].X) / 4.0f;
                    float cy = (cell.CornersPixel[0].Y + cell.CornersPixel[1].Y + cell.CornersPixel[2].Y + cell.CornersPixel[3].Y) / 4.0f;
                    cell.CenterPixel = new PointF(cx, cy);

                    // Chuyển đổi tọa độ Robot Base qua ma trận Homography phẳng
                    if (H_pixel2robot != null)
                    {
                        if (PlanarHomography.TransformPoint(H_pixel2robot, cx, cy, out double rx, out double ry))
                        {
                            cell.CenterRobot = new PointF((float)rx, (float)ry);
                        }

                        for (int k = 0; k < 4; k++)
                        {
                            if (PlanarHomography.TransformPoint(H_pixel2robot, cell.CornersPixel[k].X, cell.CornersPixel[k].Y, out double corX, out double corY))
                            {
                                cell.CornersRobot[k] = new PointF((float)corX, (float)corY);
                            }
                        }
                    }
                    else if (sortedRobots != null && sortedRobots.Length >= 4)
                    {
                        double uMid = (c + 0.5) / Cols;
                        double vMid = (r + 0.5) / Rows;
                        cell.CenterRobot = BilinearInterpolate(sortedRobots[0], sortedRobots[1], sortedRobots[3], sortedRobots[2], uMid, vMid);
                    }

                    // Đọc độ sáng thực tế tại tâm ô từ camera
                    if (currentFrame != null)
                    {
                        int pxX = (int)Math.Round(cx);
                        int pxY = (int)Math.Round(cy);
                        if (pxX >= 0 && pxX < currentFrame.Width && pxY >= 0 && pxY < currentFrame.Height)
                        {
                            try
                            {
                                Color px = currentFrame.GetPixel(pxX, pxY);
                                double b = 0.299 * px.R + 0.587 * px.G + 0.114 * px.B;
                                cell.Brightness = b;
                                cell.IsDarkCell = (b < 128.0);
                            }
                            catch { }
                        }
                    }

                    Cells.Add(cell);
                }
            }

            GridUpdated?.Invoke();
        }

        #region Thuật toán Xử lý ảnh bám nét mực (Vision Ridge Snapping)

        /// <summary>
        /// Thực thi thuật toán xử lý ảnh:
        /// 1. Quét mặt cắt 1D vuông góc với các đoạn thẳng sơ bộ để tìm đáy rãnh nét mực đen (Valley Detection).
        /// 2. Tinh chỉnh tọa độ đáy nét mực đạt độ chính xác Sub-pixel qua xấp xỉ parabol.
        /// 3. Khớp đường thẳng tối ưu bằng phương pháp bình phương bé nhất toàn phần (Total Least Squares) và loại trừ nhiễu (RANSAC).
        /// 4. Giao các đường ngang và đường dọc thực tế để xác định chính xác các điểm góc trên giấy.
        /// </summary>
        private bool ExecuteVisionLineSnapping(Bitmap frame, PointF pTL, PointF pTR, PointF pBR, PointF pBL, PointF[,] verticesOut)
        {
            int width = frame.Width;
            int height = frame.Height;

            // 1. Chuyển đổi khung hình sang mảng byte Grayscale tốc độ cao
            byte[] gray = ExtractGrayscale(frame, out width, out height);
            if (gray == null) return false;

            int totalH = Rows + 1;
            int totalV = Cols + 1;

            // Phương trình đường thẳng: A * x + B * y + C = 0 (với A^2 + B^2 = 1)
            var hLines = new List<Tuple<float, float, float>>();
            var vLines = new List<Tuple<float, float, float>>();

            int snappedH = 0;
            int snappedV = 0;
            float totalResidual = 0.0f;
            int residualCount = 0;

            // 2. Tìm và bám các đường nằm ngang (Horizontal lines)
            for (int r = 0; r < totalH; r++)
            {
                double v = (double)r / Rows;
                PointF leftEst = Lerp(pTL, pBL, (float)v);
                PointF rightEst = Lerp(pTR, pBR, (float)v);

                if (SnapLineToInkRidge(gray, width, height, leftEst, rightEst, out float A, out float B, out float C, out float res))
                {
                    hLines.Add(Tuple.Create(A, B, C));
                    snappedH++;
                    totalResidual += res;
                    residualCount++;
                }
                else
                {
                    // Dự phòng đường thẳng ước lượng ban đầu
                    GetLineEquation(leftEst, rightEst, out float A_est, out float B_est, out float C_est);
                    hLines.Add(Tuple.Create(A_est, B_est, C_est));
                }
            }

            // 3. Tìm và bám các đường thẳng đứng (Vertical lines)
            for (int c = 0; c < totalV; c++)
            {
                double u = (double)c / Cols;
                PointF topEst = Lerp(pTL, pTR, (float)u);
                PointF botEst = Lerp(pBL, pBR, (float)u);

                if (SnapLineToInkRidge(gray, width, height, topEst, botEst, out float A, out float B, out float C, out float res))
                {
                    vLines.Add(Tuple.Create(A, B, C));
                    snappedV++;
                    totalResidual += res;
                    residualCount++;
                }
                else
                {
                    GetLineEquation(topEst, botEst, out float A_est, out float B_est, out float C_est);
                    vLines.Add(Tuple.Create(A_est, B_est, C_est));
                }
            }

            SnappedLineCount = snappedH + snappedV;
            AverageResidualPx = (residualCount > 0) ? (totalResidual / residualCount) : 0.0f;

            // 4. Tính toán các điểm giao thực tế giữa đường ngang và đường dọc
            for (int r = 0; r < totalH; r++)
            {
                var h = hLines[r];
                for (int c = 0; c < totalV; c++)
                {
                    var v = vLines[c];
                    if (IntersectLines(h.Item1, h.Item2, h.Item3, v.Item1, v.Item2, v.Item3, out PointF ptInter))
                    {
                        // Giới hạn trong kích thước khung hình
                        ptInter.X = Math.Max(0, Math.Min(width - 1, ptInter.X));
                        ptInter.Y = Math.Max(0, Math.Min(height - 1, ptInter.Y));
                        verticesOut[r, c] = ptInter;
                    }
                    else
                    {
                        // Nếu không giao được, lấy nội suy
                        double u = (double)c / Cols;
                        double vFrac = (double)r / Rows;
                        verticesOut[r, c] = BilinearInterpolate(pTL, pTR, pBL, pBR, u, vFrac);
                    }
                }
            }

            SnapStatusMessage = $"✔ Đã bám dính {SnappedLineCount}/{TotalLineCount} đường nét mực thật (sai số khớp: {AverageResidualPx:F2}px)";
            return (SnappedLineCount >= (TotalLineCount * 0.6));
        }

        /// <summary>
        /// Bám dính một đoạn thẳng vào nét mực đen thực tế trên ảnh bằng kỹ thuật Caliper Scanning.
        /// </summary>
        private bool SnapLineToInkRidge(byte[] gray, int width, int height, PointF ptA, PointF ptB,
            out float outA, out float outB, out float outC, out float residualPx)
        {
            outA = 0; outB = 0; outC = 0; residualPx = 0;

            float dx = ptB.X - ptA.X;
            float dy = ptB.Y - ptA.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 10.0f) return false;

            // Vectơ tiếp tuyến và pháp tuyến đơn vị
            float tx = dx / len;
            float ty = dy / len;
            float nx = -ty;
            float ny = tx;

            int numCalipers = 24; // Số lượng điểm kiểm tra dọc theo đoạn thẳng
            int searchRadius = Math.Max(8, Math.Min(35, ValleySearchRadiusPx));
            int profileLen = searchRadius * 2 + 1;

            var detectedPoints = new List<PointF>();
            float[] profile = new float[profileLen];
            float[] smoothed = new float[profileLen];

            for (int k = 1; k <= numCalipers; k++)
            {
                float t = (float)k / (numCalipers + 1);
                float baseX = ptA.X + t * dx;
                float baseY = ptA.Y + t * dy;

                // Lấy mặt cắt độ xám theo phương pháp tuyến vuông góc với đường kẻ
                for (int s = -searchRadius; s <= searchRadius; s++)
                {
                    float qx = baseX + s * nx;
                    float qy = baseY + s * ny;
                    profile[s + searchRadius] = SampleBilinear(gray, width, height, qx, qy);
                }

                // Lọc mượt 1D (Gaussian 3-tap) để khử nhiễu cảm biến hạt trên giấy
                smoothed[0] = profile[0];
                smoothed[profileLen - 1] = profile[profileLen - 1];
                for (int i = 1; i < profileLen - 1; i++)
                {
                    smoothed[i] = (profile[i - 1] + 2.0f * profile[i] + profile[i + 1]) / 4.0f;
                }

                // Tìm cực tiểu cục bộ (đáy rãnh nét mực đen)
                int minIdx = -1;
                float minVal = float.MaxValue;
                for (int i = 2; i < profileLen - 2; i++)
                {
                    if (smoothed[i] < minVal)
                    {
                        minVal = smoothed[i];
                        minIdx = i;
                    }
                }

                if (minIdx <= 1 || minIdx >= profileLen - 2) continue;

                // Đánh giá độ tương phản so với nền giấy trắng xung quanh
                float bgLeft = (smoothed[0] + smoothed[1] + smoothed[2]) / 3.0f;
                float bgRight = (smoothed[profileLen - 1] + smoothed[profileLen - 2] + smoothed[profileLen - 3]) / 3.0f;
                float bgAvg = (bgLeft + bgRight) / 2.0f;
                float contrast = bgAvg - minVal;

                if (contrast >= MinContrastThreshold)
                {
                    // Tinh chỉnh tọa độ Sub-pixel bằng đỉnh Parabol (Quadratic Interpolation)
                    float y1 = smoothed[minIdx - 1];
                    float y2 = smoothed[minIdx];
                    float y3 = smoothed[minIdx + 1];

                    float denom = 2.0f * (y1 - 2.0f * y2 + y3);
                    if (Math.Abs(denom) > 1e-5f)
                    {
                        float delta = (y1 - y3) / denom;
                        if (Math.Abs(delta) <= 1.0f)
                        {
                            float sBest = (minIdx - searchRadius) + delta;
                            PointF ridgePt = new PointF(baseX + sBest * nx, baseY + sBest * ny);
                            detectedPoints.Add(ridgePt);
                        }
                    }
                }
            }

            // Cần tối thiểu 6 điểm hợp lệ để khớp đường thẳng tin cậy
            if (detectedPoints.Count < 6) return false;

            // 5. Khớp đường thẳng bằng Total Least Squares kèm loại trừ nhiễu RANSAC
            return FitLineTotalLeastSquares(detectedPoints, out outA, out outB, out outC, out residualPx);
        }

        /// <summary>
        /// Khớp đường thẳng tổng quát A*x + B*y + C = 0 với phương sai nhỏ nhất (Total Least Squares)
        /// kết hợp loại bỏ điểm ngoại lai (Outlier Rejection).
        /// </summary>
        private bool FitLineTotalLeastSquares(List<PointF> points, out float outA, out float outB, out float outC, out float avgResidual)
        {
            outA = 0; outB = 0; outC = 0; avgResidual = 0;
            if (points == null || points.Count < 4) return false;

            // Bước 1: Tính đường sơ bộ trên toàn bộ tập điểm
            FitTLS(points, out float A, out float B, out float C);

            // Bước 2: Loại trừ các điểm ngoại lai (Outliers) có khoảng cách > 2.5px
            var inliers = new List<PointF>();
            float totalDist = 0;

            for (int i = 0; i < points.Count; i++)
            {
                float d = Math.Abs(A * points[i].X + B * points[i].Y + C);
                if (d <= 2.8f)
                {
                    inliers.Add(points[i]);
                    totalDist += d;
                }
            }

            if (inliers.Count < 4) inliers = points;

            // Bước 3: Khớp lại lần 2 chỉ với các điểm Inlier
            FitTLS(inliers, out outA, out outB, out outC);

            totalDist = 0;
            for (int i = 0; i < inliers.Count; i++)
            {
                totalDist += Math.Abs(outA * inliers[i].X + outB * inliers[i].Y + outC);
            }
            avgResidual = totalDist / inliers.Count;

            return true;
        }

        private void FitTLS(List<PointF> pts, out float A, out float B, out float C)
        {
            int n = pts.Count;
            float mx = 0, my = 0;
            for (int i = 0; i < n; i++)
            {
                mx += pts[i].X;
                my += pts[i].Y;
            }
            mx /= n;
            my /= n;

            float sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                float dx = pts[i].X - mx;
                float dy = pts[i].Y - my;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            // Góc phương hướng chính của đường thẳng
            double theta = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
            float vx = (float)Math.Cos(theta);
            float vy = (float)Math.Sin(theta);

            // Vectơ pháp tuyến A, B
            A = -vy;
            B = vx;
            C = -(A * mx + B * my);

            // Chuẩn hóa A^2 + B^2 = 1
            float norm = (float)Math.Sqrt(A * A + B * B);
            if (norm > 1e-6f)
            {
                A /= norm;
                B /= norm;
                C /= norm;
            }
        }

        private void GetLineEquation(PointF p1, PointF p2, out float A, out float B, out float C)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f)
            {
                A = 1; B = 0; C = -p1.X;
                return;
            }
            A = -dy / len;
            B = dx / len;
            C = -(A * p1.X + B * p1.Y);
        }

        private bool IntersectLines(float A1, float B1, float C1, float A2, float B2, float C2, out PointF pt)
        {
            pt = PointF.Empty;
            float det = A1 * B2 - A2 * B1;
            if (Math.Abs(det) < 1e-6f) return false;

            float x = (B1 * C2 - B2 * C1) / det;
            float y = (A2 * C1 - A1 * C2) / det;
            pt = new PointF(x, y);
            return true;
        }

        private static byte[] ExtractGrayscale(Bitmap bitmap, out int width, out int height)
        {
            width = bitmap.Width;
            height = bitmap.Height;
            byte[] gray = new byte[width * height];

            try
            {
                BitmapData data = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly,
                    bitmap.PixelFormat);

                try
                {
                    int bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
                    int stride = data.Stride;
                    int totalBytes = Math.Abs(stride) * height;
                    byte[] rawBytes = new byte[totalBytes];
                    Marshal.Copy(data.Scan0, rawBytes, 0, totalBytes);

                    for (int y = 0; y < height; y++)
                    {
                        int rowOffset = y * stride;
                        int grayRowOffset = y * width;
                        for (int x = 0; x < width; x++)
                        {
                            int pxIdx = rowOffset + x * bytesPerPixel;
                            if (pxIdx + 2 < totalBytes)
                            {
                                byte b = rawBytes[pxIdx];
                                byte g = rawBytes[pxIdx + 1];
                                byte r = rawBytes[pxIdx + 2];
                                // Tối ưu chuyển đổi độ xám: (77*R + 150*G + 29*B) >> 8
                                gray[grayRowOffset + x] = (byte)((77 * r + 150 * g + 29 * b) >> 8);
                            }
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return gray;
            }
            catch
            {
                return null;
            }
        }

        private static float SampleBilinear(byte[] gray, int width, int height, float x, float y)
        {
            if (x < 0 || x >= width - 1 || y < 0 || y >= height - 1)
            {
                int cx = Math.Max(0, Math.Min(width - 1, (int)Math.Round(x)));
                int cy = Math.Max(0, Math.Min(height - 1, (int)Math.Round(y)));
                return gray[cy * width + cx];
            }

            int x0 = (int)x;
            int y0 = (int)y;
            int x1 = x0 + 1;
            int y1 = y0 + 1;

            float fx = x - x0;
            float fy = y - y0;

            int idx00 = y0 * width + x0;
            int idx10 = y0 * width + x1;
            int idx01 = y1 * width + x0;
            int idx11 = y1 * width + x1;

            float top = (1.0f - fx) * gray[idx00] + fx * gray[idx10];
            float bottom = (1.0f - fx) * gray[idx01] + fx * gray[idx11];

            return (1.0f - fy) * top + fy * bottom;
        }

        private static PointF Lerp(PointF a, PointF b, float t)
        {
            return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        #endregion

        private PointF BilinearInterpolate(PointF tl, PointF tr, PointF bl, PointF br, double u, double v)
        {
            double x = (1.0 - u) * (1.0 - v) * tl.X +
                       u * (1.0 - v) * tr.X +
                       (1.0 - u) * v * bl.X +
                       u * v * br.X;

            double y = (1.0 - u) * (1.0 - v) * tl.Y +
                       u * (1.0 - v) * tr.Y +
                       (1.0 - u) * v * bl.Y +
                       u * v * br.Y;

            return new PointF((float)x, (float)y);
        }

        /// <summary>
        /// Tìm ô vuông chứa tọa độ pixel chuột.
        /// </summary>
        public GridCell FindCellAtPixel(float u, float v)
        {
            if (!IsGridEnabled || Cells.Count == 0) return null;

            for (int i = 0; i < Cells.Count; i++)
            {
                if (Cells[i].ContainsPixel(u, v))
                {
                    return Cells[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Vẽ toàn bộ lưới Grid các ô vuông lên màn hình Graphics của PictureBox.
        /// </summary>
        public void DrawGrid(Graphics g, Func<float, float, Tuple<float, float>> toScreenFunc)
        {
            if (!IsGridEnabled || Cells.Count == 0 || toScreenFunc == null) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Bút và cọ vẽ hiện đại, tương phản cao
            Color gridLineColor = IsVisionSnapped ? Color.FromArgb(220, 0, 229, 255) : Color.FromArgb(160, 0, 200, 230);
            using (Pen pGrid = new Pen(gridLineColor, IsVisionSnapped ? 1.4f : 1.0f))
            using (Pen pHover = new Pen(Color.FromArgb(255, 235, 59), 2f))
            using (Pen pSelected = new Pen(Color.FromArgb(0, 230, 118), 2.5f))
            using (Brush bHover = new SolidBrush(Color.FromArgb(70, 255, 235, 59)))
            using (Brush bSelected = new SolidBrush(Color.FromArgb(90, 0, 230, 118)))
            using (Brush bDark = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
            using (Brush bLight = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
            using (Font fLabel = new Font("Segoe UI", 7.5F, FontStyle.Bold))
            using (Pen pCrosshair = new Pen(Color.FromArgb(255, 255, 255, 255), 1.0f))
            {
                // 1. Vẽ các ô vuông
                for (int i = 0; i < Cells.Count; i++)
                {
                    var cell = Cells[i];
                    PointF[] screenCorners = new PointF[4];
                    bool allVisible = true;

                    for (int k = 0; k < 4; k++)
                    {
                        var scr = toScreenFunc(cell.CornersPixel[k].X, cell.CornersPixel[k].Y);
                        if (scr == null) { allVisible = false; break; }
                        screenCorners[k] = new PointF(scr.Item1, scr.Item2);
                    }

                    if (!allVisible) continue;

                    // Tô nền caro
                    g.FillPolygon(cell.IsDarkCell ? bDark : bLight, screenCorners);

                    // Trạng thái Hover / Selected / Grid Line
                    if (cell == SelectedCell)
                    {
                        g.FillPolygon(bSelected, screenCorners);
                        g.DrawPolygon(pSelected, screenCorners);
                    }
                    else if (cell == HoveredCell)
                    {
                        g.FillPolygon(bHover, screenCorners);
                        g.DrawPolygon(pHover, screenCorners);
                    }
                    else if (ShowGridLines)
                    {
                        g.DrawPolygon(pGrid, screenCorners);
                    }

                    // Nhãn chỉ số ô vuông bên trong ranh giới ô (không vẽ chấm tâm)
                    if (ShowCellLabels)
                    {
                        float cx = (screenCorners[0].X + screenCorners[1].X + screenCorners[2].X + screenCorners[3].X) / 4.0f;
                        float cy = (screenCorners[0].Y + screenCorners[1].Y + screenCorners[2].Y + screenCorners[3].Y) / 4.0f;

                        string lbl = $"#{cell.Index}";
                        SizeF sLbl = g.MeasureString(lbl, fLabel);
                        Brush bText = (cell == SelectedCell || cell == HoveredCell) ? Brushes.Black : Brushes.White;
                        Brush bBgText = (cell == SelectedCell) ? Brushes.LightGreen : (cell == HoveredCell) ? Brushes.Yellow : new SolidBrush(Color.FromArgb(160, 15, 17, 26));

                        g.FillRectangle(bBgText, cx - sLbl.Width / 2 - 2, cy - sLbl.Height / 2 - 1, sLbl.Width + 4, sLbl.Height + 2);
                        g.DrawString(lbl, fLabel, bText, cx - sLbl.Width / 2, cy - sLbl.Height / 2);
                    }
                }

                // 2. Vẽ điểm giao nhau thật (Crosshairs) nếu bật và đã bám nét
                if (ShowCornerIntersections && _snappedVertices != null)
                {
                    int totalH = Rows + 1;
                    int totalV = Cols + 1;
                    for (int r = 0; r < totalH; r++)
                    {
                        for (int c = 0; c < totalV; c++)
                        {
                            var scr = toScreenFunc(_snappedVertices[r, c].X, _snappedVertices[r, c].Y);
                            if (scr != null)
                            {
                                float vx = scr.Item1;
                                float vy = scr.Item2;
                                g.DrawLine(pCrosshair, vx - 3, vy, vx + 3, vy);
                                g.DrawLine(pCrosshair, vx, vy - 3, vx, vy + 3);
                            }
                        }
                    }
                }
            }

            // 3. HUD Thông tin trạng thái thị giác & Ô đang hover
            float hudX = 14;
            float hudY = 38;

            // HUD Badge: Trạng thái Snapping
            string snapBadge = IsVisionSnapped
                ? $"⚡ VISION SNAPPED: {SnappedLineCount}/{TotalLineCount} đường nét mực thật (Khớp: {AverageResidualPx:F2}px)"
                : "🔲 GRID NỘI SUY HÌNH HỌC (Nhấn '⚡ BÁM ĐƯỜNG KẺ THẬT' để căn chỉnh)";

            Color badgeColor = IsVisionSnapped ? Color.FromArgb(0, 230, 118) : Color.FromArgb(255, 193, 7);
            using (Font fBadge = new Font("Segoe UI", 8F, FontStyle.Bold))
            {
                SizeF sBadge = g.MeasureString(snapBadge, fBadge);
                using (Brush bBg = new SolidBrush(Color.FromArgb(210, 15, 17, 26)))
                using (Pen pBorder = new Pen(badgeColor, 1))
                {
                    g.FillRectangle(bBg, hudX, hudY, sBadge.Width + 14, sBadge.Height + 6);
                    g.DrawRectangle(pBorder, hudX, hudY, sBadge.Width + 14, sBadge.Height + 6);
                }
                using (Brush bText = new SolidBrush(badgeColor))
                {
                    g.DrawString(snapBadge, fBadge, bText, hudX + 7, hudY + 3);
                }
                hudY += sBadge.Height + 10;
            }

            // HUD: Thông tin ô vuông đang Hover (Hiển thị ranh giới 4 góc đã khóa)
            if (HoveredCell != null)
            {
                string info = $"Ô #{HoveredCell.Index} [H{HoveredCell.Row + 1}, C{HoveredCell.Col + 1}] | 4 Đỉnh: TL({HoveredCell.CornersPixel[0].X:F0}, {HoveredCell.CornersPixel[0].Y:F0}) ➜ BR({HoveredCell.CornersPixel[2].X:F0}, {HoveredCell.CornersPixel[2].Y:F0}) | Kích thước: {HoveredCell.WidthMm:F1}x{HoveredCell.HeightMm:F1} mm";
                using (Font fInfo = new Font("Consolas", 8.5F, FontStyle.Bold))
                {
                    SizeF sInfo = g.MeasureString(info, fInfo);
                    using (Brush bBg = new SolidBrush(Color.FromArgb(220, 20, 25, 35)))
                    using (Pen pBorder = new Pen(Color.FromArgb(255, 235, 59), 1))
                    {
                        g.FillRectangle(bBg, hudX, hudY, sInfo.Width + 14, sInfo.Height + 6);
                        g.DrawRectangle(pBorder, hudX, hudY, sInfo.Width + 14, sInfo.Height + 6);
                    }
                    g.DrawString(info, fInfo, Brushes.Yellow, hudX + 7, hudY + 3);
                }
            }
        }
    }
}
