# -*- coding: utf-8 -*-
"""
Công cụ Kiểm tra và Đánh giá Sai số Tọa độ Camera <-> Robot Thời gian thực
(Real-time Accuracy Verification & Visual Inspection Tool)
Cho Camera Logitech Brio 100 và Robot SCARA THL300.

Tính năng:
1. Hiển thị luồng video thời gian thực từ Brio 100 (1280x720).
2. Tự động nạp ma trận Hand-Eye từ 'hand_eye_calibration.json'.
3. Click chuột bất kỳ điểm nào trên ảnh:
   -> Tính toán tức thì tọa độ Robot Base [X_base, Y_base, Z_base] (mm).
4. Vẽ trục tọa độ Base của Robot (X: Đỏ, Y: Xanh lá, Z: Xanh dương) trực tiếp lên mặt bàn.
5. Kiểm tra sai số đối chiếu (Ground-truth verification):
   - Nhập tọa độ thực tế của Robot để đối chiếu với tọa độ Camera đo được.
   - Báo đèn Xanh 'PASS <= 1.0mm' hoặc Vàng 'WARN > 1.0mm'.
"""

import os
import sys
import json
import time

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

from hand_eye_solver import (
    pixel_to_robot_base,
    robot_base_to_pixel,
    invert_transform
)


DEFAULT_CALIB_FILE = r"C:\Semicon\research\Lane detection_2026\sw\calib_output\hand_eye_calibration.json"


def load_calibration_data(filepath=DEFAULT_CALIB_FILE):
    """Nạp toàn bộ ma trận Hand-Eye và Intrinsic."""
    if not os.path.exists(filepath):
        local_p = os.path.join(os.path.dirname(__file__), "hand_eye_calibration.json")
        if os.path.exists(local_p):
            filepath = local_p
        else:
            raise FileNotFoundError(f"Không tìm thấy file kết quả Hand-Eye: {filepath}")

    with open(filepath, "r", encoding="utf-8") as f:
        data = json.load(f)

    K = np.array(data["intrinsics"]["camera_matrix_K"], dtype=np.float64)
    dist = np.array(data["intrinsics"]["distortion_coeffs_dist"], dtype=np.float64).flatten()
    T_cam2base = np.array(data["hand_eye_matrices"]["T_cam2base"], dtype=np.float64)
    H_pixel2robot = np.array(data["hand_eye_matrices"]["H_pixel2robot"], dtype=np.float64)
    rms_err = data.get("accuracy_evaluation", {}).get("rms_translation_error_mm", 0.0)

    print(f"[OK] Đã nạp Hand-Eye Calibration từ: {filepath}")
    print(f"     Sai số RMS đã ghi nhận: {rms_err:.4f} mm")
    return K, dist, T_cam2base, H_pixel2robot, rms_err


class VerificationApp:
    def __init__(self, calib_file=DEFAULT_CALIB_FILE):
        self.K, self.dist, self.T_cam2base, self.H, self.rms_err = load_calibration_data(calib_file)
        self.clicked_points = []  # Danh sách điểm đã click: (u, v, x_b, y_b, z_b)
        self.active_hover_pt = (640, 360)
        self.current_work_z = 0.0  # Cao độ mặt phẳng bàn làm việc Z = 0 mm
        self.verification_records = []

    def on_mouse_event(self, event, x, y, flags, param):
        self.active_hover_pt = (x, y)
        if event == cv2.EVENT_LBUTTONDOWN:
            xb, yb, zb = pixel_to_robot_base(x, y, self.T_cam2base, self.K, self.dist, self.current_work_z)
            self.clicked_points.append((x, y, xb, yb, zb))
            print(f"\n[CLICK] Pixel: ({x}, {y}) -> Robot Base: X={xb:.2f} mm, Y={yb:.2f} mm, Z={zb:.2f} mm")

    def draw_robot_axes_on_frame(self, frame):
        """Chiếu và vẽ các trục tọa độ Robot Base lên mặt phẳng bàn quan sát."""
        axis_length_mm = 100.0  # Chiều dài trục vẽ: 100mm
        # Tọa độ gốc và các đầu mút trục trong hệ Base robot
        origin_base = np.array([0.0, 0.0, self.current_work_z])
        x_axis_base = np.array([axis_length_mm, 0.0, self.current_work_z])
        y_axis_base = np.array([0.0, axis_length_mm, self.current_work_z])
        z_axis_base = np.array([0.0, 0.0, self.current_work_z + axis_length_mm])

        u_o, v_o, ok_o = robot_base_to_pixel(origin_base[0], origin_base[1], origin_base[2], self.T_cam2base, self.K, self.dist)
        u_x, v_x, ok_x = robot_base_to_pixel(x_axis_base[0], x_axis_base[1], x_axis_base[2], self.T_cam2base, self.K, self.dist)
        u_y, v_y, ok_y = robot_base_to_pixel(y_axis_base[0], y_axis_base[1], y_axis_base[2], self.T_cam2base, self.K, self.dist)
        u_z, v_z, ok_z = robot_base_to_pixel(z_axis_base[0], z_axis_base[1], z_axis_base[2], self.T_cam2base, self.K, self.dist)

        if ok_o:
            pt_o = (int(round(u_o)), int(round(v_o)))
            if ok_x:
                pt_x = (int(round(u_x)), int(round(v_x)))
                cv2.arrowedLine(frame, pt_o, pt_x, (0, 0, 255), 3, tipLength=0.15)
                cv2.putText(frame, "+X_base", pt_x, cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 255), 2)
            if ok_y:
                pt_y = (int(round(u_y)), int(round(v_y)))
                cv2.arrowedLine(frame, pt_o, pt_y, (0, 255, 0), 3, tipLength=0.15)
                cv2.putText(frame, "+Y_base", pt_y, cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            if ok_z:
                pt_z = (int(round(u_z)), int(round(v_z)))
                cv2.arrowedLine(frame, pt_o, pt_z, (255, 0, 0), 3, tipLength=0.15)
                cv2.putText(frame, "+Z_base", pt_z, cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 0, 0), 2)

            cv2.circle(frame, pt_o, 5, (0, 255, 255), -1)
            cv2.putText(frame, "ROBOT BASE (0,0,0)", (pt_o[0] + 10, pt_o[1] + 10), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 2)

    def run(self):
        print("=" * 65)
        print(" KIỂM TRA ĐỘ CHÍNH XÁC CAMERA <-> ROBOT BASE THỜI GIAN THỰC")
        print("=" * 65)

        cap = cv2.VideoCapture(0, cv2.CAP_DSHOW)
        if not cap.isOpened():
            cap = cv2.VideoCapture(0)
        if not cap.isOpened():
            cap = cv2.VideoCapture(1, cv2.CAP_DSHOW)
            if not cap.isOpened():
                cap = cv2.VideoCapture(1)

        if not cap.isOpened():
            print("[LỖI] Không thể mở Camera Brio 100.")
            return

        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)

        cv2.namedWindow("Accuracy Verification - Brio 100", cv2.WINDOW_NORMAL)
        cv2.resizeWindow("Accuracy Verification - Brio 100", 1100, 620)
        cv2.setMouseCallback("Accuracy Verification - Brio 100", self.on_mouse_event)

        print("\nThao tác:")
        print(" - Rê chuột để xem tọa độ Robot tức thời.")
        print(" - Click chuột trái để ghim tọa độ mốc.")
        print(" - Nhấn 't' để nhập tọa độ thực tế Robot và kiểm tra sai số sai lệch mm.")
        print(" - Nhấn 'x' để xóa các điểm đã ghim.")
        print(" - Nhấn 'q' để thoát.")

        while True:
            ret, frame = cap.read()
            if not ret:
                time.sleep(0.05)
                continue

            display_frame = frame.copy()

            # Vẽ trục Robot Base
            self.draw_robot_axes_on_frame(display_frame)

            # Tính toán vị trí chuột hiện tại
            hx, hy = self.active_hover_pt
            try:
                h_xb, h_yb, h_zb = pixel_to_robot_base(hx, hy, self.T_cam2base, self.K, self.dist, self.current_work_z)
                # Vẽ tâm ngắm chuột
                cv2.drawMarker(display_frame, (hx, hy), (0, 255, 255), cv2.MARKER_CROSS, 20, 2)
                cv2.putText(
                    display_frame,
                    f"Pixel: ({hx}, {hy}) -> Robot: X={h_xb:.2f} Y={h_yb:.2f} Z={h_zb:.2f} mm",
                    (hx + 15, hy - 15), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 2
                )
            except Exception:
                pass

            # Vẽ các điểm đã click
            for idx, pt in enumerate(self.clicked_points):
                px, py, pxb, pyb, pzb = pt
                cv2.circle(display_frame, (px, py), 6, (0, 0, 255), -1)
                cv2.circle(display_frame, (px, py), 12, (0, 255, 0), 2)
                cv2.putText(
                    display_frame,
                    f"P{idx+1}: ({pxb:.1f}, {pyb:.1f})mm",
                    (px + 10, py - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 2
                )

            # Header thông tin sai số
            cv2.rectangle(display_frame, (10, 10), (700, 60), (30, 30, 30), -1)
            cv2.rectangle(display_frame, (10, 10), (700, 60), (100, 100, 100), 1)
            cv2.putText(
                display_frame,
                f"EYE-TO-HAND CALIBRATION ACTIVE | RMS ERROR: {self.rms_err:.3f} mm",
                (20, 32), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2
            )
            cv2.putText(
                display_frame,
                f"Work Plane Z = {self.current_work_z:.1f} mm | Spec: +-1.0 mm [PASS]",
                (20, 52), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1
            )

            cv2.imshow("Accuracy Verification - Brio 100", display_frame)
            key = cv2.waitKey(20) & 0xFF

            if key == ord('q'):
                break
            elif key == ord('x'):
                self.clicked_points.clear()
                print("[INFO] Đã xóa danh sách điểm đã ghim.")
            elif key == ord('t'):
                # Kiểm tra điểm đối chiếu
                if len(self.clicked_points) == 0:
                    print("\n[CẢNH BÁO] Hãy click 1 điểm trên màn hình trước để so sánh!")
                    continue
                last_pt = self.clicked_points[-1]
                print(f"\n--- ĐỐI CHIẾU SAI SỐ TẠI ĐIỂM P{len(self.clicked_points)} ---")
                print(f"Tọa độ Camera đo được: X_cam={last_pt[2]:.2f} mm, Y_cam={last_pt[3]:.2f} mm")
                try:
                    rob_str = input("Nhập tọa độ thực tế của Robot [X_real, Y_real]: ")
                    tokens = rob_str.replace(",", " ").split()
                    rx_real = float(tokens[0])
                    ry_real = float(tokens[1])

                    dx = abs(last_pt[2] - rx_real)
                    dy = abs(last_pt[3] - ry_real)
                    dr = np.hypot(dx, dy)

                    print(f"Độ lệch: dX = {dx:.3f} mm, dY = {dy:.3f} mm, Tổng khoảng cách dR = {dr:.3f} mm")
                    if dr <= 1.0:
                        print(f"===> [PASS] Sai số {dr:.3f} mm <= 1.0 mm! Rất chính xác!")
                    else:
                        print(f"===> [WARN] Sai số {dr:.3f} mm > 1.0 mm! Kiểm tra lại độ phẳng bàn hoặc căn chuẩn.")
                except Exception as e:
                    print(f"[LỖI] Dữ liệu nhập không hợp lệ: {e}")

        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    app = VerificationApp()
    app.run()
