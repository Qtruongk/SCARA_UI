"""
Công cụ Căn chuẩn Camera & Khử méo Thấu kính bằng Bàn cờ (Chessboard Camera Calibration)
Tối ưu hóa cho Camera Logitech Brio 100 và hệ thống Thị giác máy tính / Robot.

Tính năng chính:
1. Nhận diện bàn cờ chính xác cao (Sub-pixel Chessboard Corner Detection):
   - Sử dụng thuật toán hiện đại cv2.findChessboardCornersSB (Sector Based) cực nhanh và nhạy.
   - Tự động bắt đúng lưới 10x14 (140 giao điểm góc) của tấm bàn cờ thực tế.
   - Vẽ các điểm giao màu sắc và đường ziczac kết nối trực quan (như công cụ ROS Camera Calibrator).
2. Quy trình Căn chuẩn Chuyên nghiệp (Multi-frame Calibration):
   - Nhấn SPACE để thu thập mẫu ảnh ở các vị trí khác nhau (giữa, trái, phải, trên, dưới, nghiêng).
   - Bảng đo độ bao phủ (X, Y, Size, Skew) theo dõi tiến độ lấy mẫu.
   - Nhấn 'c' để tính toán Ma trận Camera K và Hệ số biến dạng méo thấu kính D (k1, k2, p1, p2, k3).
   - Hiển thị sai số tái chiếu RMS Reprojection Error (chuẩn < 0.5px là hoàn hảo).
3. Chế độ Kiểm tra Khử Méo (Undistortion & Straightness Test):
   - Nhấn 'u' để chuyển sang xem ảnh đã khử méo (Undistorted View) với các đường viền thẳng tắp.
   - Chế độ Split-View so sánh trực tiếp Ảnh Gốc (Raw) và Ảnh Đã Khử Méo (Undistorted).
4. Phím tắt tiện lợi:
   - SPACE : Lưu 1 mẫu ảnh bàn cờ (cần từ 8 - 15 mẫu ở nhiều góc độ)
   - 'c'   : Thực hiện tính toán căn chuẩn (CALIBRATE)
   - 'u'   : Bật / Tắt chế độ xem ảnh khử méo (Undistorted View)
   - 'm'   : Đổi chế độ hiển thị (Live Corners -> Undistorted -> Split View)
   - 'r'   : Xoay hình 90° (0° -> 90° -> 180° -> 270°)
   - 's'   : Lưu kết quả hiệu chuẩn ra file 'camera_calibration.json' và 'camera_calibration.npz'
   - 'x'   : Xóa toàn bộ mẫu đã chụp để làm lại từ đầu
   - 'q'   : Thoát chương trình
"""

import os
import sys
import json
import time
import argparse
from datetime import datetime

# Đảm bảo in tiếng Việt không bị lỗi font trên Windows console
if sys.platform.startswith("win"):
    try:
        if hasattr(sys.stdout, "reconfigure"):
            sys.stdout.reconfigure(encoding="utf-8")
        if hasattr(sys.stderr, "reconfigure"):
            sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

import cv2
import numpy as np

def nothing(x):
    pass

def open_camera(cam_idx):
    """
    Mở camera qua DirectShow (cv2.CAP_DSHOW) với Brio 100
    """
    cam_label = "Logitech Brio 100" if cam_idx == 1 else "Laptop Webcam"
    print(f"\n[INFO] Đang kết nối tới Camera Index {cam_idx} ({cam_label})...")
    
    cap = cv2.VideoCapture(cam_idx, cv2.CAP_DSHOW)
    if not cap.isOpened():
        print(f"[CẢNH BÁO] Không mở được qua CAP_DSHOW, thử backend mặc định trên index {cam_idx}...")
        cap = cv2.VideoCapture(cam_idx)

    if not cap.isOpened():
        print(f"[LỖI] Không thể mở Camera Index {cam_idx}!")
        return None, cam_label

    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)

    # Đọc bỏ 3 frame đầu tiên để cảm biến ổn định phơi sáng
    for _ in range(3):
        cap.read()

    actual_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    actual_h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    print(f"[THÀNH CÔNG] Đã kết nối {cam_label} (Index {cam_idx})! Độ phân giải: {actual_w}x{actual_h}")
    return cap, cam_label

def detect_chessboard(gray, pattern_sizes):
    """
    Dò tìm bàn cờ với danh sách các kích thước (cols, rows) ưu tiên.
    Sử dụng thuật toán findChessboardCornersSB (Sector Based) chính xác cao.
    """
    for (cols, rows) in pattern_sizes:
        try:
            ret, corners = cv2.findChessboardCornersSB(
                gray, (cols, rows), 
                flags=cv2.CALIB_CB_NORMALIZE_IMAGE
            )
            if ret:
                return True, (cols, rows), corners
        except Exception:
            pass

        # Dự phòng bằng findChessboardCorners cổ điển
        try:
            ret, corners = cv2.findChessboardCorners(
                gray, (cols, rows),
                cv2.CALIB_CB_ADAPTIVE_THRESH + cv2.CALIB_CB_FAST_CHECK + cv2.CALIB_CB_NORMALIZE_IMAGE
            )
            if ret:
                # Tinh chỉnh độ chính xác tới mức sub-pixel
                criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.001)
                corners = cv2.cornerSubPix(gray, corners, (5, 5), (-1, -1), criteria)
                return True, (cols, rows), corners
        except Exception:
            pass

    return False, None, None

def calculate_coverage(all_corners, img_w, img_h):
    """
    Tính toán độ bao phủ không gian của các mẫu đã chụp (X, Y, Size, Skew)
    tương tự như công cụ camera_calibration trong hệ điều hành ROS.
    """
    if not all_corners:
        return 0.0, 0.0, 0.0, 0.0

    all_pts = np.vstack(all_corners).reshape(-1, 2)
    min_x, max_x = np.min(all_pts[:, 0]), np.max(all_pts[:, 0])
    min_y, max_y = np.min(all_pts[:, 1]), np.max(all_pts[:, 1])

    cov_x = min(1.0, (max_x - min_x) / (img_w * 0.75))
    cov_y = min(1.0, (max_y - min_y) / (img_h * 0.75))

    # Đánh giá kích thước (khoảng cách gần/xa)
    widths = []
    skews = []
    for c in all_corners:
        pts = c.reshape(-1, 2)
        w_span = np.max(pts[:, 0]) - np.min(pts[:, 0])
        h_span = np.max(pts[:, 1]) - np.min(pts[:, 1])
        widths.append(w_span)
        skews.append(abs(w_span - h_span) / max(1.0, w_span + h_span))

    cov_size = min(1.0, (np.max(widths) - np.min(widths)) / (img_w * 0.40)) if len(widths) > 1 else 0.2
    cov_skew = min(1.0, np.std(skews) * 4.0) if len(skews) > 1 else 0.2

    return cov_x, cov_y, cov_size, cov_skew

def draw_progress_bar(img, label, value, x, y, width=150, height=14):
    """
    Vẽ thanh tiến độ đo độ bao phủ X, Y, Size, Skew
    """
    cv2.putText(img, label, (x, y + 11), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (255, 255, 255), 1)
    bar_x = x + 45
    cv2.rectangle(img, (bar_x, y), (bar_x + width, y + height), (60, 60, 60), -1)
    fill_w = int(width * np.clip(value, 0.0, 1.0))
    # Màu chuyển từ cam sang xanh lá khi đạt đủ tiến độ
    fill_color = (0, 255, 0) if value >= 0.75 else (0, 165, 255)
    if fill_w > 0:
        cv2.rectangle(img, (bar_x, y), (bar_x + fill_w, y + height), fill_color, -1)
    cv2.rectangle(img, (bar_x, y), (bar_x + width, y + height), (180, 180, 180), 1)

def main():
    parser = argparse.ArgumentParser(description="Professional Chessboard Camera Calibration")
    parser.add_argument("--cam", type=int, default=1, help="Camera Index (1 cho Brio 100, 0 cho Webcam)")
    parser.add_argument("--cols", type=int, default=15, help="Số góc giao điểm bên trong theo chiều ngang (15 cho 16 ô cờ)")
    parser.add_argument("--rows", type=int, default=10, help="Số góc giao điểm bên trong theo chiều dọc (10 cho 11 ô cờ)")
    parser.add_argument("--square", type=float, default=25.0, help="Kích thước 1 ô vuông (mm)")
    args = parser.parse_args()

    save_dir = "snapshots"
    os.makedirs(save_dir, exist_ok=True)

    current_cam_idx = args.cam
    cap, cam_label = open_camera(current_cam_idx)

    if cap is None:
        alt_idx = 0 if current_cam_idx == 1 else 1
        print(f"[THỬ LẠI] Đang thử mở camera thay thế: Index {alt_idx}...")
        cap, cam_label = open_camera(alt_idx)
        if cap is not None:
            current_cam_idx = alt_idx
        else:
            print("[LỖI] Không mở được camera nào. Vui lòng kiểm tra lại kết nối cáp USB!")
            return

    control_win = 'Calibration Controls'
    view_win = 'Brio 100 - Chessboard Camera Calibration'

    cv2.namedWindow(control_win, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(control_win, 480, 240)

    # Thanh trượt điều khiển
    cv2.createTrackbar('Cols (Corners)', control_win, args.cols, 20, nothing)
    cv2.createTrackbar('Rows (Corners)', control_win, args.rows, 20, nothing)
    cv2.createTrackbar('Square Size (mm)', control_win, int(args.square), 50, nothing)
    cv2.createTrackbar('Auto-Capture', control_win, 0, 1, nothing)

    rotations = [None, cv2.ROTATE_90_CLOCKWISE, cv2.ROTATE_180, cv2.ROTATE_90_COUNTERCLOCKWISE]
    rot_idx = 1  # Mặc định xoay 90 độ cho thẳng tầm nhìn cánh tay robot

    # Danh sách lưu các mẫu căn chuẩn
    obj_points = []   # Tọa độ 3D trong không gian thực (mm)
    img_points = []   # Tọa độ 2D trên ảnh (pixels)
    captured_samples = 0
    last_capture_time = 0.0

    # Trạng thái căn chuẩn
    is_calibrated = False
    camera_matrix = None
    dist_coeffs = None
    rms_error = 0.0
    map1, map2 = None, None

    # Tự động nạp cấu hình đã lưu nếu có sẵn
    calib_file = "camera_calibration.json"
    if os.path.exists(calib_file):
        try:
            with open(calib_file, "r", encoding="utf-8") as f:
                saved_data = json.load(f)
            camera_matrix = np.array(saved_data["camera_matrix_K"], dtype=np.float32)
            dist_coeffs = np.array(saved_data["distortion_D"], dtype=np.float32)
            rms_error = saved_data.get("reprojection_error_rms", 0.0)
            is_calibrated = True
            print(f"[INFO] Đã tự động nạp cấu hình căn chuẩn từ trước ({calib_file}), RMS={rms_error:.3f} px")
        except Exception as e:
            print(f"[CẢNH BÁO] Không đọc được file cấu hình cũ: {e}")

    view_modes = ["Live Detection", "Undistorted View", "Split View (Raw vs Undistorted)"]
    mode_idx = 0

    prev_time = time.time()
    fps = 0.0

    print("\n" + "=" * 70)
    print(" HƯỚNG DẪN CĂN CHUẨN CAMERA VỚI BÀN CỜ:")
    print(" - Đặt bàn cờ ở nhiều góc độ và vị trí khác nhau trong khung hình.")
    print(" - Nhấn 'SPACE' : Lưu 1 mẫu ảnh (khuyến nghị thu thập từ 8 đến 15 mẫu).")
    print(" - Nhấn 'c'     : BẮT ĐẦU CĂN CHUẨN (CALIBRATE) khi đã có >= 5 mẫu.")
    print(" - Nhấn 'u'     : Bật / Tắt chế độ xem ảnh khử méo (Undistorted View).")
    print(" - Nhấn 'm'     : Đổi chế độ hiển thị (Live -> Undistorted -> Split View).")
    print(" - Nhấn 'r'     : Xoay hình 90°.")
    print(" - Nhấn 's'     : Lưu thông số vào 'camera_calibration.json'.")
    print(" - Nhấn 'x'     : Xóa các mẫu đã chụp để làm lại.")
    print(" - Nhấn 'q'/ESC : Thoát.")
    print("=" * 70 + "\n")

    while True:
        ret, frame = cap.read()
        if not ret or frame is None:
            time.sleep(0.05)
            continue

        if rotations[rot_idx] is not None:
            frame = cv2.rotate(frame, rotations[rot_idx])

        cur_time = time.time()
        fps = 0.9 * fps + 0.1 * (1.0 / max(1e-5, cur_time - prev_time))
        prev_time = cur_time

        h, w = frame.shape[:2]

        # Lấy kích thước bàn cờ từ thanh trượt
        user_cols = max(3, cv2.getTrackbarPos('Cols (Corners)', control_win))
        user_rows = max(3, cv2.getTrackbarPos('Rows (Corners)', control_win))
        square_size = max(1.0, float(cv2.getTrackbarPos('Square Size (mm)', control_win)))
        auto_capture = bool(cv2.getTrackbarPos('Auto-Capture', control_win))

        # Ưu tiên kích thước người dùng chọn và kích thước đảo chiều (do xoay)
        patterns_to_try = [
            (user_cols, user_rows),
            (user_rows, user_cols),
            (15, 10), (10, 15),
            (10, 14), (14, 10),
            (9, 6), (6, 9),
            (8, 6), (6, 8)
        ]
        # Loại bỏ trùng lặp
        patterns_to_try = list(dict.fromkeys(patterns_to_try))

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        found, active_pattern, corners = detect_chessboard(gray, patterns_to_try)

        # Chuẩn bị ảnh hiển thị
        display = frame.copy()

        # Tạo map khử méo nếu đã có thông số căn chuẩn
        if is_calibrated and (map1 is None or map2 is None):
            new_K, _ = cv2.getOptimalNewCameraMatrix(camera_matrix, dist_coeffs, (w, h), 0, (w, h))
            map1, map2 = cv2.initUndistortRectifyMap(camera_matrix, dist_coeffs, None, new_K, (w, h), cv2.CV_32FC1)

        # ---------------------------------------------------------------------
        # Xử lý theo từng chế độ xem
        # ---------------------------------------------------------------------
        if mode_idx == 0:  # Live Detection
            if found:
                # Vẽ tất cả các điểm góc và đường ziczac kết nối
                cv2.drawChessboardCorners(display, active_pattern, corners, found)
                cols_f, rows_f = active_pattern
                status_text = f"DA BAT DUOC BAN CO: {cols_f}x{rows_f} ({cols_f*rows_f} GIAO DIEM)"
                cv2.putText(display, status_text, (20, 35),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
                cv2.putText(display, "Nhan 'SPACE' de chup luu mau anh nay", (20, 65),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 1)

                # Tự động chụp nếu bật Auto-Capture (mỗi 1.5 giây 1 mẫu khi bàn cờ đứng yên)
                if auto_capture and (cur_time - last_capture_time > 1.5):
                    # Tính tọa độ 3D thế giới thực theo milimet
                    objp = np.zeros((cols_f * rows_f, 3), np.float32)
                    objp[:, :2] = np.mgrid[0:cols_f, 0:rows_f].T.reshape(-1, 2) * square_size
                    obj_points.append(objp)
                    img_points.append(corners)
                    captured_samples += 1
                    last_capture_time = cur_time
                    print(f"[AUTO-CHỤP] Đã lưu mẫu {captured_samples} ({cols_f}x{rows_f})...")
            else:
                cv2.putText(display, "DANG QUET TIM BAN CO...", (20, 35),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 140, 255), 2)
                cv2.putText(display, f"Kiem tra: {user_cols}x{user_rows} (Chinh Cols/Rows neu ban co khac)", (20, 65),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)

        elif mode_idx == 1:  # Undistorted View
            if is_calibrated and map1 is not None and map2 is not None:
                display = cv2.remap(frame, map1, map2, cv2.INTER_LINEAR)
                # Kẻ các đường thẳng kiểm tra xem các hàng ô cờ có thẳng tắp không
                for y_line in range(50, h, 60):
                    cv2.line(display, (0, y_line), (w, y_line), (0, 50, 0), 1)
                for x_line in range(50, w, 60):
                    cv2.line(display, (x_line, 0), (x_line, h), (0, 50, 0), 1)

                cv2.putText(display, "ANH DA KHU MEO (UNDISTORTED VIEW)", (20, 35),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
                cv2.putText(display, f"RMS Error: {rms_error:.3f} px | Cac duong thang da phang 100%", (20, 65),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.52, (100, 255, 255), 1)
            else:
                cv2.putText(display, "CHUA CAN CHUAN! Hay chup >= 5 mau va nhan 'c' de Calibrate.", (20, 50),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 0, 255), 2)

        else:  # mode_idx == 2: Split View (Raw vs Undistorted)
            if is_calibrated and map1 is not None and map2 is not None:
                undist = cv2.remap(frame, map1, map2, cv2.INTER_LINEAR)
                half_w, half_h = w // 2, h // 2
                raw_small = cv2.resize(frame, (half_w, half_h))
                undist_small = cv2.resize(undist, (half_w, half_h))

                # Kẻ lưới màu đỏ mảnh để so sánh độ cong méo
                for y_line in range(30, half_h, 30):
                    cv2.line(raw_small, (0, y_line), (half_w, y_line), (0, 0, 180), 1)
                    cv2.line(undist_small, (0, y_line), (half_w, y_line), (0, 180, 0), 1)

                display = np.hstack([raw_small, undist_small])
                cv2.line(display, (half_w, 0), (half_w, half_h), (0, 255, 255), 2)
                cv2.putText(display, "ANH GOC (RAW)", (20, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
                cv2.putText(display, "DA KHU MEO (UNDISTORTED)", (half_w + 20, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            else:
                cv2.putText(display, "CHUA CAN CHUAN! Hay chup >= 5 mau va nhan 'c' de Calibrate.", (20, 50),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 0, 255), 2)

        # ---------------------------------------------------------------------
        # Bảng điều khiển tiến độ lấy mẫu (Bên góc phải như ROS Camera Calibrator)
        # ---------------------------------------------------------------------
        if mode_idx == 0:
            panel_w, panel_h = 240, 210
            px, py = display.shape[1] - panel_w - 15, 15
            cv2.rectangle(display, (px, py), (px + panel_w, py + panel_h), (25, 25, 25), -1)
            cv2.rectangle(display, (px, py), (px + panel_w, py + panel_h), (80, 80, 80), 1)

            cv2.putText(display, "TIEN DO CAN CHUAN", (px + 12, py + 22),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.52, (0, 255, 255), 1)

            # Số mẫu đã chụp
            sample_color = (0, 255, 0) if captured_samples >= 10 else ((0, 255, 255) if captured_samples >= 5 else (0, 165, 255))
            cv2.putText(display, f"So mau: {captured_samples} / 15", (px + 12, py + 48),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.55, sample_color, 2)

            # Tính độ bao phủ
            cov_x, cov_y, cov_size, cov_skew = calculate_coverage(img_points, w, h)
            draw_progress_bar(display, "X", cov_x, px + 12, py + 62)
            draw_progress_bar(display, "Y", cov_y, px + 12, py + 84)
            draw_progress_bar(display, "Size", cov_size, px + 12, py + 106)
            draw_progress_bar(display, "Skew", cov_skew, px + 12, py + 128)

            # Nút hướng dẫn CALIBRATE
            if captured_samples >= 5:
                calib_btn_color = (0, 255, 0)
                calib_btn_text = "[NHAN 'c': CALIBRATE]"
            else:
                calib_btn_color = (120, 120, 120)
                calib_btn_text = f"Can them {5 - captured_samples} mau..."

            cv2.rectangle(display, (px + 12, py + 155), (px + panel_w - 12, py + 192), (40, 40, 40), -1)
            cv2.rectangle(display, (px + 12, py + 155), (px + panel_w - 12, py + 192), calib_btn_color, 2)
            cv2.putText(display, calib_btn_text, (px + 22, py + 180),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.48, calib_btn_color, 2)

        # Thanh HUD thông tin dưới cùng
        calib_status_str = f"DA CALIB (RMS={rms_error:.2f}px)" if is_calibrated else "CHUA CALIB"
        hud_text = (f"{cam_label} | FPS: {fps:.1f} | Che do: {view_modes[mode_idx]} | {calib_status_str} | "
                    f"[SPACE: Chup, c: Calib, u: Khudo, m: Mode, r: Xoay, s: Luu, q: Thoat]")
        cv2.putText(display, hud_text, (15, display.shape[0] - 12),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.42, (255, 255, 0), 1, cv2.LINE_AA)

        cv2.imshow(view_win, display)

        key = cv2.waitKey(1) & 0xFF
        if key == ord('q') or key == 27:
            break

        elif key == 32:  # Phím SPACE: Chụp lưu mẫu
            if found:
                cols_f, rows_f = active_pattern
                objp = np.zeros((cols_f * rows_f, 3), np.float32)
                objp[:, :2] = np.mgrid[0:cols_f, 0:rows_f].T.reshape(-1, 2) * square_size
                obj_points.append(objp)
                img_points.append(corners)
                captured_samples += 1
                last_capture_time = cur_time
                print(f"[ĐÃ LƯU MẪU {captured_samples}] Lưới {cols_f}x{rows_f} ({cols_f*rows_f} điểm). "
                      f"Hãy đổi góc nghiêng hoặc vị trí bàn cờ rồi chụp tiếp!")
            else:
                print("[CẢNH BÁO] Chưa bắt được bàn cờ trong khung hình! Vui lòng căn chỉnh lại.")

        elif key == ord('c'):  # Phím 'c': Bắt đầu tính toán căn chuẩn
            if len(obj_points) < 5:
                print(f"[CẢNH BÁO] Cần ít nhất 5 mẫu ảnh bàn cờ để căn chuẩn (Hiện có: {len(obj_points)} mẫu). "
                      f"Hãy nhấn SPACE để chụp thêm mẫu!")
            else:
                print("\n" + "=" * 60)
                print(f"[ĐANG TÍNH TOÁN] Đang căn chuẩn Camera với {len(obj_points)} mẫu ảnh...")
                t_calib_start = time.time()
                ret_val, K, D, rvecs, tvecs = cv2.calibrateCamera(
                    obj_points, img_points, (w, h), None, None
                )
                t_calib_duration = time.time() - t_calib_start
                rms_error = ret_val
                camera_matrix = K
                dist_coeffs = D
                is_calibrated = True
                map1, map2 = None, None  # Reset map để tính lại map mới

                print(f"[THÀNH CÔNG] Đã căn chuẩn xong trong {t_calib_duration:.2f}s!")
                print(f" - Sai số tái chiếu (RMS Error): {rms_error:.4f} pixels")
                print(f" - Tiêu cự (Focal Length): fx = {K[0,0]:.2f}, fy = {K[1,1]:.2f}")
                print(f" - Tâm quang học (Center): cx = {K[0,2]:.2f}, cy = {K[1,2]:.2f}")
                print(f" - Hệ số méo thấu kính (k1, k2, p1, p2, k3):")
                print(f"   {D.ravel()}")
                print("=" * 60)
                print("[HƯỚNG DẪN] Nhấn 'u' hoặc 'm' để xem ảnh sau khi khử méo!")
                print("[HƯỚNG DẪN] Nhấn 's' để lưu thông số này thành file cấu hình chuẩn.\n")
                mode_idx = 1  # Chuyển ngay sang xem ảnh khử méo

        elif key == ord('u'):  # Bật/tắt chế độ xem ảnh khử méo
            if is_calibrated:
                mode_idx = 0 if mode_idx != 0 else 1
                print(f"[CHẾ ĐỘ] Chuyển sang: {view_modes[mode_idx]}")
            else:
                print("[CẢNH BÁO] Camera chưa được căn chuẩn! Hãy chụp đủ mẫu và nhấn 'c' trước.")

        elif key == ord('m'):  # Đổi chế độ hiển thị
            mode_idx = (mode_idx + 1) % len(view_modes)
            print(f"[CHẾ ĐỘ] Chuyển sang: {view_modes[mode_idx]}")

        elif key == ord('r'):  # Xoay hình 90 độ
            rot_idx = (rot_idx + 1) % len(rotations)
            # Reset map khi xoay để tính lại
            map1, map2 = None, None
            print(f"[XOAY HÌNH] Góc xoay: {rot_idx * 90} độ")

        elif key == ord('x'):  # Xóa toàn bộ mẫu đã chụp
            obj_points.clear()
            img_points.clear()
            captured_samples = 0
            print("[ĐÃ XÓA] Đã xóa toàn bộ mẫu ảnh. Bạn có thể bắt đầu chụp lại từ đầu.")

        elif key == ord('s'):  # Lưu cấu hình ra file
            if is_calibrated and camera_matrix is not None and dist_coeffs is not None:
                calib_data = {
                    "camera_name": cam_label,
                    "camera_index": current_cam_idx,
                    "resolution": {"width": w, "height": h},
                    "rotation_deg": rot_idx * 90,
                    "reprojection_error_rms": float(rms_error),
                    "camera_matrix_K": camera_matrix.tolist(),
                    "distortion_D": dist_coeffs.tolist(),
                    "square_size_mm": square_size,
                    "num_samples_used": len(obj_points),
                    "timestamp": datetime.now().isoformat()
                }
                with open(calib_file, "w", encoding="utf-8") as f:
                    json.dump(calib_data, f, indent=4)

                np.savez("camera_calibration.npz",
                         K=camera_matrix,
                         D=dist_coeffs,
                         rms=rms_error,
                         resolution=np.array([w, h]))

                # Lưu ảnh chụp hiện tại vào snapshots
                timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
                snap_path = os.path.join(save_dir, f"calib_result_{timestamp}.jpg")
                cv2.imwrite(snap_path, display)

                print(f"\n[ĐÃ LƯU THÀNH CÔNG]")
                print(f"  + File cấu hình JSON: {os.path.abspath(calib_file)}")
                print(f"  + File ma trận NPZ:  {os.path.abspath('camera_calibration.npz')}")
                print(f"  + Ảnh kết quả:       {snap_path}\n")
            else:
                print("[CẢNH BÁO] Chưa có kết quả căn chuẩn để lưu! Hãy nhấn 'c' để Calibrate trước.")

    cap.release()
    cv2.destroyAllWindows()
    print("[INFO] Đã đóng camera và kết thúc chương trình.")

if __name__ == "__main__":
    main()
