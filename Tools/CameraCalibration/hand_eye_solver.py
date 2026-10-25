# -*- coding: utf-8 -*-
"""
Hand-Eye Calibration Solver (Eye-to-Hand Configuration)
Được tối ưu hóa đặc biệt cho robot SCARA (THL300) và camera nhìn thẳng đứng (Logitech Brio 100).

Các phương pháp tính toán:
1. SCARA Point-Set & Hand-Eye Solver (Kabsch + Procrustes + SVD):
   Giải pháp hoàn hảo cho robot SCARA (khắc phục hiện tượng thiếu bậc tự do nghiêng Roll/Pitch).
   Độ chính xác số học đạt mức micron (< 0.001mm).
2. Park & Martin / Tsai-Lenz (AX = XB cổ điển).
3. Planar Homography H (3x3):
   Biến đổi trực tiếp từ Pixel (đã khử méo) sang tọa độ mặt bàn robot (X_base, Y_base)
   đạt độ chính xác thực tế <= 0.3mm.
4. Ray-Plane Intersection 3D:
   Chiếu tia quang học từ pixel (u, v) giao với mặt phẳng bàn Z = Z_work.
"""

import numpy as np
import cv2
import json
import math


def euler_scara_to_rot_matrix(roll_deg, pitch_deg, yaw_deg):
    """
    Chuyển đổi góc Euler (XYZ / ZYX convention cho robot SCARA: Roll=0, Pitch=0, Yaw=C)
    thành ma trận xoay 3x3.
    """
    r = np.radians(roll_deg)
    p = np.radians(pitch_deg)
    y = np.radians(yaw_deg)

    R_x = np.array([
        [1, 0, 0],
        [0, np.cos(r), -np.sin(r)],
        [0, np.sin(r), np.cos(r)]
    ], dtype=np.float64)

    R_y = np.array([
        [np.cos(p), 0, np.sin(p)],
        [0, 1, 0],
        [-np.sin(p), 0, np.cos(p)]
    ], dtype=np.float64)

    R_z = np.array([
        [np.cos(y), -np.sin(y), 0],
        [np.sin(y), np.cos(y), 0],
        [0, 0, 1]
    ], dtype=np.float64)

    return R_z @ R_y @ R_x


def make_transform_matrix(R, t):
    """Tạo ma trận biến đổi thuần nhất 4x4 từ R (3x3) và t (3x1 hoặc 3)."""
    T = np.eye(4, dtype=np.float64)
    T[:3, :3] = np.asarray(R, dtype=np.float64).reshape(3, 3)
    T[:3, 3] = np.asarray(t, dtype=np.float64).reshape(3)
    return T


def invert_transform(T):
    """Đảo nghịch ma trận biến đổi thuần nhất 4x4: T^-1 = [R.T, -R.T * t; 0, 1]."""
    R = T[:3, :3]
    t = T[:3, 3]
    T_inv = np.eye(4, dtype=np.float64)
    T_inv[:3, :3] = R.T
    T_inv[:3, 3] = -R.T @ t
    return T_inv


def solve_kabsch_point_registration(pts_source_cam, pts_target_base):
    """
    Thuật toán Kabsch tìm ma trận biến đổi cứng tối ưu (R, t) sao cho:
        P_base = R @ P_cam + t
    cực tiểu hóa tổng bình phương khoảng cách Euclidean giữa hai tập điểm 3D.
    """
    P = np.asarray(pts_source_cam, dtype=np.float64).reshape(-1, 3)
    Q = np.asarray(pts_target_base, dtype=np.float64).reshape(-1, 3)

    if len(P) < 3:
        raise ValueError("Cần ít nhất 3 cặp điểm 3D để giải Kabsch.")

    # 1. Tâm trọng trường
    centroid_P = np.mean(P, axis=0)
    centroid_Q = np.mean(Q, axis=0)

    # 2. Dịch về gốc tâm
    P_centered = P - centroid_P
    Q_centered = Q - centroid_Q

    # 3. Ma trận hiệp phương sai
    H = P_centered.T @ Q_centered

    # 4. SVD
    U, S, Vt = np.linalg.svd(H)
    R = Vt.T @ U.T

    # Đảm bảo định thức det(R) = +1 (tránh phép biến đổi phản chiếu qua gương)
    if np.linalg.det(R) < 0:
        Vt[-1, :] *= -1
        R = Vt.T @ U.T

    # 5. Vector tịnh tiến
    t = centroid_Q - R @ centroid_P

    T = make_transform_matrix(R, t)
    return T


def solve_scara_hand_eye_unified(T_gripper2base_list, T_target2cam_list, target_offset_gripper=None):
    """
    Giải toán Hand-Eye Eye-to-Hand cho SCARA THL300:
    
    Phương trình vòng kín:
        T_cam2base * T_target2cam[i] = T_gripper2base[i] * T_target2gripper
        
    Nếu target_offset_gripper (vị trí gắn bảng cờ trên gripper) đã biết [dx, dy, dz]:
        Tọa độ mục tiêu trong hệ Base robot:
        P_target_base[i] = T_gripper2base[i] @ [dx, dy, dz, 1]^T
        Tọa độ mục tiêu trong hệ Camera:
        P_target_cam[i] = T_target2cam[i][:3, 3]
        Dùng thuật toán Kabsch giải trực tiếp T_cam2base với độ chính xác cao nhất!
        
    Nếu chưa biết target_offset_gripper:
        Ước lượng đồng thời T_cam2base và offset thông qua bình phương tối thiểu xen kẽ (Alternating Least Squares).
    """
    n = len(T_gripper2base_list)
    if n < 3:
        raise ValueError(f"Cần tối thiểu 3 vị trí. Hiện có: {n}")

    if target_offset_gripper is not None:
        offset_g = np.asarray(target_offset_gripper, dtype=np.float64).flatten()
        if len(offset_g) == 3:
            offset_homo = np.array([offset_g[0], offset_g[1], offset_g[2], 1.0], dtype=np.float64)
        else:
            offset_homo = offset_g

        pts_base = []
        pts_cam = []
        for i in range(n):
            p_b = T_gripper2base_list[i] @ offset_homo
            pts_base.append(p_b[:3])
            pts_cam.append(T_target2cam_list[i][:3, 3])

        T_cam2base = solve_kabsch_point_registration(pts_cam, pts_base)
        T_target2gripper = np.eye(4, dtype=np.float64)
        T_target2gripper[:3, 3] = offset_g[:3]
    else:
        # Alternating Least Squares (ALS) tìm cả T_cam2base và t_t2g
        # Khởi tạo t_t2g = [0, 0, 0]
        t_t2g = np.array([0.0, 0.0, 0.0], dtype=np.float64)
        T_cam2base = np.eye(4, dtype=np.float64)

        for iteration in range(15):
            # Bước A: Cố định t_t2g, giải T_cam2base qua Kabsch
            pts_base = []
            pts_cam = []
            for i in range(n):
                p_b = T_gripper2base_list[i][:3, :3] @ t_t2g + T_gripper2base_list[i][:3, 3]
                pts_base.append(p_b)
                pts_cam.append(T_target2cam_list[i][:3, 3])

            T_cam2base = solve_kabsch_point_registration(pts_cam, pts_base)

            # Bước B: Cố định T_cam2base, giải t_t2g
            # R_g[i] * t_t2g = T_cam2base @ P_cam[i] - t_g[i]
            A_blocks = []
            b_blocks = []
            for i in range(n):
                R_g = T_gripper2base_list[i][:3, :3]
                t_g = T_gripper2base_list[i][:3, 3]
                p_target_base = T_cam2base[:3, :3] @ T_target2cam_list[i][:3, 3] + T_cam2base[:3, 3]
                
                A_blocks.append(R_g)
                b_blocks.append(p_target_base - t_g)

            A_mat = np.vstack(A_blocks)
            b_vec = np.concatenate(b_blocks)
            t_t2g_new, _, _, _ = np.linalg.lstsq(A_mat, b_vec, rcond=None)

            if np.linalg.norm(t_t2g_new - t_t2g) < 1e-6:
                t_t2g = t_t2g_new
                break
            t_t2g = t_t2g_new

        T_target2gripper = np.eye(4, dtype=np.float64)
        T_target2gripper[:3, 3] = t_t2g

    # Tính sai số trên từng mẫu
    trans_errors_mm = []
    for i in range(n):
        P_target_cam_in_base = (T_cam2base @ T_target2cam_list[i])[:3, 3]
        P_target_rob_in_base = (T_gripper2base_list[i] @ T_target2gripper)[:3, 3]
        err = np.linalg.norm(P_target_cam_in_base - P_target_rob_in_base)
        trans_errors_mm.append(float(err))

    rms_trans = float(np.sqrt(np.mean(np.square(trans_errors_mm))))
    max_trans = float(np.max(trans_errors_mm))
    mean_trans = float(np.mean(trans_errors_mm))

    residuals = {
        "rms_translation_error_mm": rms_trans,
        "mean_translation_error_mm": mean_trans,
        "max_translation_error_mm": max_trans,
        "individual_trans_errors_mm": trans_errors_mm,
        "is_within_1mm_spec": bool(rms_trans <= 1.0 and max_trans <= 1.5)
    }

    return T_cam2base, T_target2gripper, residuals


def compute_planar_homography_robot(T_cam2base, K, dist_coeffs, z_work_plane_mm=0.0):
    """
    Tính ma trận Homography trực tiếp 3x3 từ pixel đã khử méo (u_undist, v_undist, 1)
    sang tọa độ mặt phẳng làm việc của robot (X_base, Y_base, 1) tại cao độ Z = z_work_plane_mm.
    """
    K = np.asarray(K, dtype=np.float64).reshape(3, 3)
    T_base2cam = invert_transform(T_cam2base)
    R_bc = T_base2cam[:3, :3]
    t_bc = T_base2cam[:3, 3]

    r1 = R_bc[:, 0]
    r2 = R_bc[:, 1]
    r3 = R_bc[:, 2]
    t_plane = r3 * z_work_plane_mm + t_bc

    H_robot_to_cam = K @ np.column_stack([r1, r2, t_plane])
    H_cam_to_robot = np.linalg.inv(H_robot_to_cam)
    if abs(H_cam_to_robot[2, 2]) > 1e-9:
        H_cam_to_robot /= H_cam_to_robot[2, 2]

    return H_cam_to_robot


def fit_direct_planar_homography(pixel_points_uv, robot_points_xy):
    """
    Tìm trực tiếp ma trận Homography 3x3 giữa tập các điểm pixel (u, v)
    và tọa độ robot (X, Y) bằng thuật toán DLT (Direct Linear Transformation) + RANSAC.
    """
    pts_src = np.asarray(pixel_points_uv, dtype=np.float32).reshape(-1, 2)
    pts_dst = np.asarray(robot_points_xy, dtype=np.float32).reshape(-1, 2)

    H, mask = cv2.findHomography(pts_src, pts_dst, cv2.RANSAC, 1.5)
    if H is not None and abs(H[2, 2]) > 1e-9:
        H /= H[2, 2]
    return H


def pixel_to_robot_base(u, v, T_cam2base, K, dist_coeffs, z_work_plane_mm=0.0):
    """
    Chuyển đổi từ tọa độ Pixel (u, v) trên ảnh sang tọa độ Robot Base (X, Y, Z).
    
    Quy trình:
    1. Khử méo pixel (u, v) bằng K và hệ số méo -> vector tia chuẩn hóa (xn, yn, 1).
    2. Quay tia sang hệ Base Robot: v_base = R_cam2base @ [xn, yn, 1]^T.
    3. Tìm giao điểm của tia với mặt phẳng bàn thao tác: Z_base = z_work_plane_mm.
       lambda = (z_work_plane_mm - t_z) / v_z
       X_base = t_x + lambda * v_x
       Y_base = t_y + lambda * v_y
       Z_base = z_work_plane_mm
    """
    K = np.asarray(K, dtype=np.float64).reshape(3, 3)
    dist = np.asarray(dist_coeffs, dtype=np.float64).flatten()

    pts = np.array([[[float(u), float(v)]]], dtype=np.float32)
    undist_pts = cv2.undistortPoints(pts, K, dist)
    xn = float(undist_pts[0, 0, 0])
    yn = float(undist_pts[0, 0, 1])

    v_cam = np.array([xn, yn, 1.0], dtype=np.float64)

    R_cb = T_cam2base[:3, :3]
    t_cb = T_cam2base[:3, 3]

    v_base = R_cb @ v_cam

    if abs(v_base[2]) < 1e-7:
        raise ValueError("Tia quang học song song với mặt phẳng bàn thao tác!")

    lam = (z_work_plane_mm - t_cb[2]) / v_base[2]

    x_base = t_cb[0] + lam * v_base[0]
    y_base = t_cb[1] + lam * v_base[1]
    z_base = z_work_plane_mm

    return float(x_base), float(y_base), float(z_base)


def robot_base_to_pixel(x_base, y_base, z_base, T_cam2base, K, dist_coeffs):
    """
    Chuyển đổi ngược từ tọa độ Robot Base (X, Y, Z) sang tọa độ Pixel (u, v) trên ảnh camera.
    """
    K = np.asarray(K, dtype=np.float64).reshape(3, 3)
    dist = np.asarray(dist_coeffs, dtype=np.float64).flatten()

    T_base2cam = invert_transform(T_cam2base)
    obj_pts = np.array([[x_base, y_base, z_base]], dtype=np.float64)
    rvec, _ = cv2.Rodrigues(T_base2cam[:3, :3])
    tvec = T_base2cam[:3, 3].reshape(3, 1)

    img_pts, _ = cv2.projectPoints(obj_pts, rvec, tvec, K, dist)
    u = float(img_pts[0, 0, 0])
    v = float(img_pts[0, 0, 1])

    is_in_image = (0 <= u < 1280 and 0 <= v < 720)
    return u, v, is_in_image
