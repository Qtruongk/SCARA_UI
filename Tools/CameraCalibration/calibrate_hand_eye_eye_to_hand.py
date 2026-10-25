# -*- coding: utf-8 -*-
"""
Chương trình Căn chuẩn Hand-Eye Cố định (Eye-to-Hand Calibration)
Chuyên biệt cho Camera Logitech Brio 100 và Robot SCARA THL300.

Chức năng:
1. Nạp ma trận nội tại K và hệ số méo thấu kính từ 'camera_calibration.json'.
2. Mở camera Logitech Brio 100 (1280x720) ở chế độ thời gian thực.
3. Thu thập 15-20 vị trí đồng bộ giữa:
   - Khâu cuối Robot: [X, Y, Z, C] (mm, độ)
   - Bảng cờ gắn trên Robot: rvec, tvec qua cv2.solvePnP()
4. Tính toán Ma trận biến đổi thuần nhất 4x4 T_cam2base, T_base2cam và Homography H_pixel2robot.
5. Kiểm tra sai số đối chiếu (Residual Error) và đánh giá tiêu chuẩn sai số <= 1.0mm.
6. Xuất kết quả ra file 'hand_eye_calibration.json' và báo cáo chi tiết.
"""

import os
import sys
import json
import time
import argparse
from datetime import datetime

# Hỗ trợ UTF-8 console Windows
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

# Import bộ giải toán Hand-Eye
from hand_eye_solver import (
    euler_scara_to_rot_matrix,
    make_transform_matrix,
    invert_transform,
    solve_scara_hand_eye_unified,
    compute_planar_homography_robot,
    pixel_to_robot_base,
    robot_base_to_pixel
)


DEFAULT_CALIB_PATH = r"C:\Semicon\research\Lane detection_2026\sw\calib_output\camera_calibration.json"


def load_camera_intrinsics(json_path=DEFAULT_CALIB_PATH):
    """Nạp ma trận K và hệ số méo thấu kính từ file JSON đã căn chuẩn."""
    if not os.path.exists(json_path):
        # Thử tìm ở thư mục hiện tại
        local_path = os.path.join(os.path.dirname(__file__), "camera_calibration.json")
        if os.path.exists(local_path):
            json_path = local_path
        else:
            raise FileNotFoundError(f"Không tìm thấy file nội tại camera: {json_path}")

    with open(json_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    K = np.array(data["camera_matrix_K"], dtype=np.float64)
    dist = np.array(data["distortion_coeffs_dist"], dtype=np.float64).flatten()
    img_w = int(data.get("image_size", {}).get("width", 1280))
    img_h = int(data.get("image_size", {}).get("height", 720))
    square_size_mm = float(data.get("square_size_mm", 25.0))

    print(f"[OK] Đã nạp nội tại camera từ: {json_path}")
    print(f"     fx={K[0,0]:.2f}, fy={K[1,1]:.2f}, cx={K[0,2]:.2f}, cy={K[1,2]:.2f}")
    print(f"     Distortion coeffs ({len(dist)}): {dist}")
    print(f"     Độ phân giải: {img_w}x{img_h}, Kích thước ô cờ: {square_size_mm} mm")

    return K, dist, img_w, img_h, square_size_mm


def make_chessboard_3d_points(pattern_size, square_size_mm):
    """Tạo tọa độ 3D của các giao điểm trên mặt phẳng bảng cờ (Z = 0)."""
    cols, rows = pattern_size
    objp = np.zeros((rows * cols, 3), np.float32)
    objp[:, :2] = np.mgrid[0:cols, 0:rows].T.reshape(-1, 2) * square_size_mm
    return objp


def detect_chessboard_pose(frame_bgr, pattern_size, square_size_mm, K, dist):
    """
    Tìm bàn cờ với độ chính xác sub-pixel và giải PnP để lấy R_target2cam, t_target2cam.
    """
    gray = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY)
    cols, rows = pattern_size

    # Thử tìm bằng thuật toán hiện đại Sector-Based
    ret, corners = cv2.findChessboardCornersSB(gray, (cols, rows), flags=cv2.CALIB_CB_NORMALIZE_IMAGE)
    if not ret:
        ret, corners = cv2.findChessboardCorners(
            gray, (cols, rows),
            cv2.CALIB_CB_ADAPTIVE_THRESH + cv2.CALIB_CB_FAST_CHECK + cv2.CALIB_CB_NORMALIZE_IMAGE
        )
        if ret:
            criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.001)
            corners = cv2.cornerSubPix(gray, corners, (11, 11), (-1, -1), criteria)

    if not ret or corners is None:
        return False, None, None, None

    objp = make_chessboard_3d_points(pattern_size, square_size_mm)

    # Giải PnP với mô hình thấu kính đã biết
    success, rvec, tvec = cv2.solvePnP(objp, corners, K, dist, flags=cv2.SOLVEPNP_ITERATIVE)
    if not success:
        return False, None, None, None

    R, _ = cv2.Rodrigues(rvec)
    T_target2cam = make_transform_matrix(R, tvec)

    return True, T_target2cam, corners, (rvec, tvec)


def run_interactive_calibration():
    """Quy trình chạy thu thập mẫu và căn chuẩn tương tác."""
    print("=" * 65)
    print(" CĂN CHUẨN HAND-EYE (EYE-TO-HAND) ROBOT SCARA & BRIO 100")
    print("=" * 65)

    K, dist, img_w, img_h, square_size_mm = load_camera_intrinsics()

    # Mở camera Brio 100
    cam_idx = 0
    print(f"\n[INFO] Đang mở Camera Logitech Brio 100 (Index {cam_idx})...")
    cap = cv2.VideoCapture(cam_idx, cv2.CAP_DSHOW)
    if not cap.isOpened():
        cap = cv2.VideoCapture(cam_idx)

    if not cap.isOpened():
        print(f"[CẢNH BÁO] Không mở được Camera Index {cam_idx}, thử Index 1...")
        cam_idx = 1
        cap = cv2.VideoCapture(cam_idx, cv2.CAP_DSHOW)
        if not cap.isOpened():
            cap = cv2.VideoCapture(cam_idx)

    if not cap.isOpened():
        print("[LỖI] Không thể kết nối với Camera Logitech Brio 100!")
        print("Vui lòng cắm cáp USB hoặc kiểm tra quyền truy cập Camera.")
        return

    cap.set(cv2.CAP_PROP_FRAME_WIDTH, img_w)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, img_h)

    pattern_sizes_to_try = [(15, 10), (10, 14), (9, 6), (7, 7)]
    detected_pattern = pattern_sizes_to_try[0]

    captured_samples = []  # Lưu các cặp (T_gripper2base, T_target2cam, robot_pose, corners)

    print("\n--- HƯỚNG DẪN THAO TÁC ---")
    print("1. Gắn bảng cờ chắc chắn lên đầu khâu cuối (Gripper) của Robot SCARA.")
    print("2. Di chuyển Robot đến các vị trí khác nhau trong vùng nhìn thấy của Brio 100.")
    print("3. Nhấn phím SPACE trên cửa sổ camera để chụp mẫu tại vị trí hiện tại.")
    print("   Nhập tọa độ Robot [X, Y, Z, C] (mm, độ) từ màn hình điều khiển.")
    print("4. Khi đã thu thập từ 10 - 20 mẫu, nhấn phím 'c' để BẮT ĐẦU TÍNH TOÁN.")
    print("5. Nhấn phím 'q' để thoát.\n")

    cv2.namedWindow("Hand-Eye Calibration - Logitech Brio 100", cv2.WINDOW_NORMAL)
    cv2.resizeWindow("Hand-Eye Calibration - Logitech Brio 100", 1024, 576)

    while True:
        ret, frame = cap.read()
        if not ret:
            print("[CẢNH BÁO] Không đọc được frame từ camera.")
            time.sleep(0.1)
            continue

        display_frame = frame.copy()

        # Dò tìm bàn cờ live
        found = False
        active_corners = None
        current_T_t2c = None
        for ps in pattern_sizes_to_try:
            ok, T_t2c, corners, pnp_res = detect_chessboard_pose(frame, ps, square_size_mm, K, dist)
            if ok:
                found = True
                active_corners = corners
                current_T_t2c = T_t2c
                detected_pattern = ps
                break

        # Vẽ bàn cờ và thông tin trạng thái
        if found:
            cv2.drawChessboardCorners(display_frame, detected_pattern, active_corners, True)
            t_mm = current_T_t2c[:3, 3]
            cv2.putText(
                display_frame,
                f"TARGET FOUND: X={t_mm[0]:.1f} Y={t_mm[1]:.1f} Z={t_mm[2]:.1f} mm",
                (30, 40), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2
            )
            status_color = (0, 255, 0)
        else:
            cv2.putText(
                display_frame,
                "SEARCHING CHESSBOARD...",
                (30, 40), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2
            )
            status_color = (0, 0, 255)

        # Hiển thị số lượng mẫu đã lưu
        cv2.putText(
            display_frame,
            f"Samples Captured: {len(captured_samples)} / 15-20 (SPACE: Capture, 'c': Calibrate, 'q': Quit)",
            (30, 80), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (255, 255, 255), 2
        )

        cv2.imshow("Hand-Eye Calibration - Logitech Brio 100", display_frame)
        key = cv2.waitKey(20) & 0xFF

        if key == ord('q'):
            print("\nĐã hủy quá trình căn chuẩn.")
            break

        elif key == 32:  # Phím SPACE: Chụp 1 mẫu
            if not found:
                print("\n[CẢNH BÁO] Không tìm thấy bàn cờ trong khung hình! Vui lòng căn chỉnh lại.")
                continue

            print(f"\n--- [MẪU SỐ {len(captured_samples) + 1}] ---")
            print("Vui lòng nhập tọa độ hiện tại của Robot (từ màn hình điều khiển hoặc GetPsnFbkWorld):")
            try:
                raw_input = input("Nhập X, Y, Z, C (cách nhau bởi dấu cách hoặc phẩy) [VD: 250.5 50.0 60.0 15.0]: ")
                tokens = raw_input.replace(",", " ").split()
                if len(tokens) < 4:
                    print("[LỖI] Cần nhập đủ 4 giá trị X, Y, Z, C! Hủy mẫu này.")
                    continue

                rx, ry, rz, rc = [float(x) for x in tokens[:4]]

                # Tạo T_gripper2base
                R_g2b = euler_scara_to_rot_matrix(0.0, 0.0, rc)
                t_g2b = np.array([rx, ry, rz], dtype=np.float64)
                T_g2b = make_transform_matrix(R_g2b, t_g2b)

                captured_samples.append({
                    "sample_id": len(captured_samples) + 1,
                    "robot_pose": [rx, ry, rz, rc],
                    "T_gripper2base": T_g2b,
                    "T_target2cam": current_T_t2c,
                    "target_pos_cam_mm": current_T_t2c[:3, 3].tolist()
                })

                print(f"[THÀNH CÔNG] Đã lưu mẫu {len(captured_samples)}: Robot=[{rx}, {ry}, {rz}, {rc}]")
                print(f"             Target in Cam: X={current_T_t2c[0,3]:.1f}, Y={current_T_t2c[1,3]:.1f}, Z={current_T_t2c[2,3]:.1f} mm")

            except Exception as e:
                print(f"[LỖI] Nhập dữ liệu thất bại: {e}")

        elif key == ord('c'):  # Phím 'c': Bắt đầu tính toán
            if len(captured_samples) < 3:
                print(f"\n[CẢNH BÁO] Cần tối thiểu 3 mẫu (hiện có {len(captured_samples)}). Khuyến nghị từ 15-20 mẫu!")
                continue

            print(f"\n[ĐANG TÍNH TOÁN] Đang giải phương trình Hand-Eye với {len(captured_samples)} mẫu...")
            cap.release()
            cv2.destroyAllWindows()
            execute_calibration_calculation(captured_samples, K, dist, img_w, img_h)
            return

    cap.release()
    cv2.destroyAllWindows()


def execute_calibration_calculation(samples, K, dist, img_w=1280, img_h=720, target_offset_gripper=None):
    """Thực hiện giải toán Hand-Eye và lưu kết quả ra JSON."""
    T_g2b_list = [s["T_gripper2base"] for s in samples]
    T_t2c_list = [s["T_target2cam"] for s in samples]

    # Giải thuật toán thống nhất
    T_cam2base, T_target2gripper, residuals = solve_scara_hand_eye_unified(
        T_g2b_list, T_t2c_list, target_offset_gripper=target_offset_gripper
    )

    T_base2cam = invert_transform(T_cam2base)

    # Tính Homography cho mặt phẳng bàn thao tác Z = 0 mm
    H_cam2robot = compute_planar_homography_robot(T_cam2base, K, dist, z_work_plane_mm=0.0)

    # In kết quả chi tiết
    print("\n" + "=" * 65)
    print(" KẾT QUẢ CĂN CHUẨN HAND-EYE (EYE-TO-HAND) THÀNH CÔNG!")
    print("=" * 65)
    print(f"Số mẫu sử dụng          : {len(samples)}")
    print(f"Sai số tịnh tiến RMS     : {residuals['rms_translation_error_mm']:.4f} mm")
    print(f"Sai số trung bình (Mean): {residuals['mean_translation_error_mm']:.4f} mm")
    print(f"Sai số cực đại (Max)    : {residuals['max_translation_error_mm']:.4f} mm")
    
    status_str = "ĐẠT CHUẨN (<= 1.0mm) - SẴN SÀNG ĐIỀU KHIỂN ROBOT!" if residuals['is_within_1mm_spec'] else "CẢNH BÁO (> 1.0mm) - NÊN THU THẬP THÊM MẪU HOẶC KIỂM TRA BẢNG CỜ"
    print(f"Đánh giá tiêu chuẩn     : {status_str}")
    print("\nMa trận T_cam2base (Camera -> Robot Base) [4x4]:")
    print(np.array2string(T_cam2base, precision=6, suppress_small=True))

    print("\nMa trận T_base2cam (Robot Base -> Camera) [4x4]:")
    print(np.array2string(T_base2cam, precision=6, suppress_small=True))

    print("\nMa trận Homography H_pixel2robot (Pixel phẳng -> Robot XY tại Z=0) [3x3]:")
    print(np.array2string(H_cam2robot, precision=6, suppress_small=True))

    # Đóng gói dữ liệu xuất file
    calib_result = {
        "timestamp": datetime.now().isoformat(),
        "camera_model": "Logitech Brio 100",
        "robot_model": "Shibaura / Toshiba THL300 SCARA",
        "configuration": "Eye-to-Hand (Camera Fixed Looking Down)",
        "image_size": {"width": img_w, "height": img_h},
        "num_samples_used": len(samples),
        "intrinsics": {
            "fx": float(K[0, 0]),
            "fy": float(K[1, 1]),
            "cx": float(K[0, 2]),
            "cy": float(K[1, 2]),
            "camera_matrix_K": K.tolist(),
            "distortion_coeffs_dist": dist.tolist()
        },
        "hand_eye_matrices": {
            "T_cam2base": T_cam2base.tolist(),
            "R_cam2base": T_cam2base[:3, :3].tolist(),
            "t_cam2base_mm": T_cam2base[:3, 3].tolist(),
            "T_base2cam": T_base2cam.tolist(),
            "R_base2cam": T_base2cam[:3, :3].tolist(),
            "t_base2cam_mm": T_base2cam[:3, 3].tolist(),
            "T_target2gripper": T_target2gripper.tolist(),
            "H_pixel2robot": H_cam2robot.tolist()
        },
        "accuracy_evaluation": {
            "rms_translation_error_mm": residuals["rms_translation_error_mm"],
            "mean_translation_error_mm": residuals["mean_translation_error_mm"],
            "max_translation_error_mm": residuals["max_translation_error_mm"],
            "target_spec_mm": 1.0,
            "is_within_1mm_spec": residuals["is_within_1mm_spec"],
            "individual_errors_mm": residuals["individual_trans_errors_mm"]
        }
    }

    # Xuất ra các vị trí thư mục cần thiết
    save_paths = [
        r"C:\Semicon\research\Lane detection_2026\sw\calib_output\hand_eye_calibration.json",
        os.path.join(os.path.dirname(__file__), "hand_eye_calibration.json"),
        r"c:\Users\qtruo\source\repos\Test_1\bin\x86\Debug\hand_eye_calibration.json"
    ]

    for p in save_paths:
        try:
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, "w", encoding="utf-8") as f:
                json.dump(calib_result, f, indent=4, ensure_ascii=False)
            print(f"[ĐÃ LƯU] File cấu hình: {p}")
        except Exception as e:
            print(f"[CẢNH BÁO] Không ghi được file {p}: {e}")

    # Báo cáo text
    report_path = r"C:\Semicon\research\Lane detection_2026\sw\calib_output\hand_eye_calibration_report.txt"
    try:
        with open(report_path, "w", encoding="utf-8") as f:
            f.write("=== BÁO CÁO CĂN CHUẨN HAND-EYE (EYE-TO-HAND) ROBOT SCARA ===\n")
            f.write(f"Thời gian: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n")
            f.write(f"Camera: Logitech Brio 100 ({img_w}x{img_h})\n")
            f.write(f"Số mẫu: {len(samples)}\n")
            f.write(f"Sai số RMS: {residuals['rms_translation_error_mm']:.4f} mm\n")
            f.write(f"Sai số Max: {residuals['max_translation_error_mm']:.4f} mm\n")
            f.write(f"Kết luận: {'ĐẠT CHUẨN <= 1mm' if residuals['is_within_1mm_spec'] else 'CHƯA ĐẠT CHUẨN'}\n\n")
            f.write("T_cam2base:\n" + str(T_cam2base) + "\n\n")
            f.write("T_base2cam:\n" + str(T_base2cam) + "\n\n")
            f.write("H_pixel2robot (Z=0):\n" + str(H_cam2robot) + "\n\n")
        print(f"[ĐÃ LƯU] Báo cáo chi tiết: {report_path}")
    except Exception:
        pass


if __name__ == "__main__":
    run_interactive_calibration()
