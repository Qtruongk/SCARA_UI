using System;
using System.Drawing;

namespace Test_1.Services
{
    /// <summary>
    /// Bộ giải thuật toán biến đổi phẳng phối cảnh Homography 3x3 (Planar Perspective Homography).
    /// Ánh xạ trực tiếp từ tọa độ Pixel (u, v) trên khung hình camera sang tọa độ Robot Base (X, Y)
    /// trên mặt phẳng bàn làm việc Z = workPlaneZ của Robot SCARA.
    /// </summary>
    public static class PlanarHomography
    {
        /// <summary>
        /// Tính ma trận Homography H (3x3) từ 4 cặp điểm tương ứng (pixel -> robot)
        /// sử dụng phương pháp khử Gauss-Jordan với tìm phần tử trục (Partial Pivoting).
        /// </summary>
        public static bool ComputeHomography(PointF[] imagePoints, PointF[] robotPoints, out double[,] H, out double rmsErrorMm)
        {
            H = new double[3, 3];
            rmsErrorMm = double.MaxValue;

            if (imagePoints == null || robotPoints == null || imagePoints.Length < 4 || robotPoints.Length < 4)
            {
                return false;
            }

            // Thiết lập hệ phương trình tuyến tính A * h = b (8 phương trình, 8 ẩn số với h33 = 1)
            // Cho mỗi điểm i (i = 0..3):
            //   h00*u + h01*v + h02 - x*h20*u - x*h21*v = x
            //   h10*u + h11*v + h12 - y*h20*u - y*h21*v = y
            double[,] A = new double[8, 8];
            double[] B = new double[8];

            for (int i = 0; i < 4; i++)
            {
                double u = imagePoints[i].X;
                double v = imagePoints[i].Y;
                double x = robotPoints[i].X;
                double y = robotPoints[i].Y;

                // Phương trình cho trục X
                A[2 * i, 0] = u;
                A[2 * i, 1] = v;
                A[2 * i, 2] = 1.0;
                A[2 * i, 3] = 0.0;
                A[2 * i, 4] = 0.0;
                A[2 * i, 5] = 0.0;
                A[2 * i, 6] = -u * x;
                A[2 * i, 7] = -v * x;
                B[2 * i] = x;

                // Phương trình cho trục Y
                A[2 * i + 1, 0] = 0.0;
                A[2 * i + 1, 1] = 0.0;
                A[2 * i + 1, 2] = 0.0;
                A[2 * i + 1, 3] = u;
                A[2 * i + 1, 4] = v;
                A[2 * i + 1, 5] = 1.0;
                A[2 * i + 1, 6] = -u * y;
                A[2 * i + 1, 7] = -v * y;
                B[2 * i + 1] = y;
            }

            // Giải hệ A * h = B bằng phương pháp khử Gauss-Jordan
            if (!SolveLinearSystem8x8(A, B, out double[] h))
            {
                return false;
            }

            // Điền vào ma trận H 3x3
            H[0, 0] = h[0];
            H[0, 1] = h[1];
            H[0, 2] = h[2];
            H[1, 0] = h[3];
            H[1, 1] = h[4];
            H[1, 2] = h[5];
            H[2, 0] = h[6];
            H[2, 1] = h[7];
            H[2, 2] = 1.0;

            // Đánh giá sai số tái chiếu (Reprojection Error) trên cả 4 điểm
            double sumSqErr = 0.0;
            for (int i = 0; i < 4; i++)
            {
                TransformPoint(H, imagePoints[i].X, imagePoints[i].Y, out double predX, out double predY);
                double dx = predX - robotPoints[i].X;
                double dy = predY - robotPoints[i].Y;
                sumSqErr += (dx * dx + dy * dy);
            }
            rmsErrorMm = Math.Sqrt(sumSqErr / 4.0);

            return true;
        }

        /// <summary>
        /// Biến đổi điểm pixel (u, v) sang tọa độ mặt phẳng robot (X, Y) qua ma trận Homography H.
        /// </summary>
        public static bool TransformPoint(double[,] H, double u, double v, out double x, out double y)
        {
            x = 0; y = 0;
            if (H == null) return false;

            double w = H[2, 0] * u + H[2, 1] * v + H[2, 2];
            if (Math.Abs(w) < 1e-10) return false;

            x = (H[0, 0] * u + H[0, 1] * v + H[0, 2]) / w;
            y = (H[1, 0] * u + H[1, 1] * v + H[1, 2]) / w;
            return true;
        }

        /// <summary>
        /// Nghịch đảo ma trận 3x3 để chuyển đổi ngược lại (Robot -> Pixel).
        /// </summary>
        public static bool InvertHomography(double[,] H, out double[,] H_inv)
        {
            H_inv = new double[3, 3];
            if (H == null) return false;

            double det = H[0, 0] * (H[1, 1] * H[2, 2] - H[1, 2] * H[2, 1])
                       - H[0, 1] * (H[1, 0] * H[2, 2] - H[1, 2] * H[2, 0])
                       + H[0, 2] * (H[1, 0] * H[2, 1] - H[1, 1] * H[2, 0]);

            if (Math.Abs(det) < 1e-12) return false;

            double invDet = 1.0 / det;

            H_inv[0, 0] = (H[1, 1] * H[2, 2] - H[1, 2] * H[2, 1]) * invDet;
            H_inv[0, 1] = (H[0, 2] * H[2, 1] - H[0, 1] * H[2, 2]) * invDet;
            H_inv[0, 2] = (H[0, 1] * H[1, 2] - H[0, 2] * H[1, 1]) * invDet;

            H_inv[1, 0] = (H[1, 2] * H[2, 0] - H[1, 0] * H[2, 2]) * invDet;
            H_inv[1, 1] = (H[0, 0] * H[2, 2] - H[0, 2] * H[2, 0]) * invDet;
            H_inv[1, 2] = (H[0, 2] * H[1, 0] - H[0, 0] * H[1, 2]) * invDet;

            H_inv[2, 0] = (H[1, 0] * H[2, 1] - H[1, 1] * H[2, 0]) * invDet;
            H_inv[2, 1] = (H[0, 1] * H[2, 0] - H[0, 0] * H[2, 1]) * invDet;
            H_inv[2, 2] = (H[0, 0] * H[1, 1] - H[0, 1] * H[1, 0]) * invDet;

            // Chuẩn hóa phần tử góc cuối = 1.0 nếu có thể
            if (Math.Abs(H_inv[2, 2]) > 1e-10)
            {
                double scale = 1.0 / H_inv[2, 2];
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        H_inv[r, c] *= scale;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Giải hệ phương trình tuyến tính A * X = B kích thước 8x8 bằng thuật toán Gauss-Jordan với Partial Pivoting.
        /// </summary>
        private static bool SolveLinearSystem8x8(double[,] A, double[] B, out double[] X)
        {
            int n = 8;
            X = new double[n];

            // Tạo ma trận mở rộng [A | B]
            double[,] M = new double[n, n + 1];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    M[r, c] = A[r, c];
                }
                M[r, n] = B[r];
            }

            // Khử Gauss-Jordan với Partial Pivoting
            for (int col = 0; col < n; col++)
            {
                // Tìm hàng có phần tử trục lớn nhất
                int maxRow = col;
                double maxVal = Math.Abs(M[col, col]);
                for (int row = col + 1; row < n; row++)
                {
                    double val = Math.Abs(M[row, col]);
                    if (val > maxVal)
                    {
                        maxVal = val;
                        maxRow = row;
                    }
                }

                if (maxVal < 1e-12)
                {
                    return false; // Ma trận suy biến (các điểm chọn thẳng hàng hoặc trùng nhau)
                }

                // Hoán đổi hàng
                if (maxRow != col)
                {
                    for (int k = col; k <= n; k++)
                    {
                        double tmp = M[col, k];
                        M[col, k] = M[maxRow, k];
                        M[maxRow, k] = tmp;
                    }
                }

                // Chuẩn hóa hàng trục
                double pivot = M[col, col];
                for (int k = col; k <= n; k++)
                {
                    M[col, k] /= pivot;
                }

                // Khử các hàng khác
                for (int row = 0; row < n; row++)
                {
                    if (row != col)
                    {
                        double factor = M[row, col];
                        if (Math.Abs(factor) > 1e-15)
                        {
                            for (int k = col; k <= n; k++)
                            {
                                M[row, k] -= factor * M[col, k];
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                X[i] = M[i, n];
            }

            return true;
        }
    }
}
