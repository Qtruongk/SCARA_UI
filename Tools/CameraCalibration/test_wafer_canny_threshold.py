"""
Wafer Inspection Test Tool: Canny Edge Detection + Thresholding + Lane/Street Detection
Thiết kế tối ưu cho Camera Stereo ZED 1 và tấm Wafer bán dẫn (Semiconductor Wafer).

Các tính năng chính:
- Tự động nhận diện Camera ZED 1 (Cam 1, cắt frame mắt trái/phải chuẩn).
- Tích hợp đồng thời Canny Edge Detection và đa chế độ Thresholding (Manual, Otsu, Adaptive).
- Bộ lọc CLAHE & Gaussian Blur chuyên biệt để xử lý phản quang / lóa sáng trên bề mặt silicon.
- Tùy chọn phát hiện đường rãnh cắt (Wafer Dicing Streets / Lane Detection) bằng HoughLinesP.
- Dashboard 2x2 trực quan hiển thị song song ảnh Gốc + Canny + Threshold + Tiền xử lý.
- Phím tắt tiện lợi: Chụp snapshot lưu ảnh phục vụ báo cáo/nghiên cứu.
"""

import os
import time
from datetime import datetime
import cv2
import numpy as np

def nothing(x):
    pass

class WaferVisionTester:
    def __init__(self, cam_index=1):
        self.cam_index = cam_index
        self.cap = None
        self.view_mode = "left"  # "left", "right", "stereo"
        self.enable_overlay = True
        self.enable_lines = False  # Phát hiện đường dicing street/lane
        self.show_grid = True      # Dashboard 2x2
        self.save_dir = "snapshots"
        os.makedirs(self.save_dir, exist_ok=True)
        
        self.init_camera()
        self.init_ui()

    def init_camera(self):
        if self.cap is not None and self.cap.isOpened():
            self.cap.release()
            
        print(f"[INFO] Đang khởi tạo Camera (Index {self.cam_index})...")
        # Thử mở với backend cv2.CAP_DSHOW trước (tối ưu cho Logitech Brio 100 Index 1 trên Windows)
        self.cap = cv2.VideoCapture(self.cam_index, cv2.CAP_DSHOW)
        if not self.cap.isOpened():
            self.cap = cv2.VideoCapture(self.cam_index)
        
        # Nếu index chỉ định không mở được, thử index còn lại (0 hoặc 1)
        if not self.cap.isOpened():
            alt_index = 0 if self.cam_index == 1 else 1
            print(f"[CẢNH BÁO] Không mở được Camera {self.cam_index}, thử mở Camera {alt_index}...")
            self.cap = cv2.VideoCapture(alt_index, cv2.CAP_DSHOW)
            if not self.cap.isOpened():
                self.cap = cv2.VideoCapture(alt_index)
            if self.cap.isOpened():
                self.cam_index = alt_index
            else:
                print("[LỖI] Không tìm thấy camera nào khả dụng!")
                return False

        # Thiết lập độ phân giải HD 720p nếu là webcam đơn như Brio 100
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)

        w = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        h = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        print(f"[INFO] Camera {self.cam_index} kết nối thành công: Độ phân giải {w}x{h}")
        if w >= 2 * h:
            print(f"[INFO] Đã phát hiện định dạng ZED Stereo Side-by-Side ({w}x{h}). Mặc định chọn Mắt Trái (Left Eye).")
        return True

    def init_ui(self):
        self.ctrl_win = "Bang Dieu Khien - Wafer Settings"
        cv2.namedWindow(self.ctrl_win, cv2.WINDOW_NORMAL)
        cv2.resizeWindow(self.ctrl_win, 560, 480)

        # Thanh trượt cho Threshold
        # 0: Manual Binary, 1: Otsu Auto, 2: Adaptive Mean, 3: Adaptive Gaussian
        cv2.createTrackbar("Thresh Mode (0:Man, 1:Otsu, 2:AdM, 3:AdG)", self.ctrl_win, 3, 3, nothing)
        cv2.createTrackbar("Threshold Val", self.ctrl_win, 120, 255, nothing)
        cv2.createTrackbar("Adaptive BlockSize", self.ctrl_win, 15, 60, nothing)  # lẻ = 2*k + 1
        cv2.createTrackbar("Invert Binary (0/1)", self.ctrl_win, 0, 1, nothing)

        # Thanh trượt cho Canny Edge
        cv2.createTrackbar("Canny Low Thresh", self.ctrl_win, 30, 255, nothing)
        cv2.createTrackbar("Canny High Thresh", self.ctrl_win, 90, 255, nothing)

        # Thanh trượt Tiền xử lý (Khử lóa hắt sáng & tăng tương phản)
        # 0: Off, 1: Background Division (làm phẳng nền lóa), 2: DoG (Difference of Gaussians)
        cv2.createTrackbar("Anti-Glare Norm (0:Off, 1:Div, 2:DoG)", self.ctrl_win, 1, 2, nothing)
        cv2.createTrackbar("Gaussian Blur Kernel", self.ctrl_win, 1, 10, nothing) # odd = 2*k + 1
        cv2.createTrackbar("CLAHE Contrast (0=Off)", self.ctrl_win, 3, 10, nothing)

        # Phát hiện đường rãnh dicing wafer (Lane detection)
        cv2.createTrackbar("Detect Lanes (0:Off, 1:On)", self.ctrl_win, 0, 1, nothing)

    def get_frame(self):
        if self.cap is None or not self.cap.isOpened():
            return None
        ret, frame = self.cap.read()
        if not ret:
            return None
            
        h, w = frame.shape[:2]
        # Xử lý frame Stereo của ZED 1 (ảnh ghép đôi trái-phải)
        if w >= 2 * h:
            half_w = w // 2
            if self.view_mode == "left":
                return frame[:, :half_w]
            elif self.view_mode == "right":
                return frame[:, half_w:]
            else:
                return frame
        return frame

    def process(self, frame):
        # Đọc giá trị từ Trackbars
        mode = cv2.getTrackbarPos("Thresh Mode (0:Man, 1:Otsu, 2:AdM, 3:AdG)", self.ctrl_win)
        thresh_val = cv2.getTrackbarPos("Threshold Val", self.ctrl_win)
        ad_block = cv2.getTrackbarPos("Adaptive BlockSize", self.ctrl_win)
        inv = cv2.getTrackbarPos("Invert Binary (0/1)", self.ctrl_win)
        canny_low = cv2.getTrackbarPos("Canny Low Thresh", self.ctrl_win)
        canny_high = cv2.getTrackbarPos("Canny High Thresh", self.ctrl_win)
        anti_glare = cv2.getTrackbarPos("Anti-Glare Norm (0:Off, 1:Div, 2:DoG)", self.ctrl_win)
        blur_k = cv2.getTrackbarPos("Gaussian Blur Kernel", self.ctrl_win)
        clahe_clip = cv2.getTrackbarPos("CLAHE Contrast (0=Off)", self.ctrl_win)
        detect_lanes = cv2.getTrackbarPos("Detect Lanes (0:Off, 1:On)", self.ctrl_win)

        # 1. TIỀN XỬ LÝ (PREPROCESSING)
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)

        # KHỬ HẮT SÁNG / LÀM PHẲNG NỀN (ILLUMINATION NORMALIZATION)
        if anti_glare == 1:
            # Thuật toán Background Division: Ước lượng phông sáng không đều bằng Gaussian cực lớn
            # rồi chuẩn hóa (gray / background) để loại bỏ quầng sáng lóa
            bg = cv2.GaussianBlur(gray, (51, 51), 0)
            bg = np.maximum(bg, 1)
            norm = (gray.astype(np.float32) / bg.astype(np.float32)) * 128.0
            norm_gray = np.clip(norm, 0, 255).astype(np.uint8)
            norm_name = "Div Norm"
        elif anti_glare == 2:
            # Thuật toán Difference of Gaussians (DoG): Lọc thông dải (Bandpass) giữ lại rãnh dicing
            g1 = cv2.GaussianBlur(gray, (3, 3), 0)
            g2 = cv2.GaussianBlur(gray, (25, 25), 0)
            norm_gray = cv2.addWeighted(g1, 1.0, g2, -1.0, 128)
            norm_name = "DoG Norm"
        else:
            norm_gray = gray
            norm_name = "Off"

        # Tăng cường độ tương phản rãnh wafer bằng CLAHE
        if clahe_clip > 0:
            clahe = cv2.createCLAHE(clipLimit=float(clahe_clip), tileGridSize=(8, 8))
            gray_enhanced = clahe.apply(norm_gray)
        else:
            gray_enhanced = norm_gray

        # Lọc mịn nhẹ để giữ nguyên rãnh mỏng (tránh làm nhòe mất rãnh wafer)
        ksize = 2 * blur_k + 1
        if ksize > 1:
            blurred = cv2.GaussianBlur(gray_enhanced, (ksize, ksize), 0)
        else:
            blurred = gray_enhanced

        # 2. THRESHOLDING (PHÂN NGƯỠNG)
        bin_type = cv2.THRESH_BINARY_INV if inv else cv2.THRESH_BINARY
        computed_thresh_val = thresh_val

        # Đảm bảo block size cho Adaptive Threshold là số lẻ >= 3
        adaptive_block_size = max(3, 2 * ad_block + 1)

        if mode == 0:  # Manual Binary
            _, thresh = cv2.threshold(blurred, thresh_val, 255, bin_type)
            mode_name = f"Manual ({thresh_val})"
        elif mode == 1:  # Otsu Auto Threshold
            computed_thresh_val, thresh = cv2.threshold(
                blurred, 0, 255, bin_type + cv2.THRESH_OTSU
            )
            mode_name = f"Otsu (Auto={int(computed_thresh_val)})"
        elif mode == 2:  # Adaptive Mean (Chia nhỏ vùng, không sợ hắt sáng)
            thresh = cv2.adaptiveThreshold(
                blurred, 255, cv2.ADAPTIVE_THRESH_MEAN_C, bin_type, adaptive_block_size, 4
            )
            mode_name = f"Adp-Mean (B={adaptive_block_size})"
        else:  # Adaptive Gaussian (Tốt nhất cho bề mặt sáng không đều)
            thresh = cv2.adaptiveThreshold(
                blurred, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, bin_type, adaptive_block_size, 4
            )
            mode_name = f"Adp-Gauss (B={adaptive_block_size})"

        # 3. CANNY EDGE DETECTION (TÌM BIÊN CẠNH)
        c_low = min(canny_low, canny_high)
        c_high = max(canny_low, canny_high)
        canny_edges = cv2.Canny(blurred, c_low, c_high)

        # 4. LANE / DICING STREET DETECTION (HoughLinesP)
        overlay = frame.copy()
        lines_count = 0
        if detect_lanes:
            lines = cv2.HoughLinesP(canny_edges, 1, np.pi / 180, threshold=40,
                                    minLineLength=30, maxLineGap=10)
            if lines is not None:
                lines_count = len(lines)
                for line in lines:
                    x1, y1, x2, y2 = line[0]
                    cv2.line(overlay, (x1, y1), (x2, y2), (0, 0, 255), 2)

        # 5. OVERLAY CANNY LÊN ẢNH GỐC (Viền xanh lục neon)
        if self.enable_overlay:
            overlay[canny_edges > 0] = [0, 255, 0]

        return {
            "raw": frame,
            "gray": gray_enhanced,
            "blurred": blurred,
            "thresh": thresh,
            "canny": canny_edges,
            "overlay": overlay,
            "info": {
                "mode_name": mode_name,
                "canny_range": f"{c_low}-{c_high}",
                "blur_k": ksize,
                "clahe": clahe_clip,
                "anti_glare": norm_name,
                "lines_count": lines_count,
                "detect_lanes": detect_lanes
            }
        }

    def save_snapshot(self, results):
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        raw_path = os.path.join(self.save_dir, f"wafer_raw_{timestamp}.png")
        thresh_path = os.path.join(self.save_dir, f"wafer_thresh_{timestamp}.png")
        canny_path = os.path.join(self.save_dir, f"wafer_canny_{timestamp}.png")
        overlay_path = os.path.join(self.save_dir, f"wafer_overlay_{timestamp}.png")

        cv2.imwrite(raw_path, results["raw"])
        cv2.imwrite(thresh_path, results["thresh"])
        cv2.imwrite(canny_path, results["canny"])
        cv2.imwrite(overlay_path, results["overlay"])

        print(f"[SNAPSHOT] Đã lưu 4 ảnh mẫu vào: {self.save_dir}/ (Thời gian: {timestamp})")
        return timestamp

    def create_dashboard(self, results):
        raw = results["overlay"] if (self.enable_overlay or results["info"]["detect_lanes"]) else results["raw"]
        thresh_bgr = cv2.cvtColor(results["thresh"], cv2.COLOR_GRAY2BGR)
        canny_bgr = cv2.cvtColor(results["canny"], cv2.COLOR_GRAY2BGR)
        gray_bgr = cv2.cvtColor(results["gray"], cv2.COLOR_GRAY2BGR)

        # Tiêu đề trên từng khung hình
        lane_str = f" | Lanes: {results['info']['lines_count']}" if results["info"]["detect_lanes"] else ""
        cv2.putText(raw, f"1. Raw + Canny Overlay [Goc: {self.view_mode.upper()}]{lane_str}", (10, 25),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 2)
        cv2.putText(gray_bgr, f"2. Preprocess (AG={results['info']['anti_glare']}, Blur={results['info']['blur_k']}, CLAHE={results['info']['clahe']})", (10, 25),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.52, (0, 255, 255), 2)
        cv2.putText(thresh_bgr, f"3. Threshold: {results['info']['mode_name']}", (10, 25),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 2)
        cv2.putText(canny_bgr, f"4. Canny Edges [{results['info']['canny_range']}]", (10, 25),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 2)

        # Ghép 4 khung hình thành lưới 2x2
        top_row = np.hstack([raw, gray_bgr])
        bot_row = np.hstack([thresh_bgr, canny_bgr])
        dashboard = np.vstack([top_row, bot_row])
        return dashboard

    def run(self):
        print("="*65)
        print("   KIEM TRA ANH WAFER BANG CANNY & THRESHOLD (ZED 1 CAMERA)")
        print("="*65)
        print("Cac phim tat dieu khien:")
        print("  [q] / [ESC] : Thoat chuong trinh")
        print("  [s]         : Chup & luu bo anh (Raw, Thresh, Canny, Overlay)")
        print("  [v]         : Chuyen goc nhin ZED (Left Eye -> Right Eye -> Stereo)")
        print("  [c]         : Chuyen doi Camera (Cam 1 <-> Cam 0)")
        print("  [o]         : Bat/Tat vien Canny xanh de len anh goc")
        print("  [g]         : Bat/Tat che do Dashboard 2x2 gop chung")
        print("="*65)

        prev_time = time.time()
        snapshot_notify = ""
        notify_timer = 0

        while True:
            frame = self.get_frame()
            if frame is None:
                print("[CANH BAO] Khong nhan duoc frame tu camera. Dang thu ket noi lai...")
                time.sleep(0.3)
                continue

            results = self.process(frame)

            # Do toc do FPS
            curr_time = time.time()
            fps = 1.0 / (curr_time - prev_time) if (curr_time - prev_time) > 0 else 0
            prev_time = curr_time

            if self.show_grid:
                dashboard = self.create_dashboard(results)
                # Thanh trang thai OSD
                dh, dw = dashboard.shape[:2]
                status_bar = np.zeros((35, dw, 3), dtype=np.uint8)
                status_text = f"FPS: {fps:4.1f} | [S] Luu anh | [V] Goc: {self.view_mode} | [O] Overlay | [C] Cam {self.cam_index} | [Q] Thoat"
                if time.time() < notify_timer:
                    status_text = f"*** {snapshot_notify} ***  |  " + status_text
                cv2.putText(status_bar, status_text, (10, 23),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 255, 200), 1)
                full_view = np.vstack([dashboard, status_bar])
                cv2.imshow("Wafer Vision Inspection Dashboard", full_view)
            else:
                cv2.imshow("Wafer Raw + Overlay", results["overlay"] if self.enable_overlay else results["raw"])
                cv2.imshow("Wafer Threshold", results["thresh"])
                cv2.imshow("Wafer Canny Edges", results["canny"])

            key = cv2.waitKey(1) & 0xFF
            if key in [ord('q'), 27]:
                break
            elif key == ord('s'):
                ts = self.save_snapshot(results)
                snapshot_notify = f"DA LUU SNAPSHOT: {ts}"
                notify_timer = time.time() + 2.5
            elif key == ord('v'):
                modes = ["left", "right", "stereo"]
                idx = (modes.index(self.view_mode) + 1) % len(modes)
                self.view_mode = modes[idx]
                print(f"[VIEW] Chuyen goc nhin ZED sang: {self.view_mode.upper()}")
            elif key == ord('c'):
                self.cam_index = 0 if self.cam_index == 1 else 1
                self.init_camera()
            elif key == ord('o'):
                self.enable_overlay = not self.enable_overlay
                print(f"[OVERLAY] Che do ve vien Canny: {'BAT' if self.enable_overlay else 'TAT'}")
            elif key == ord('g'):
                self.show_grid = not self.show_grid
                cv2.destroyAllWindows()
                self.init_ui()

        if self.cap:
            self.cap.release()
        cv2.destroyAllWindows()
        print("[INFO] Da dong camera va thoat chuong trinh.")

if __name__ == "__main__":
    import sys
    # Mac dinh dung Cam 1 (ZED 1). Co the truyen cam_id khac qua terminal: python test_wafer_canny_threshold.py 0
    cam_id = 1
    if len(sys.argv) > 1:
        try:
            cam_id = int(sys.argv[1])
        except ValueError:
            pass
    tester = WaferVisionTester(cam_index=cam_id)
    tester.run()
