# -*- coding: utf-8 -*-
"""
Chương trình Hiệu chuẩn Camera (Camera Calibration) bằng Bàn cờ A3 (16x11 ô vuông -> 15x10 giao điểm trong).
Hỗ trợ:
  1. Hiệu chuẩn trực tiếp từ Live Camera / Webcam (bấm SPACE để chụp, c để calib).
  2. Hiệu chuẩn từ thư mục ảnh chụp sẵn (--images_glob "data/*.jpg").
  3. Hiệu chuẩn từ file video quay bàn cờ (--video "video.mp4").
  4. set-world: xác lập hệ bàn từ webcam hoặc --reference_image rồi xác nhận.
  5. measure: nạp --world_file, click nhiều điểm trên mặt bàn Zb = 0.
  Các lệnh hiệu chuẩn cũ và --world (xác lập/đo gộp) vẫn được hỗ trợ.

Kết quả xuất ra:
  - camera_calibration.npz   : File nhị phân NumPy (dùng trực tiếp cho code Lane Detection).
  - camera_calibration.json  : File JSON chứa K, dist, RMS, kích thước ảnh.
  - calibration_report.txt   : Báo cáo chi tiết sai số tái chiếu.
  - Thư mục undistorted_samples/ : Ảnh mẫu đã khử méo thấu kính.
  - world_reference.npz/json, world_calibration_report.txt và ảnh tham chiếu:
    xuất riêng sau khi người dùng duyệt gốc/trục; không thay thế file nội tại.

Quy ước: P_camera = R @ P_board + t (mm). Hệ bàn chưa phải hệ robot.
measure dùng homography trên Zb=0; --world cũ giữ giao tia với Zw=--plane_z_mm.
"""

import argparse
import glob
import json
import os
import sys
import time
from datetime import datetime, timezone

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


def make_object_points(pattern_size, square_size_mm):
    """Điểm (j*s, i*s, 0), thứ tự hàng trước, khớp corners của OpenCV."""
    cols, rows = map(int, pattern_size)
    if cols < 2 or rows < 2:
        raise ValueError("Pattern phải có ít nhất 2x2 góc trong.")
    if not np.isfinite(square_size_mm) or square_size_mm <= 0:
        raise ValueError("--square_size_mm phải hữu hạn, > 0 và là cạnh ô đã đo thực tế.")
    points = np.zeros((rows * cols, 3), np.float32)
    points[:, :2] = np.mgrid[0:cols, 0:rows].T.reshape(-1, 2) * square_size_mm
    return points


def validate_camera_model(K, dist):
    K = np.asarray(K, dtype=np.float64).reshape(3, 3)
    dist = np.asarray(dist, dtype=np.float64).reshape(-1, 1)
    if (not np.isfinite(K).all() or not np.isfinite(dist).all()
            or K[0, 0] <= 0 or K[1, 1] <= 0
            or not np.allclose(K[2], [0, 0, 1])
            or not np.isclose(K[0, 1], 0) or not np.isclose(K[1, 0], 0)):
        raise ValueError("K/dist không hợp lệ cho mô hình camera OpenCV.")
    if dist.size not in (4, 5, 8, 12, 14):
        raise ValueError("dist cần 4, 5, 8, 12 hoặc 14 hệ số (mô hình calibrateCamera).")
    return K, dist


def validate_pose(R, tvec):
    R = np.asarray(R, dtype=np.float64).reshape(3, 3)
    t = np.asarray(tvec, dtype=np.float64).reshape(3)
    if (not np.isfinite(R).all() or not np.isfinite(t).all()
            or not np.allclose(R.T @ R, np.eye(3), atol=1e-7)
            or not np.isclose(np.linalg.det(R), 1.0, atol=1e-7)):
        raise ValueError("R/tvec không phải phép biến đổi cứng hữu hạn hợp lệ.")
    return R, t


def build_pose_matrices(K, rvec, tvec):
    """Quy ước P_camera = R @ P_world + t; các độ dài tính bằng mm."""
    K = np.asarray(K, dtype=np.float64).reshape(3, 3)
    rvec = np.asarray(rvec, dtype=np.float64).reshape(3, 1)
    R, _ = cv2.Rodrigues(rvec)
    R, t = validate_pose(R, tvec)
    if not np.isfinite(K).all() or not np.isfinite(rvec).all():
        raise ValueError("K/rvec phải hữu hạn.")
    camera_position = -R.T @ t
    T_cw = np.eye(4)
    T_cw[:3, :3], T_cw[:3, 3] = R, t
    T_wc = np.eye(4)
    T_wc[:3, :3], T_wc[:3, 3] = R.T, camera_position
    return {
        "rvec": rvec, "R": R, "tvec": t.reshape(3, 1),
        "T_cw": T_cw, "T_wc": T_wc,
        "T_camera_from_board": T_cw, "T_board_from_camera": T_wc,
        "camera_position_world_mm": camera_position, "C_board": camera_position,
        "P_projection": K @ T_cw[:3],
    }


def invert_homography(matrix):
    """Nghịch đảo homography hữu hạn, loại ma trận suy biến/không ổn định."""
    matrix = np.asarray(matrix, dtype=np.float64).reshape(3, 3)
    if not np.isfinite(matrix).all():
        raise ValueError("Homography chứa giá trị không hữu hạn.")
    try:
        condition = np.linalg.cond(matrix)
        if not np.isfinite(condition) or condition > 1e12:
            raise ValueError("Homography không khả nghịch hoặc quá gần suy biến; hãy chọn lại ảnh tham chiếu.")
        inverse = np.linalg.inv(matrix)
    except np.linalg.LinAlgError as exc:
        raise ValueError("Homography không khả nghịch.") from exc
    if not np.isfinite(inverse).all():
        raise ValueError("Nghịch đảo homography không hữu hạn.")
    return inverse


def build_board_homographies(K, R, tvec):
    """Zb=0: H dùng PIXEL LÝ TƯỞNG, khử méo với P=K (không phải ảnh gốc)."""
    K = np.asarray(K, dtype=np.float64).reshape(3, 3)
    R, t = validate_pose(R, tvec)
    H = K @ np.column_stack((R[:, 0], R[:, 1], t))
    return {"H_board_to_image": H, "H_image_to_board": invert_homography(H)}


def pixel_to_board(pixel, K, dist, H_image_to_board, R=None, tvec=None):
    """Pixel ẢNH GỐC -> pixel lý tưởng cùng K -> (Xb,Yb,0), đơn vị mm.

    Khử méo đúng một lần bằng undistortPoints(P=K), rồi chia tọa độ đồng nhất.
    R/tvec nếu cung cấp được dùng kiểm tra điểm trước camera, không khử méo thêm.
    """
    K, dist = validate_camera_model(K, dist)
    uv = np.asarray(pixel, dtype=np.float64).reshape(2)
    H = np.asarray(H_image_to_board, dtype=np.float64).reshape(3, 3)
    invert_homography(H)  # Kiểm tra kể cả khi được gọi độc lập, không qua loader.
    if not np.isfinite(uv).all():
        raise ValueError("Pixel cần hữu hạn.")
    ideal = cv2.undistortPoints(uv.reshape(1, 1, 2), K, dist, P=K).reshape(2)
    homogeneous = H @ np.array([ideal[0], ideal[1], 1.0])
    if not np.isfinite(homogeneous).all():
        raise ValueError("Không đổi được tọa độ: kết quả đồng nhất không hữu hạn.")
    if abs(homogeneous[2]) <= 1e-12 * np.linalg.norm(homogeneous):
        raise ValueError("Không chia được tọa độ đồng nhất: tia gần song song mặt bàn.")
    xy = homogeneous[:2] / homogeneous[2]
    point = np.array([xy[0], xy[1], 0.0])
    if not np.isfinite(point).all():
        raise ValueError("Tọa độ bàn không hữu hạn sau phép chia đồng nhất.")
    result = {"pixel": uv, "ideal_pixel": ideal, "point_board_mm": point}
    if (R is None) != (tvec is None):
        raise ValueError("Cần cung cấp đồng thời R và tvec.")
    if R is not None:
        R, t = validate_pose(R, tvec)
        camera_point = R @ point + t
        if not np.isfinite(camera_point).all() or camera_point[2] <= 0:
            raise ValueError("Điểm giao nằm phía sau hoặc tại tâm camera.")
        result["point_camera_mm"] = camera_point
    return result


def pixel_to_plane(pixel, K, dist, R, tvec, plane_z_mm=0.0,
                   parallel_epsilon=1e-9):
    """Giao tia từ PIXEL ẢNH GỐC với Zw có dấu; không suy ra 3D tự do.

    Trả pixel, point_camera_mm, point_world_mm và ray_parameter_mm.
    ValueError nghĩa là không có giao điểm hợp lệ; không trả điểm giả.
    """
    K, dist = validate_camera_model(K, dist)
    R, t = validate_pose(R, tvec)
    uv = np.asarray(pixel, dtype=np.float64).reshape(2)
    if not np.isfinite(uv).all() or not np.isfinite(plane_z_mm):
        raise ValueError("Pixel và --plane_z_mm phải hữu hạn.")
    if not np.isfinite(parallel_epsilon) or parallel_epsilon <= 0:
        raise ValueError("Ngưỡng song song phải hữu hạn và > 0.")
    # Không truyền R/P: đầu ra đã chuẩn hóa, tuyệt đối không nhân K^-1 lần nữa.
    normalized = cv2.undistortPoints(uv.reshape(1, 1, 2), K, dist).reshape(2)
    q_camera = np.array([normalized[0], normalized[1], 1.0])
    C_world = -R.T @ t
    direction_world = R.T @ q_camera
    if not np.isfinite(direction_world).all():
        raise ValueError("Không khử méo được pixel: tia nhìn không hữu hạn.")
    if abs(direction_world[2]) <= parallel_epsilon * np.linalg.norm(direction_world):
        raise ValueError("Tia nhìn gần song song mặt phẳng đo; giao điểm không xác định/không ổn định.")
    ray_parameter = (float(plane_z_mm) - C_world[2]) / direction_world[2]
    if not np.isfinite(ray_parameter) or ray_parameter <= 0:
        raise ValueError("Giao điểm nằm phía sau hoặc ngay tại tâm camera.")
    point_world = C_world + ray_parameter * direction_world
    point_camera = R @ point_world + t
    if (not np.isfinite(point_world).all() or not np.isfinite(point_camera).all()
            or point_camera[2] <= 0):
        raise ValueError("Giao điểm không hữu hạn hoặc nằm phía sau camera.")
    return {"pixel": uv, "point_camera_mm": point_camera,
            "point_world_mm": point_world, "ray_parameter_mm": float(ray_parameter)}


def reorder_reference_corners(corners, pattern_size, origin_index=0, swap_axes=False):
    """Chọn 1 trong 4 góc NGOÀI CÙNG CỦA LƯỚI GÓC TRONG làm gốc.

    Không coi corner[0] luôn là cùng một góc vật lý khi bàn cờ xoay.
    Đảo hàng/cột rồi có thể chuyển vị cả lưới và pattern, bảo toàn tương ứng 2D/3D.
    """
    cols, rows = map(int, pattern_size)
    grid = np.asarray(corners, dtype=np.float32).reshape(rows, cols, 2)
    if origin_index not in (0, 1, 2, 3):
        raise ValueError("origin_index phải thuộc 0..3.")
    if origin_index in (2, 3):
        grid = grid[::-1, :, :]
    if origin_index in (1, 2):
        grid = grid[:, ::-1, :]
    if swap_axes:
        grid = grid.transpose(1, 0, 2)
    rows, cols = grid.shape[:2]
    return np.ascontiguousarray(grid.reshape(-1, 1, 2)), (cols, rows)


def estimate_world_pose(corners, pattern_size, square_size_mm, K, dist):
    """Hàm độc lập webcam: solvePnP trên đầy đủ các góc theo thứ tự đã duyệt."""
    K, dist = validate_camera_model(K, dist)
    object_points = make_object_points(pattern_size, square_size_mm)
    corners = np.asarray(corners, dtype=np.float64).reshape(-1, 1, 2)
    if len(corners) != len(object_points) or not np.isfinite(corners).all():
        raise ValueError("Cần đầy đủ các góc hữu hạn, đúng kích thước pattern.")
    ok, rvec, tvec = cv2.solvePnP(object_points, corners, K, dist,
                                 flags=cv2.SOLVEPNP_ITERATIVE)
    if not ok:
        raise ValueError("solvePnP không tìm được ngoại tại tham chiếu.")
    result = build_pose_matrices(K, rvec, tvec)
    result.update(build_board_homographies(K, result["R"], result["tvec"]))
    camera_points = (result["R"] @ object_points.T).T + result["tvec"].reshape(3)
    if np.any(camera_points[:, 2] <= 0):
        raise ValueError("Nghiệm solvePnP đặt góc bàn cờ phía sau camera.")
    projected, _ = cv2.projectPoints(object_points, rvec, tvec, K, dist)
    errors = np.linalg.norm(projected.reshape(-1, 2) - corners.reshape(-1, 2), axis=1)
    result.update({"reprojection_mean_error_px": float(errors.mean()),
                   "reprojection_rms_error_px": float(np.sqrt(np.mean(errors ** 2))),
                   "reprojection_max_error_px": float(errors.max()),
                   "reference_corners_px": corners.reshape(-1, 2),
                   "board_points_mm": object_points})
    if not np.isfinite(list(result[k] for k in (
            "reprojection_mean_error_px", "reprojection_rms_error_px",
            "reprojection_max_error_px"))).all():
        raise ValueError("Sai số tái chiếu không hữu hạn.")
    return result


def detect_corners(gray, pattern_size):
    """
    Tìm góc giao điểm bàn cờ với độ chính xác cao:
    Ưu tiên thuật toán Sector-Based (cv2.findChessboardCornersSB) tự động subpixel,
    nếu không được sẽ fallback sang cv2.findChessboardCorners + cornerSubPix.
    Tự động thử cả 2 chiều (cols, rows) và (rows, cols) khi người dùng xoay bàn cờ.
    """
    cols, rows = pattern_size
    candidates = [(cols, rows), (rows, cols)]

    for c, r in candidates:
        # 1. Thuật toán hiện đại Sector-Based (nhanh, tự làm mịn subpixel, rất chuẩn cho bàn cờ lớn A3)
        try:
            ret, corners = cv2.findChessboardCornersSB(
                gray, (c, r), flags=cv2.CALIB_CB_NORMALIZE_IMAGE
            )
            if ret and corners is not None:
                return True, (c, r), corners
        except Exception:
            pass

        # 2. Thuật toán cổ điển + cornerSubPix dự phòng
        try:
            ret, corners = cv2.findChessboardCorners(
                gray, (c, r),
                cv2.CALIB_CB_ADAPTIVE_THRESH + cv2.CALIB_CB_FAST_CHECK + cv2.CALIB_CB_NORMALIZE_IMAGE
            )
            if ret and corners is not None:
                criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 0.001)
                corners = cv2.cornerSubPix(gray, corners, (11, 11), (-1, -1), criteria)
                return True, (c, r), corners
        except Exception:
            pass

    return False, (0, 0), None


def read_image_unicode(path):
    """
    Đọc ảnh an toàn trên Windows hỗ trợ cả đường dẫn chứa tiếng Việt / Unicode.
    """
    try:
        data = np.fromfile(path, dtype=np.uint8)
        img = cv2.imdecode(data, cv2.IMREAD_COLOR)
        return img
    except Exception:
        return cv2.imread(path)


def save_image_unicode(path, img):
    """
    Lưu ảnh an toàn trên Windows hỗ trợ đường dẫn Unicode.
    """
    try:
        ext = os.path.splitext(path)[1]
        ok, buf = cv2.imencode(ext, img)
        if ok:
            buf.tofile(path)
            return True
    except Exception:
        pass
    return cv2.imwrite(path, img)


def collect_images_from_video(video_path, out_dir, max_frames=50, skip=10):
    """Trích xuất frame từ video để làm mẫu ảnh căn chuẩn."""
    if max_frames < 1 or skip < 1:
        raise ValueError("--video_max_frames và --video_skip phải >= 1.")
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        raise RuntimeError(f"Không thể mở video: {video_path}")
    os.makedirs(out_dir, exist_ok=True)
    saved = []
    idx = 0
    frame_id = 0
    print(f"[INFO] Đang trích xuất ảnh từ video '{video_path}'...")
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        if frame_id % skip == 0:
            out_file = os.path.join(out_dir, f"frame_{idx:04d}.jpg")
            if not save_image_unicode(out_file, frame):
                cap.release()
                raise OSError(f"Không lưu được frame: {out_file}")
            saved.append(out_file)
            idx += 1
            if idx >= max_frames:
                break
        frame_id += 1
    cap.release()
    print(f"[INFO] Đã trích xuất {len(saved)} khung hình vào thư mục '{out_dir}'.")
    return saved


def run_batch_calibration(img_paths, nx=15, ny=10, square_size_mm=25.0, save_dir="calib_output", show=False):
    """
    Hiệu chuẩn từ danh sách các file ảnh có sẵn.
    """
    os.makedirs(save_dir, exist_ok=True)
    print(f"\n[BẮT ĐẦU HIỆU CHUẨN BATCH]")
    print(f" - Bàn cờ A3: {nx}x{ny} giao điểm góc trong (16x11 ô vuông)")
    print(f" - Kích thước cạnh 1 ô: {square_size_mm} mm")
    print(f" - Tổng số ảnh đầu vào: {len(img_paths)}")

    objpoints = []
    imgpoints = []
    img_size = None
    used_images = []
    patterns_per_view = []

    for idx, p in enumerate(img_paths):
        img = read_image_unicode(p)
        if img is None:
            print(f" [BỎ QUA] Không đọc được ảnh: {p}")
            continue

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        if img_size is None:
            img_size = gray.shape[::-1]  # (width, height)
        elif gray.shape[::-1] != img_size:
            print(f" [BỎ QUA] Ảnh {p} có kích thước {gray.shape[::-1]}, "
                  f"khác kích thước hiệu chuẩn {img_size} (WxH).")
            continue

        found, pattern_found, corners = detect_corners(gray, (nx, ny))
        if found:
            c_found, r_found = pattern_found
            # Tạo lưới 3D tương ứng với chiều phát hiện (c_found, r_found)
            objp = make_object_points(pattern_found, square_size_mm)

            objpoints.append(objp)
            imgpoints.append(corners)
            used_images.append(p)
            patterns_per_view.append(pattern_found)
            print(f" [{idx+1}/{len(img_paths)}] Bắt thành công bàn cờ ({c_found}x{r_found}): {os.path.basename(p)}")

            if show:
                vis = cv2.drawChessboardCorners(img.copy(), (c_found, r_found), corners, True)
                cv2.imshow("Chessboard Detection Preview", cv2.resize(vis, (960, 540)))
                cv2.waitKey(200)
        else:
            print(f" [{idx+1}/{len(img_paths)}] KHÔNG tìm thấy bàn cờ: {os.path.basename(p)}")
            if show:
                cv2.imshow("Chessboard Detection Preview", cv2.resize(img, (960, 540)))
                cv2.waitKey(100)

    if show:
        cv2.destroyAllWindows()

    if len(objpoints) < 5:
        print(f"\n[LỖI] Chỉ có {len(objpoints)} ảnh hợp lệ. Cần ít nhất 5 ảnh (khuyến nghị 12-25 ảnh) để căn chuẩn chính xác!")
        return None

    return compute_and_save_calibration(objpoints, imgpoints, img_size, square_size_mm,
                                        used_images, save_dir, patterns_per_view)


def run_live_camera_calibration(cam_idx=0, nx=15, ny=10, square_size_mm=25.0, save_dir="calib_output"):
    """
    Hiệu chuẩn trực tiếp từ Live Camera / Webcam.
    Bấm SPACE: Chụp lưu mẫu bàn cờ.
    Bấm C: Thực hiện tính toán hiệu chuẩn.
    Bấm U: Bật/tắt xem ảnh đã khử méo.
    Bấm S: Lưu thông số ra file.
    Bấm Q: Thoát.
    """
    os.makedirs(save_dir, exist_ok=True)
    snap_dir = os.path.join(save_dir, "captured_samples")
    os.makedirs(snap_dir, exist_ok=True)

    print(f"\n[INFO] Đang kết nối tới Camera Index {cam_idx}...")
    cap = cv2.VideoCapture(cam_idx, cv2.CAP_DSHOW)
    if not cap.isOpened():
        cap = cv2.VideoCapture(cam_idx)

    if not cap.isOpened():
        print(f"[LỖI] Không thể mở Camera Index {cam_idx}! Vui lòng kiểm tra lại cáp kết nối.")
        return None

    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)

    for _ in range(5):
        cap.read()

    win_name = "Camera Calibration (Chessboard 15x10 A3)"
    cv2.namedWindow(win_name, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(win_name, 1100, 680)

    print("\n" + "=" * 70)
    print(" HƯỚNG DẪN HIỆU CHUẨN CAMERA TRỰC TIẾP:")
    print(" 1. Cầm tấm bàn cờ A3 đưa vào khung hình ở các vị trí khác nhau:")
    print("    - Ở giữa khung hình, sát góc trên-trái, trên-phải, dưới-trái, dưới-phải.")
    print("    - Để gần camera (bàn cờ to) và để lùi xa ra (bàn cờ nhỏ).")
    print("    - Nghiêng bàn cờ khoảng 15 - 30 độ để camera ước lượng chiều sâu 3D.")
    print(" 2. Khi thấy lưới màu xuất hiện bao trọn 15x10 góc, nhấn phím SPACE để chụp.")
    print(" 3. Sau khi chụp được >= 8 đến 15 mẫu, nhấn phím 'c' để BẮT ĐẦU HIỆU CHUẨN.")
    print(" 4. Nhấn 'u' để kiểm tra kết quả khử méo (xem các cạnh có thẳng tắp không).")
    print(" 5. Nhấn 's' để lưu lại ma trận K và hệ số méo D.")
    print(" 6. Nhấn 'q' hoặc 'ESC' để thoát.")
    print("=" * 70 + "\n")

    objpoints = []
    imgpoints = []
    saved_paths = []
    patterns_per_view = []
    img_size = None

    is_calibrated = False
    camera_matrix = None
    dist_coeffs = None
    rms_error = 0.0
    map1, map2 = None, None
    show_undistorted = False

    while True:
        ret, frame = cap.read()
        if not ret or frame is None:
            print("[LỖI] Không đọc được frame camera; dừng để tránh dùng ảnh cũ.")
            break

        h, w = frame.shape[:2]
        if img_size is None:
            img_size = (w, h)
        elif (w, h) != img_size:
            print(f"[LỖI] Camera đổi kích thước từ {img_size} thành {(w, h)}. Hãy chạy lại.")
            break

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        found, (c_found, r_found), corners = detect_corners(gray, (nx, ny))

        display = frame.copy()

        if show_undistorted and is_calibrated and map1 is not None and map2 is not None:
            display = cv2.remap(frame, map1, map2, cv2.INTER_LINEAR)
            # Vẽ các đường gióng ngang dọc để người dùng kiểm tra độ thẳng
            for y_line in range(40, h, 60):
                cv2.line(display, (0, y_line), (w, y_line), (0, 70, 0), 1)
            for x_line in range(40, w, 60):
                cv2.line(display, (x_line, 0), (x_line, h), (0, 70, 0), 1)
            cv2.putText(display, f"CHE DO KHU MEO (UNDISTORTED) - RMS: {rms_error:.4f} px",
                        (20, 40), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (0, 255, 0), 2)
            cv2.putText(display, "[Nhan 'u' de quay lai xem camera truc tiep]",
                        (20, 75), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1)
        else:
            if found:
                cv2.drawChessboardCorners(display, (c_found, r_found), corners, found)
                cv2.putText(display, f"DA BAT DUOC BAN CO: {c_found}x{r_found} ({c_found*r_found} DIEM GOC)",
                            (20, 40), cv2.FONT_HERSHEY_SIMPLEX, 0.75, (0, 255, 0), 2)
                cv2.putText(display, "Nhan [SPACE] de chup luu mau nay!",
                            (20, 75), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)
            else:
                cv2.putText(display, f"DANG TIM BAN CO {nx}x{ny} (A3, 16x11 o vuong)...",
                            (20, 40), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 140, 255), 2)

            # Khung hiển thị số lượng mẫu ở góc trên bên phải
            box_color = (0, 255, 0) if len(objpoints) >= 10 else ((0, 255, 255) if len(objpoints) >= 5 else (0, 165, 255))
            cv2.rectangle(display, (w - 280, 15), (w - 15, 115), (30, 30, 30), -1)
            cv2.rectangle(display, (w - 280, 15), (w - 15, 115), box_color, 2)
            cv2.putText(display, f"Mau da chup: {len(objpoints)} / 15", (w - 265, 50),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.65, box_color, 2)
            if len(objpoints) >= 5:
                cv2.putText(display, "Nhan 'c' de CALIB", (w - 265, 85),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            else:
                cv2.putText(display, f"Can them {5 - len(objpoints)} mau...", (w - 265, 85),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (180, 180, 180), 1)

        # Thanh hướng dẫn phím tắt dưới đáy
        info_text = f"SPACE: Chup mau | c: Calib | u: Xem khu meo | s: Luu | q: Thoat"
        cv2.putText(display, info_text, (20, h - 15),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.52, (255, 255, 255), 1, cv2.LINE_AA)

        cv2.imshow(win_name, display)
        key = cv2.waitKey(1) & 0xFF

        if key == ord('q') or key == 27:
            break

        elif key == 32:  # SPACE
            if found:
                objp = make_object_points((c_found, r_found), square_size_mm)
                img_path = os.path.join(snap_dir, f"sample_{len(objpoints) + 1:02d}.jpg")
                if not save_image_unicode(img_path, frame):
                    print(f"[LỖI] Không lưu được mẫu: {img_path}")
                    continue
                objpoints.append(objp)
                imgpoints.append(corners.copy())
                saved_paths.append(img_path)
                patterns_per_view.append((c_found, r_found))

                print(f"[ĐÃ CHỤP MẪU {len(objpoints)}] Lưu tại {img_path}. "
                      f"Hãy đổi góc nghiêng / vị trí và chụp tiếp!")
            else:
                print("[CẢNH BÁO] Chưa bắt được bàn cờ trong khung hình! Vui lòng căn chỉnh lại.")

        elif key == ord('c'):  # 'c': Tính toán căn chuẩn
            if len(objpoints) < 5:
                print(f"[CẢNH BÁO] Cần ít nhất 5 mẫu ảnh bàn cờ (Hiện có: {len(objpoints)}). "
                      f"Hãy nhấn SPACE để chụp thêm!")
            else:
                print(f"\n[ĐANG TÍNH TOÁN HIỆU CHUẨN] Với {len(objpoints)} mẫu ảnh...")
                calib_res = compute_and_save_calibration(
                    objpoints, imgpoints, img_size, square_size_mm, saved_paths, save_dir,
                    patterns_per_view
                )
                if calib_res is not None:
                    camera_matrix = calib_res["K"]
                    dist_coeffs = calib_res["dist"]
                    rms_error = calib_res["rms"]
                    is_calibrated = True

                    # Tạo bản đồ khử méo
                    new_K, _ = cv2.getOptimalNewCameraMatrix(camera_matrix, dist_coeffs, img_size, 0, img_size)
                    map1, map2 = cv2.initUndistortRectifyMap(camera_matrix, dist_coeffs, None, new_K, img_size, cv2.CV_32FC1)
                    show_undistorted = True
                    print("[INFO] Đã tự động chuyển sang chế độ xem ảnh khử méo (Undistorted). Nhấn 'u' để đổi lại.")

        elif key == ord('u'):
            if is_calibrated:
                show_undistorted = not show_undistorted
                print(f"[CHẾ ĐỘ] Hiển thị khử méo: {show_undistorted}")
            else:
                print("[CẢNH BÁO] Camera chưa được hiệu chuẩn! Hãy chụp đủ mẫu và nhấn 'c' trước.")

        elif key == ord('s'):
            if is_calibrated:
                print(f"[ĐÃ LƯU] Toàn bộ thông số hiệu chuẩn đã được lưu tại thư mục: {os.path.abspath(save_dir)}")
            else:
                print("[CẢNH BÁO] Chưa có kết quả căn chuẩn để lưu!")

    cap.release()
    cv2.destroyAllWindows()


def compute_and_save_calibration(objpoints, imgpoints, img_size, square_size_mm, img_paths,
                                 save_dir, pattern_sizes_per_view=None):
    """
    Chạy cv2.calibrateCamera, tính sai số tái chiếu và xuất ra file npz, json, report, ảnh preview.
    """
    os.makedirs(save_dir, exist_ok=True)
    if not objpoints or len(objpoints) != len(imgpoints) or len(img_paths) != len(objpoints):
        raise ValueError("Mỗi mẫu cần có điểm 3D, điểm ảnh và đường dẫn ảnh tương ứng.")
    if pattern_sizes_per_view is None:
        # Tương thích lời gọi hàm cũ: suy ra từng lưới từ chính tọa độ object_points.
        pattern_sizes_per_view = [
            (len(np.unique(np.asarray(p)[:, 0])), len(np.unique(np.asarray(p)[:, 1])))
            for p in objpoints
        ]
    if len(pattern_sizes_per_view) != len(objpoints):
        raise ValueError("Số pattern_sizes_per_view phải bằng số mẫu.")
    for points, pixels, pattern in zip(objpoints, imgpoints, pattern_sizes_per_view):
        expected = make_object_points(pattern, square_size_mm)
        if (np.asarray(points).shape != expected.shape
                or not np.allclose(points, expected)
                or np.asarray(pixels).size != len(expected) * 2
                or not np.isfinite(pixels).all()):
            raise ValueError("Metadata pattern/kích thước ô không khớp thứ tự điểm 3D/ảnh.")
    flags = 0  # Mô hình biến dạng tiêu chuẩn (k1, k2, p1, p2, k3)
    t0 = time.time()
    rms, K, dist, rvecs, tvecs = cv2.calibrateCamera(
        objpoints, imgpoints, img_size, None, None, flags=flags
    )
    duration = time.time() - t0

    fx, fy = K[0, 0], K[1, 1]
    cx, cy = K[0, 2], K[1, 2]

    # Tính Mean Reprojection Error (sai số chiếu lại trung bình tính theo pixel)
    total_error = 0.0
    errors_per_view = []
    for i in range(len(objpoints)):
        reprojected_pts, _ = cv2.projectPoints(objpoints[i], rvecs[i], tvecs[i], K, dist)
        # Chuan hoa toa do ve cung dang (N, 2), dung float64
        pts1 = np.asarray(imgpoints[i], dtype=np.float64).reshape(-1, 2)
        pts2 = np.asarray(reprojected_pts, dtype=np.float64).reshape(-1, 2)
        if pts1.shape != pts2.shape:
            raise ValueError(
                f"Mau {i + 1}: so diem khong khop: "
                f"imgpoints={pts1.shape}, reprojected={pts2.shape}"
            )
        # Tinh sai so bang NumPy, khong dung cv2.norm()
        distances = np.sqrt(np.sum((pts1 - pts2) ** 2, axis=1))
        err = float(np.mean(distances))
        errors_per_view.append(err)
        total_error += err
    mean_reproj_err = total_error / len(objpoints)

    print("\n" + "=" * 60)
    print(" KẾT QUẢ HIỆU CHUẨN CAMERA (CALIBRATION RESULT)")
    print("=" * 60)
    print(f"Kích thước ảnh (WxH)    : {img_size[0]} x {img_size[1]} pixels")
    print(f"Số mẫu ảnh sử dụng      : {len(objpoints)}")
    print(f"Thời gian tính toán     : {duration:.2f} giây")
    print(f"Sai số RMS (OpenCV)     : {rms:.4f} pixels")
    print(f"Sai số Mean Reprojection: {mean_reproj_err:.4f} pixels")
    print(f"Tiêu cự (Focal Length)  : fx = {fx:.2f} px, fy = {fy:.2f} px")
    print(f"Tâm quang học (Principal): cx = {cx:.2f} px, cy = {cy:.2f} px")
    print(f"Hệ số méo (Distortion)  : {dist.ravel()}")
    print("=" * 60)

    # 1. Lưu file NumPy .npz (cực kỳ thuận tiện để load trong các thuật toán xử lý ảnh tiếp theo)
    npz_path = os.path.join(save_dir, "camera_calibration.npz")
    np.savez(
        npz_path,
        K=K,
        dist=dist,
        rms=rms,
        mean_error=mean_reproj_err,
        image_size=np.array(img_size),
        rvecs=np.asarray(rvecs),
        tvecs=np.asarray(tvecs),
        image_paths=np.asarray([os.path.abspath(p) for p in img_paths], dtype=str),
        pattern_sizes_per_view=np.asarray(pattern_sizes_per_view, dtype=np.int32),
        pattern_size=np.asarray(pattern_sizes_per_view[0], dtype=np.int32),
        errors_per_view=np.asarray(errors_per_view),
        square_size_mm=float(square_size_mm),
        units="mm"
    )

    # 2. Lưu file cấu hình JSON
    json_path = os.path.join(save_dir, "camera_calibration.json")
    json_data = {
        "image_size": {"width": img_size[0], "height": img_size[1]},
        "num_samples_used": len(objpoints),
        "square_size_mm": square_size_mm,
        "rms_error": float(rms),
        "mean_reprojection_error_px": float(mean_reproj_err),
        "fx": float(fx),
        "fy": float(fy),
        "cx": float(cx),
        "cy": float(cy),
        "camera_matrix_K": K.tolist(),
        "distortion_coeffs_dist": dist.ravel().tolist(),
        "rvecs": np.asarray(rvecs).tolist(),
        "tvecs": np.asarray(tvecs).tolist(),
        "image_paths": [os.path.abspath(p) for p in img_paths],
        "pattern_sizes_per_view": np.asarray(pattern_sizes_per_view).tolist(),
        "pattern_size": list(pattern_sizes_per_view[0]),
        "errors_per_view": errors_per_view,
        "errors_per_view_definition": "Mean Euclidean reprojection distance in pixels",
        "units": "mm",
        "extrinsics_convention": "P_camera = R_i @ P_board_i + t_i; each view has its own board frame"
    }
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(json_data, f, indent=4, ensure_ascii=False, allow_nan=False)

    # 3. Lưu báo cáo dạng text
    report_path = os.path.join(save_dir, "calibration_report.txt")
    with open(report_path, "w", encoding="utf-8") as f:
        f.write("=== BÁO CÁO HIỆU CHUẨN CAMERA (CHESSBOARD A3 15x10) ===\n")
        f.write(f"Kích thước ảnh : {img_size[0]} x {img_size[1]}\n")
        f.write(f"Số ảnh sử dụng : {len(objpoints)}\n")
        f.write(f"Kích thước 1 ô : {square_size_mm} mm\n")
        f.write(f"Sai số RMS     : {rms:.6f} px\n")
        f.write(f"Sai số Mean    : {mean_reproj_err:.6f} px\n\n")
        f.write("Ma trận Camera K (Intrinsic Matrix):\n")
        f.write(str(K) + "\n\n")
        f.write("Hệ số biến dạng méo thấu kính (Distortion Coefficients):\n")
        f.write(f"k1={dist[0,0]:.6f}, k2={dist[0,1]:.6f}, p1={dist[0,2]:.6f}, p2={dist[0,3]:.6f}, k3={dist[0,4]:.6f}\n\n")
        f.write("Sai số từng ảnh:\n")
        for i, err in enumerate(errors_per_view):
            f.write(f" - Mẫu {i+1:02d}: {err:.4f} px; pattern={pattern_sizes_per_view[i]}; "
                    f"ảnh={os.path.abspath(img_paths[i])}\n")
            f.write(f"   rvec={np.asarray(rvecs[i]).ravel()}; tvec(mm)={np.asarray(tvecs[i]).ravel()}\n")
        f.write("\nMỗi ảnh có hệ bàn cờ riêng. Không lấy trung bình ngoại tại và không tự chọn ảnh làm world.\n")

    # 4. Xuất ảnh khử méo mẫu (Undistorted Preview)
    if img_paths:
        und_dir = os.path.join(save_dir, "undistorted_samples")
        os.makedirs(und_dir, exist_ok=True)
        for i, p in enumerate(img_paths[:8]):
            img = read_image_unicode(p)
            if img is None:
                continue
            if img.shape[1::-1] != tuple(img_size):
                print(f"[BỎ QUA] Ảnh preview đã đổi kích thước: {p}")
                continue
            und = cv2.undistort(img, K, dist)
            out_p = os.path.join(und_dir, f"undistorted_{i+1:02d}.jpg")
            save_image_unicode(out_p, und)
        print(f"[INFO] Đã lưu ảnh mẫu đã khử méo vào: {und_dir}")

    print(f"\n[THÀNH CÔNG] Đã lưu kết quả:")
    print(f"  -> File NumPy: {os.path.abspath(npz_path)}")
    print(f"  -> File JSON:  {os.path.abspath(json_path)}")
    print(f"  -> Báo cáo:    {os.path.abspath(report_path)}")

    return {"K": K, "dist": dist, "rms": rms, "mean_error": mean_reproj_err,
            "image_size": tuple(img_size), "rvecs": np.asarray(rvecs),
            "tvecs": np.asarray(tvecs), "image_paths": json_data["image_paths"],
            "pattern_sizes_per_view": np.asarray(pattern_sizes_per_view, dtype=np.int32),
            "pattern_size": tuple(pattern_sizes_per_view[0]),
            "errors_per_view": np.asarray(errors_per_view), "square_size_mm": float(square_size_mm)}


def _integer_pair(value, label, minimum=1):
    values = np.asarray(value, dtype=np.float64).reshape(-1)
    if (values.size != 2 or not np.isfinite(values).all()
            or np.any(values != np.floor(values)) or np.any(values < minimum)):
        raise ValueError(f"{label} phải gồm 2 số nguyên >= {minimum}.")
    return tuple(int(v) for v in values)


def read_parameter_file(path):
    if not os.path.isfile(path):
        raise FileNotFoundError(f"Không tìm thấy file thông số: {path}")
    extension = os.path.splitext(os.fspath(path))[1].lower()
    if extension == ".npz":
        with np.load(path, allow_pickle=False) as archive:
            return {key: archive[key] for key in archive.files}
    if extension == ".json":
        with open(path, encoding="utf-8-sig") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            raise ValueError("File JSON thông số phải chứa một object.")
        return data
    raise ValueError("File thông số phải là .npz hoặc .json do chương trình xuất.")


def load_calibration_file(path, nx=None, ny=None, square_size_mm=None,
                          image_width=None, image_height=None):
    """Đọc NPZ/JSON mới hoặc cũ, không bật allow_pickle cho dữ liệu đầu vào.

    Metadata thiếu phải được cung cấp rõ qua CLI, không đoán từ K hoặc ảnh đầu tiên.
    --nx/--ny/--square_size_mm có thể mô tả bàn cờ tham chiếu khác bộ ảnh nội tại.
    """
    data = read_parameter_file(path)
    raw_K = data.get("K", data.get("camera_matrix_K"))
    raw_dist = data.get("dist", data.get("distortion_coeffs_dist"))
    if raw_K is None or raw_dist is None:
        raise ValueError("File thiếu K/dist (hoặc camera_matrix_K/distortion_coeffs_dist).")
    K, dist = validate_camera_model(raw_K, raw_dist)
    missing = []
    image_size = data.get("image_size")
    if isinstance(image_size, dict):
        image_size = [image_size.get("width"), image_size.get("height")]
    if (image_width is None) != (image_height is None):
        raise ValueError("Phải truyền cả --image_width và --image_height.")
    if image_size is None:
        if image_width is None:
            missing.append("--image_width W --image_height H (đúng kích thước lúc hiệu chuẩn)")
        else:
            image_size = (image_width, image_height)
    else:
        image_size = _integer_pair(image_size, "image_size")
        if image_width is not None and image_size != (image_width, image_height):
            raise ValueError("Không được ghi đè image_size có sẵn: K thuộc độ phân giải đã hiệu chuẩn.")
    pattern = data.get("pattern_size")
    if pattern is None and "pattern_sizes_per_view" in data:
        patterns = np.asarray(data["pattern_sizes_per_view"]).reshape(-1, 2)
        if len(patterns):
            pattern = patterns[0]
    if (nx is None) != (ny is None):
        raise ValueError("Phải truyền cả --nx và --ny để chỉ định bàn cờ tham chiếu.")
    if nx is not None:
        pattern = (nx, ny)
    if pattern is None:
        missing.append("--nx 15 --ny 10 (số góc trong thực tế của bàn cờ tham chiếu)")
    if square_size_mm is None:
        square_size_mm = data.get("square_size_mm")
    if square_size_mm is None:
        missing.append("--square_size_mm S (cạnh ô thực tế đã đo, đơn vị mm)")
    if missing:
        raise ValueError("File hiệu chuẩn cũ thiếu metadata. Hãy bổ sung: " + "; ".join(missing))
    image_size = _integer_pair(image_size, "image_size")
    pattern = _integer_pair(pattern, "pattern_size", minimum=2)
    square_size_mm = float(np.asarray(square_size_mm).item())
    make_object_points(pattern, square_size_mm)  # Kiểm tra số hữu hạn / kích thước dương.
    return {"K": K, "dist": dist, "image_size": image_size,
            "pattern_size": pattern, "square_size_mm": square_size_mm,
            "calibration_file": os.path.abspath(path)}


def load_world_reference(path):
    """Nạp tư thế đã lưu; không phát hiện bàn cờ và không gọi calibrateCamera/solvePnP."""
    data = read_parameter_file(path)
    model = load_calibration_file(path)
    missing = [key for key in ("rvec", "R", "tvec", "world_definition") if key not in data]
    if missing:
        raise ValueError("File chưa phải hệ bàn đã xác lập (thiếu " + ", ".join(missing)
                         + "). Hãy chạy set-world và xác nhận trước.")
    if "confirmed" in data and not bool(np.asarray(data["confirmed"]).item()):
        raise ValueError("Hệ bàn trong file chưa được xác nhận. Hãy chạy set-world.")
    if str(np.asarray(data.get("units", "mm")).item()) != "mm":
        raise ValueError("Hệ bàn cần có đơn vị mm.")
    pose = build_pose_matrices(model["K"], data["rvec"], data["tvec"])
    R, t = validate_pose(data["R"], data["tvec"])
    if not np.allclose(R, pose["R"], atol=1e-7):
        raise ValueError("R và rvec trong file không nhất quán; cần xác lập lại hệ bàn.")
    pose.update(build_board_homographies(model["K"], R, t))
    for key in ("T_camera_from_board", "T_board_from_camera", "T_cw", "T_wc", "C_board"):
        if key in data:
            supplied = np.asarray(data[key], dtype=np.float64).reshape(pose[key].shape)
            if not np.isfinite(supplied).all() or not np.allclose(supplied, pose[key], atol=1e-6):
                raise ValueError(f"{key} không khớp R/tvec trong file.")
    for key in ("H_board_to_image", "H_image_to_board"):
        if key in data:
            supplied = np.asarray(data[key], dtype=np.float64).reshape(3, 3)
            invert_homography(supplied)
            expected = pose[key]
            scale = np.vdot(expected, supplied) / np.vdot(expected, expected)
            if abs(scale) < 1e-15 or not np.allclose(supplied / scale, expected, rtol=1e-6, atol=1e-7):
                raise ValueError(f"{key} không khớp K/R/tvec; cần xác lập lại hệ bàn.")
    points = make_object_points(model["pattern_size"], model["square_size_mm"])
    if np.any(((R @ points.T).T + t)[:, 2] <= 0):
        raise ValueError("Hệ bàn trong file đặt các góc phía sau camera.")
    validated = {key: model[key] for key in ("K", "dist", "image_size", "pattern_size", "square_size_mm")}
    model.update(data)
    model.update(validated)
    model.update(pose)
    model["world_file"] = os.path.abspath(path)
    return model


WORLD_DEFINITION = (
    "World gắn với bàn cờ tham chiếu đã xác nhận. O là góc trong ngoài cùng được "
    "đánh dấu trên ảnh; người dùng đối chiếu với dấu vật lý trên bàn cờ. "
    "+X dọc hàng, +Y dọc cột của lưới đã sắp thứ tự; "
    "điểm (j*s, i*s, 0) mm; +Z = +X cross +Y theo bàn tay phải. "
    "Dấu của +Z không mặc định là chiều cao phía trên bàn. "
    "Camera OpenCV: +X sang phải ảnh, +Y xuống dưới ảnh, +Z ra trước camera. "
    "Chưa có phép đổi từ world sang hệ gốc robot."
)


def draw_world_axes(image, pose, K, dist, axis_length_mm):
    """Vẽ trên ảnh GỐC bằng projectPoints, gồm đầy đủ méo thấu kính."""
    image = image.copy()
    axes = np.array([[0, 0, 0], [axis_length_mm, 0, 0],
                     [0, axis_length_mm, 0], [0, 0, axis_length_mm]], np.float64)
    projected, _ = cv2.projectPoints(axes, pose["rvec"], pose["tvec"], K, dist)
    projected = projected.reshape(-1, 2)
    camera_axes = (pose["R"] @ axes.T).T + np.asarray(pose["tvec"]).reshape(3)
    if not np.isfinite(projected).all() or np.any(camera_axes[:, 2] <= 0):
        raise ValueError("Không vẽ được đủ các trục trước camera; hãy chụp lại bàn cờ.")
    # Tránh overflow int32 của OpenCV với mô hình méo suy biến.
    if np.max(np.abs(projected)) > 1e7:
        raise ValueError("Hình chiếu trục quá xa ảnh; hãy kiểm tra K/dist và chụp lại.")
    points = np.rint(projected).astype(int)
    origin = tuple(points[0])
    for end, label, color in zip(points[1:], ("+X", "+Y", "+Z"),
                                 ((0, 0, 255), (0, 200, 0), (255, 100, 0))):
        cv2.arrowedLine(image, origin, tuple(end), color, 3, cv2.LINE_AA, tipLength=0.15)
        cv2.putText(image, label, tuple(end + [8, -8]), cv2.FONT_HERSHEY_SIMPLEX,
                    0.7, color, 2, cv2.LINE_AA)
    cv2.circle(image, origin, 8, (0, 255, 255), 2, cv2.LINE_AA)
    cv2.putText(image, "O (0,0,0)", (origin[0] + 10, origin[1] + 25),
                cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2, cv2.LINE_AA)
    return image


def put_lines(image, lines, top=24):
    """Chữ ASCII vì font Hershey của OpenCV không hỗ trợ đầy đủ tiếng Việt."""
    for i, line in enumerate(lines):
        point = (10, top + i * 24)
        cv2.putText(image, line, point, cv2.FONT_HERSHEY_SIMPLEX, 0.52,
                    (0, 0, 0), 3, cv2.LINE_AA)
        cv2.putText(image, line, point, cv2.FONT_HERSHEY_SIMPLEX, 0.52,
                    (255, 255, 255), 1, cv2.LINE_AA)


def draw_corner_order(image, corners, pattern_size):
    """Đánh số 0..N-1 đúng hàng/cột đã chọn, cho phép đối chiếu 2D với bàn."""
    image = image.copy()
    corners = np.asarray(corners, dtype=np.float32).reshape(-1, 1, 2)
    cv2.drawChessboardCorners(image, tuple(pattern_size), corners, True)
    for index, xy in enumerate(corners.reshape(-1, 2)):
        position = tuple(np.rint(xy + [4, -4]).astype(int))
        cv2.putText(image, str(index), position, cv2.FONT_HERSHEY_SIMPLEX,
                    0.30, (0, 0, 0), 2, cv2.LINE_AA)
        cv2.putText(image, str(index), position, cv2.FONT_HERSHEY_SIMPLEX,
                    0.30, (255, 255, 255), 1, cv2.LINE_AA)
    return image


def save_world_calibration(save_dir, calibration, pose, reference_frame, pattern_size,
                           detected_pattern_size, origin_index, swap_axes, plane_z_mm):
    """Chỉ ghi world_*, không ghi đè camera_calibration.*."""
    os.makedirs(save_dir, exist_ok=True)
    annotated = reference_frame.copy()
    if "reference_corners_px" in pose:
        annotated = draw_corner_order(annotated, pose["reference_corners_px"], pattern_size)
    annotated = draw_world_axes(annotated, pose, calibration["K"],
                                calibration["dist"], 2 * calibration["square_size_mm"])
    put_lines(annotated, ["CONFIRMED WORLD | RAW IMAGE | mm | +Z = +X cross +Y",
                          f"Pattern: {pattern_size} | square: {calibration['square_size_mm']:g} mm",
                          f"Reference RMS: {pose['reprojection_rms_error_px']:.4f} px",
                          f"Measurement plane: Zw = {plane_z_mm:+g} mm"])
    raw_path = os.path.abspath(os.path.join(save_dir, "world_reference_raw.png"))
    axes_path = os.path.abspath(os.path.join(save_dir, "world_reference_axes.png"))
    for path, img in ((raw_path, reference_frame), (axes_path, annotated)):
        if not save_image_unicode(path, img):
            raise OSError(f"Không lưu được ảnh tham chiếu: {path}")
    data = dict(pose)
    data.update(build_board_homographies(calibration["K"], pose["R"], pose["tvec"]))
    data.update({"K": calibration["K"], "dist": calibration["dist"],
                 "image_size": np.asarray(calibration["image_size"]),
                 "square_size_mm": calibration["square_size_mm"],
                 "pattern_size": np.asarray(pattern_size),
                 "detected_pattern_size": np.asarray(detected_pattern_size),
                 "origin_index": int(origin_index), "swap_axes": bool(swap_axes),
                 "units": "mm", "world_definition": WORLD_DEFINITION,
                 "confirmed": True,
                 "transform_convention": "P_camera = R @ P_board + t",
                 "board_plane_definition": "Zb = 0; board coordinates in mm, not robot coordinates",
                 "corner_order_definition": "index = i * columns + j; P_board = (j*s, i*s, 0)",
                 "origin_row_reversed": origin_index in (2, 3),
                 "origin_column_reversed": origin_index in (1, 2),
                 "homography_pixel_convention": "Ideal undistorted pixel coordinates with the SAME K; cv2.undistortPoints(raw_pixel, K, dist, P=K)",
                 "tvec_definition": "Tọa độ gốc world trong hệ camera (mm)",
                 "camera_position_definition": "Tâm camera trong world = -R.T @ t (mm)",
                 "P_projection_definition": "K @ [R | t], ideal projection, excludes lens distortion",
                 "plane_z_mm": float(plane_z_mm),
                 "established_at": datetime.now(timezone.utc).isoformat(),
                 "calibration_file": calibration["calibration_file"],
                 "reference_raw_image": raw_path, "reference_axes_image": axes_path})
    json_data = {k: v.tolist() if isinstance(v, np.ndarray) else v for k, v in data.items()}
    # JSON và NPZ có cùng tên trường; NPZ chỉ gồm numeric/string arrays, không có pickle.
    for basename in ("world_reference", "world_calibration"):
        np.savez(os.path.join(save_dir, basename + ".npz"), **data)
        with open(os.path.join(save_dir, basename + ".json"), "w", encoding="utf-8") as f:
            json.dump(json_data, f, indent=4, ensure_ascii=False, allow_nan=False)
    with open(os.path.join(save_dir, "world_calibration_report.txt"), "w", encoding="utf-8") as f:
        f.write("=== XÁC LẬP WORLD THAM CHIẾU CHO CAMERA CỐ ĐỊNH ===\n")
        f.write(f"Thời điểm UTC: {data['established_at']}\n")
        f.write(f"Nội tại nguồn: {data['calibration_file']}\n")
        f.write(f"Đơn vị: mm; kích thước ảnh (WxH): {calibration['image_size']}\n")
        f.write(f"Cạnh ô thực tế: {calibration['square_size_mm']} mm\n")
        f.write(f"Pattern detector: {detected_pattern_size}; lưới world sau sắp thứ tự: {pattern_size}\n")
        f.write(f"Góc gốc detector: {origin_index}; đổi X/Y: {swap_axes}\n")
        f.write(WORLD_DEFINITION + "\n\n")
        f.write("P_camera = R @ P_board + t\n")
        f.write("tvec là tọa độ GỐC WORLD trong camera, không phải vị trí camera trong world.\n")
        f.write("camera_position_world_mm = -R.T @ t là vị trí TÂM CAMERA trong world.\n")
        f.write("T_cw: world -> camera; T_wc: camera -> world.\n")
        f.write("P_projection = K @ [R | t] là ma trận chiếu lý tưởng, chưa gồm méo.\n")
        f.write("Vẽ ảnh gốc/tính sai số bằng cv2.projectPoints(K, dist).\n")
        f.write("T_camera_from_board: bàn -> camera; T_board_from_camera: camera -> bàn.\n")
        f.write("C_board = -R.T @ t là tâm camera trong hệ bàn; tvec là gốc bàn trong camera.\n")
        f.write("H_board_to_image = K @ [r1 r2 t], H_image_to_board là nghịch đảo.\n")
        f.write("H chỉ áp dụng Zb=0 và pixel LÝ TƯỞNG đã khử méo bằng undistortPoints(P=K).\n")
        f.write("Không áp dụng H trực tiếp lên pixel ảnh gốc có méo; không khử méo hai lần.\n")
        for name in ("K", "dist", "rvec", "R", "tvec", "T_cw", "T_wc",
                     "camera_position_world_mm", "P_projection", "T_camera_from_board",
                     "T_board_from_camera", "C_board", "H_board_to_image", "H_image_to_board"):
            f.write(f"\n{name}:\n{np.array2string(np.asarray(data[name]), precision=9)}\n")
        for name in ("reprojection_mean_error_px", "reprojection_rms_error_px",
                     "reprojection_max_error_px"):
            f.write(f"{name}: {pose[name]:.6f} px\n")
        f.write(f"\nMặt phẳng đo Zw = {plane_z_mm:+g} mm, có dấu trong hệ world.\n")
        f.write("Một pixel chỉ xác định điểm khi giả định điểm nằm trên mặt phẳng này.\n")
        f.write("Điểm click được hiển thị/in console: point_camera_mm và point_world_mm, "
                "khác với vị trí tâm camera camera_position_world_mm.\n")
        f.write("Ngoại tại cố định sau xác nhận; chỉ chọn lại bằng thao tác chủ động.\n")
        f.write("Giữ nguyên camera (vị trí, tiêu cự/zoom, độ phân giải) và mặt phẳng làm việc.\n")
        f.write("Có thể bỏ bàn cờ sau xác nhận. Để điều khiển SCARA theo hệ robot, "
                "cần xác lập thêm biến đổi world -> hệ gốc robot.\n")
        f.write(f"Ảnh gốc: {raw_path}\nẢnh gốc/trục đã xác nhận: {axes_path}\n")
    return data


def display_to_raw_pixel(x, y, raw_size, display_size):
    """Ánh xạ tâm pixel của ảnh cv2.resize về ảnh gốc, không thay đổi K/dist."""
    raw_w, raw_h = raw_size
    display_w, display_h = display_size
    if not (0 <= x < display_w and 0 <= y < display_h):
        raise ValueError("Click nằm ngoài vùng ảnh hiển thị.")
    u = np.clip((x + 0.5) * raw_w / display_w - 0.5, 0, raw_w - 1)
    v = np.clip((y + 0.5) * raw_h / display_h - 0.5, 0, raw_h - 1)
    return np.array([u, v], np.float64)


def open_camera(cam_idx):
    cap = cv2.VideoCapture(cam_idx, cv2.CAP_DSHOW) if sys.platform.startswith("win") else cv2.VideoCapture(cam_idx)
    if not cap.isOpened():
        cap.release()
        cap = cv2.VideoCapture(cam_idx)
    if not cap.isOpened():
        cap.release()
        raise RuntimeError(f"Không mở được camera {cam_idx}.")
    return cap


def require_frame_size(frame, image_size, source="Frame camera"):
    if frame is None:
        raise RuntimeError(f"Không đọc được ảnh từ {source}.")
    if frame.shape[1::-1] != tuple(image_size):
        raise ValueError(f"{source} {frame.shape[1::-1]} khác kích thước hiệu chuẩn "
                         f"{tuple(image_size)}. Chọn đúng chế độ camera/ảnh hoặc hiệu chuẩn lại; "
                         "không dùng/scale K âm thầm.")


def load_reference_image(path, image_size):
    if not os.path.isfile(path):
        raise FileNotFoundError(f"Không tìm thấy ảnh: {path}")
    frame = read_image_unicode(path)
    require_frame_size(frame, image_size, f"Ảnh {path}")
    return frame


def run_world_mode(calibration, cam_idx=0, save_dir="calib_output", plane_z_mm=0.0,
                   reference_image=None, setup_only=False):
    """set-world chỉ lưu rồi thoát; mặc định giữ tương thích --world gộp cũ."""
    if not np.isfinite(plane_z_mm):
        raise ValueError("--plane_z_mm phải hữu hạn; đây là Zw có dấu.")
    if setup_only and plane_z_mm != 0:
        raise ValueError("set-world xác lập mặt bàn Zb=0; --plane_z_mm khác 0 chỉ dành cho --world cũ.")
    K, dist = calibration["K"], calibration["dist"]
    image_size = tuple(calibration["image_size"])
    pattern = calibration["pattern_size"]
    square = calibration["square_size_mm"]
    print("\n[WORLD] Đặt bàn cờ cố định tại vị trí tham chiếu, giữ camera ở vị trí vận hành.")
    print("Đánh dấu vật lý một góc TRONG ngoài cùng để nhận biết gốc sau mỗi lần xác lập.")
    print("SPACE: chụp tham chiếu. O: đổi góc gốc; H/V: đảo cột/hàng; X: đổi X/Y.")
    print("Trong ảnh đóng băng, click gần 1 trong 4 góc ngoài cùng cũng chọn được gốc.")
    print("Đối chiếu O/+X/+Y/+Z với bàn cờ thật rồi ENTER hoặc C để xác nhận và lưu.")
    print("R: bỏ world hiện tại và chụp lại chủ động; Q/ESC: thoát.")
    if setup_only:
        print("SET-WORLD: sau xác nhận sẽ lưu world_reference.json/.npz rồi thoát; dùng measure để đo.")
    else:
        print(f"Sau xác nhận: click trái để đo, giả định điểm nằm trên Zw = {plane_z_mm:+g} mm.")
    print("Không suy ra được tọa độ 3D bất kỳ từ một pixel; world chưa phải hệ robot.")
    image_frame = load_reference_image(reference_image, image_size) if reference_image else None
    cap = None if reference_image else open_camera(cam_idx)
    window = "World - RAW image / fixed camera / mm"
    state = {"mode": "capture", "reference": None, "corners": None,
             "detected_pattern": None, "origin": 0, "swap": False,
             "pose": None, "measurement": None, "message": "",
             "display_size": None, "dirty": False}
    if image_frame is not None:
        found, detected, corners = detect_corners(cv2.cvtColor(image_frame, cv2.COLOR_BGR2GRAY), pattern)
        if not found:
            raise ValueError(f"Không tìm thấy đầy đủ bàn cờ {pattern} trên ảnh tham chiếu {reference_image}; chưa lưu hệ bàn.")
        state.update(mode="review", reference=image_frame.copy(), corners=corners.copy(),
                     detected_pattern=detected, dirty=True)

    def on_mouse(event, x, y, flags, userdata):
        if event != cv2.EVENT_LBUTTONDOWN or state["display_size"] is None:
            return
        try:
            uv = display_to_raw_pixel(x, y, image_size, state["display_size"])
            if state["mode"] == "review":
                cols, rows = state["detected_pattern"]
                grid = state["corners"].reshape(rows, cols, 2)
                endpoints = np.array([grid[0, 0], grid[0, -1], grid[-1, -1], grid[-1, 0]])
                distances = np.linalg.norm(endpoints - uv, axis=1)
                threshold = 35 * max(image_size[0] / state["display_size"][0],
                                     image_size[1] / state["display_size"][1])
                if distances.min() <= threshold:
                    state["origin"] = int(np.argmin(distances))
                    state["pose"] = None
                    state["dirty"] = True
            elif state["mode"] == "measure":
                pose = state["pose"]
                result = pixel_to_plane(uv, K, dist, pose["R"], pose["tvec"], plane_z_mm)
                state["measurement"] = result
                state["message"] = ""
                print(f"[ĐIỂM CLICK] Pixel ảnh gốc (u,v) = {uv.round(3)}")
                print(f"  Camera (Xc,Yc,Zc), mm = {result['point_camera_mm'].round(4)}")
                print(f"  World  (Xw,Yw,Zw), mm = {result['point_world_mm'].round(4)} "
                      f"[giả định Zw={plane_z_mm:+g} mm]")
        except (ValueError, cv2.error) as exc:
            state["measurement"] = None
            state["message"] = "INVALID INTERSECTION - see console"
            print(f"[KHÔNG ĐO ĐƯỢC] {exc}")

    try:
        if cap is not None:
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, image_size[0])
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, image_size[1])
            for _ in range(5):
                cap.read()
        # Dùng ảnh resize tường minh và AUTOSIZE để callback luôn thuộc đúng buffer hiển thị.
        cv2.namedWindow(window, cv2.WINDOW_AUTOSIZE)
        cv2.setMouseCallback(window, on_mouse)
        while True:
            if cap is not None:
                ok, frame = cap.read()
                if not ok:
                    raise RuntimeError("Không đọc được frame webcam; dừng đo.")
            else:
                frame = image_frame
            require_frame_size(frame, image_size)
            if state["mode"] == "review" and state["dirty"]:
                state["dirty"] = False
                ordered, ordered_pattern = reorder_reference_corners(
                    state["corners"], state["detected_pattern"], state["origin"], state["swap"])
                try:
                    state["pose"] = estimate_world_pose(ordered, ordered_pattern, square, K, dist)
                    state["ordered_pattern"] = ordered_pattern
                    # Xác nhận chỉ được phép sau khi đủ các trục đã vẽ hợp lệ.
                    draw_world_axes(state["reference"], state["pose"], K, dist, 2 * square)
                    state["message"] = ""
                    print(f"[THAM CHIẾU CHỜ DUYỆT] O={state['origin']}; đổi X/Y={state['swap']}; "
                          f"pattern={ordered_pattern}; RMS={state['pose']['reprojection_rms_error_px']:.4f} px")
                except (ValueError, cv2.error) as exc:
                    state["pose"] = None
                    state["message"] = "POSE INVALID - R to recapture; see console"
                    print(f"[LỖI NGOẠI TẠI] {exc}")
            canvas = (state["reference"] if state["mode"] == "review" else frame).copy()
            if state["mode"] == "review":
                ordered, ordered_pattern = reorder_reference_corners(
                    state["corners"], state["detected_pattern"], state["origin"], state["swap"])
                canvas = draw_corner_order(canvas, ordered, ordered_pattern)
            if state["pose"] is not None:
                canvas = draw_world_axes(canvas, state["pose"], K, dist, 2 * square)
            if state["mode"] == "capture":
                lines = ["RAW | SPACE: capture fixed reference | Q: quit",
                         "Mark a physical INNER corner for O; keep camera fixed."]
            elif state["mode"] == "review":
                lines = ["FROZEN RAW | Verify physical O, +X, +Y and right-handed +Z",
                         "O/click: origin | H/V: flip | X: swap | ENTER/C: save | R: reset"]
                if state["pose"] is not None:
                    lines.append(f"Reference RMS: {state['pose']['reprojection_rms_error_px']:.4f} px")
            else:
                lines = [f"MEASURE RAW | Fixed world | plane Zw={plane_z_mm:+g} mm",
                         "Left click: coordinates (mm) | R: reset world | Q: quit"]
                measurement = state["measurement"]
                if measurement is not None:
                    uv = measurement["pixel"]
                    cv2.drawMarker(canvas, tuple(np.rint(uv).astype(int)), (0, 255, 255),
                                   cv2.MARKER_CROSS, 20, 2)
                    lines.extend([f"Raw pixel: ({uv[0]:.2f}, {uv[1]:.2f})",
                                  "Camera mm: " + np.array2string(measurement["point_camera_mm"], precision=3),
                                  "World mm: " + np.array2string(measurement["point_world_mm"], precision=3)])
            if state["message"]:
                lines.append(state["message"])
            scale = min(1.0, 1100 / image_size[0], 750 / image_size[1])
            display_size = (max(1, round(image_size[0] * scale)), max(1, round(image_size[1] * scale)))
            display = cv2.resize(canvas, display_size, interpolation=cv2.INTER_AREA)
            put_lines(display, lines)
            state["display_size"] = display_size
            cv2.imshow(window, display)
            key = cv2.waitKey(20) & 0xFF
            if key in (27, ord("q")) or cv2.getWindowProperty(window, cv2.WND_PROP_VISIBLE) < 1:
                break
            if key == ord("r"):
                if image_frame is not None:
                    state.update(mode="review", pose=None, measurement=None, message="",
                                 dirty=True, origin=0, swap=False)
                    print("[WORLD] Đã đặt lại thứ tự trên ảnh tham chiếu; cần xác nhận mới.")
                else:
                    state.update(mode="capture", reference=None, corners=None, pose=None,
                                 measurement=None, message="", dirty=False, origin=0, swap=False)
                    print("[WORLD] Đã bỏ ngoại tại đang dùng. SPACE để chọn lại; cần xác nhận mới.")
            elif key == 32 and state["mode"] == "capture":
                found, detected, corners = detect_corners(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY), pattern)
                if found:
                    state.update(mode="review", reference=frame.copy(), corners=corners.copy(),
                                 detected_pattern=detected, origin=0, swap=False, dirty=True, pose=None)
                else:
                    state["message"] = "Full board not found - adjust board and SPACE again"
                    print("[CHƯA CHỤP] Cần phát hiện đầy đủ các góc bàn cờ trong frame.")
            elif state["mode"] == "review":
                if key in (ord("o"), ord("x"), ord("h"), ord("v")):
                    if key == ord("o"):
                        state["origin"] = (state["origin"] + 1) % 4
                    elif key == ord("h"):
                        state["origin"] = (1, 0, 3, 2)[state["origin"]]
                    elif key == ord("v"):
                        state["origin"] = (3, 2, 1, 0)[state["origin"]]
                    else:
                        state["swap"] = not state["swap"]
                    state["pose"] = None
                    state["dirty"] = True
                elif key in (10, 13, ord("c")) and state["pose"] is not None and not state["dirty"]:
                    # Ghi thành công trước khi bật đo; không có bước tự cập nhật solvePnP sau đây.
                    saved = save_world_calibration(save_dir, calibration, state["pose"], state["reference"],
                                           state["ordered_pattern"], state["detected_pattern"],
                                           state["origin"], state["swap"], plane_z_mm)
                    print(f"[WORLD ĐÃ CỐ ĐỊNH] Lưu tại {os.path.abspath(save_dir)}")
                    print("  Tâm camera trong world, mm =",
                          state["pose"]["camera_position_world_mm"].round(4))
                    if setup_only:
                        print("[SET-WORLD] Hoàn tất. Chạy measure --world_file để đo trên mặt bàn Zb=0.")
                        return saved
                    state["mode"] = "measure"
                    print("  Có thể bỏ bàn cờ; giữ camera và mặt phẳng cố định. Click trái để đo.")
    finally:
        if cap is not None:
            cap.release()
        cv2.destroyAllWindows()


def draw_measurement_points(image, points, reserved_top=0):
    """Nhãn X/Y có nền tối, số điểm và đường nối; chỉ vẽ, không đổi tọa độ.

    Chọn vị trí nhãn trong ảnh, ưu tiên tránh thanh trạng thái, các điểm và nhãn khác.
    Trả các hình chữ nhật nhãn (x1,y1,x2,y2) để có thể kiểm tra bố cục ngoại tuyến.
    """
    if not points:
        return []
    height, width = image.shape[:2]
    font, scale, thickness = cv2.FONT_HERSHEY_SIMPLEX, 0.60, 1
    padding, gap = 10, 16
    text_height, baseline = cv2.getTextSize("X: -123.45 mm", font, scale, thickness)[0][1], 6
    line_height = text_height + baseline + 7
    box_height = padding * 2 + line_height + text_height + baseline
    accent, background = (0, 230, 255), (30, 30, 30)
    pixels = [tuple(np.rint(result["pixel"]).astype(int)) for result in points]
    point_bounds = [(x - 13, y - 13, x + 14, y + 14) for x, y in pixels]
    reserved = (0, 0, width, min(height, max(0, reserved_top)))
    labels, rectangles = [], []

    def overlap(a, b):
        return max(0, min(a[2], b[2]) - max(a[0], b[0])) * max(
            0, min(a[3], b[3]) - max(a[1], b[1]))

    for index, (result, pixel) in enumerate(zip(points, pixels), 1):
        x, y = pixel
        point = result["point_board_mm"]
        lines = [f"X: {point[0]:.2f} mm", f"Y: {point[1]:.2f} mm"]
        number = str(index)
        badge_width = max(32, cv2.getTextSize(number, font, scale, 2)[0][0] + 12)
        text_width = max(cv2.getTextSize(line, font, scale, thickness)[0][0] for line in lines)
        box_width = badge_width + 3 * padding + text_width
        candidates = []
        for offset in (0, box_height + 8, -box_height - 8,
                       2 * (box_height + 8), -2 * (box_height + 8)):
            for left in (x + gap, x - gap - box_width):
                candidates.append((left, y - box_height // 2 + offset))
        candidates.extend([(x - box_width // 2, y + gap),
                           (x - box_width // 2, y - gap - box_height)])
        candidates = [(max(2, min(left, width - box_width - 2)),
                       max(2, min(top, height - box_height - 2))) for left, top in candidates]

        def cost(position):
            left, top = position
            rect = (left, top, left + box_width, top + box_height)
            obstruction = (10 * overlap(rect, reserved)
                           + 4 * sum(overlap(rect, previous) for previous in rectangles)
                           + 8 * sum(overlap(rect, marker) for marker in point_bounds))
            distance = (x - np.clip(x, rect[0], rect[2])) ** 2 + (y - np.clip(y, rect[1], rect[3])) ** 2
            return obstruction, distance

        left, top = min(candidates, key=cost)
        rect = (left, top, left + box_width, top + box_height)
        rectangles.append(rect)
        labels.append((rect, lines, number, badge_width))

    # Đường nối vẽ trước nền nhãn; dấu tâm vẽ cuối để luôn nhìn rõ vị trí click.
    for pixel, rect in zip(pixels, rectangles):
        anchor = (int(np.clip(pixel[0], rect[0], rect[2])),
                  int(np.clip(pixel[1], rect[1], rect[3])))
        cv2.line(image, pixel, anchor, (0, 0, 0), 3, cv2.LINE_AA)
        cv2.line(image, pixel, anchor, accent, 1, cv2.LINE_AA)
    for (left, top, right, bottom), lines, number, badge_width in labels:
        cv2.rectangle(image, (left, top), (right, bottom), background, -1)
        cv2.rectangle(image, (left, top), (right, bottom), accent, 1, cv2.LINE_AA)
        badge_top = top + (box_height - 32) // 2
        cv2.rectangle(image, (left + padding, badge_top),
                      (left + padding + badge_width, badge_top + 32), accent, -1)
        number_size = cv2.getTextSize(number, font, scale, 2)[0]
        cv2.putText(image, number, (left + padding + (badge_width - number_size[0]) // 2,
                                   badge_top + (32 + number_size[1]) // 2),
                    font, scale, background, 2, cv2.LINE_AA)
        for row, line in enumerate(lines):
            cv2.putText(image, line, (left + badge_width + 2 * padding,
                                     top + padding + text_height + row * line_height),
                        font, scale, (255, 255, 255), thickness, cv2.LINE_AA)
    for pixel in pixels:
        cv2.drawMarker(image, pixel, (0, 0, 0), cv2.MARKER_CROSS, 20, 6, cv2.LINE_AA)
        cv2.drawMarker(image, pixel, accent, cv2.MARKER_CROSS, 20, 2, cv2.LINE_AA)
        cv2.circle(image, pixel, 2, accent, -1, cv2.LINE_AA)
    return rectangles


def run_measure_mode(world, cam_idx=0, image_path=None):
    """Chỉ đọc pose/H đã lưu, đo nhiều điểm Zb=0; không tìm lại bàn cờ hay nội tại."""
    K, dist = validate_camera_model(world["K"], world["dist"])
    image_size = tuple(world["image_size"])
    R, t = validate_pose(world["R"], world["tvec"])
    H = np.asarray(world["H_image_to_board"], dtype=np.float64).reshape(3, 3)
    invert_homography(H)
    image_frame = load_reference_image(image_path, image_size) if image_path else None
    cap = None if image_path else open_camera(cam_idx)
    window = "Measure - RAW / board Zb=0 / mm / NOT robot coordinates"
    state = {"points": [], "message": ""}
    print("\n[MEASURE] Dùng hệ bàn đã lưu; không thu mẫu hoặc tính lại K/D/ngoại tại.")
    print("Click trái: thêm điểm; C: xóa tất cả; U/BACKSPACE: bỏ điểm cuối; Q/ESC: thoát.")
    print("Ảnh hiển thị là ảnh gốc, đúng độ phân giải của K. Mỗi click chỉ khử méo một lần.")
    print("Kết quả (Xb,Yb) mm theo mặt phẳng bàn Zb=0, CHƯA phải tọa độ robot.")
    print("Nếu camera hoặc mặt bàn dịch chuyển, cần chạy lại set-world trước khi đo.")

    def on_mouse(event, x, y, flags, userdata):
        if event != cv2.EVENT_LBUTTONDOWN:
            return
        try:
            uv = display_to_raw_pixel(x, y, image_size, image_size)
            result = pixel_to_board(uv, K, dist, H, R, t)
            state["points"].append(result)
            state["message"] = ""
            point = result["point_board_mm"]
            print(f"[ĐIỂM {len(state['points'])}] pixel ({uv[0]:.2f}, {uv[1]:.2f}) -> "
                  f"Xb={point[0]:.4f} mm, Yb={point[1]:.4f} mm, Zb=0 (chưa phải hệ robot)")
            if len(state["points"]) > 1:
                previous = state["points"][-2]["point_board_mm"]
                print(f"  Khoảng cách tới điểm trước trên bàn: {np.linalg.norm(point - previous):.4f} mm")
        except (ValueError, cv2.error) as exc:
            state["message"] = "INVALID BOARD POINT - see console"
            print(f"[KHÔNG ĐO ĐƯỢC] {exc}")

    try:
        if cap is not None:
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, image_size[0])
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, image_size[1])
            for _ in range(5):
                cap.read()
        # Không resize: callback nhận trực tiếp pixel ảnh gốc 1280x720 (hoặc size đã lưu).
        cv2.namedWindow(window, cv2.WINDOW_AUTOSIZE)
        cv2.setMouseCallback(window, on_mouse)
        while True:
            if cap is not None:
                ok, frame = cap.read()
                if not ok:
                    raise RuntimeError("Không đọc được frame camera; dừng đo.")
            else:
                frame = image_frame
            require_frame_size(frame, image_size)
            canvas = frame.copy()
            lines = [f"RAW {image_size[0]}x{image_size[1]} | BOARD Zb=0 | mm | NOT robot coordinates",
                     "Left click: add | C: clear all | U/BACKSPACE: undo | Q: quit",
                     f"Marked points: {len(state['points'])} | Keep camera AND table fixed"]
            if state["points"]:
                point = state["points"][-1]["point_board_mm"]
                lines.append(f"Last: Xb={point[0]:.3f} mm, Yb={point[1]:.3f} mm, Zb=0")
            if len(state["points"]) > 1:
                distance = np.linalg.norm(state["points"][-1]["point_board_mm"]
                                          - state["points"][-2]["point_board_mm"])
                lines.append(f"Distance to previous: {distance:.3f} mm "
                             f"(adjacent grid corners: {world['square_size_mm']:g} mm)")
            if state["message"]:
                lines.append(state["message"])
            draw_measurement_points(canvas, state["points"], reserved_top=24 * len(lines) + 10)
            put_lines(canvas, lines)
            cv2.imshow(window, canvas)
            key = cv2.waitKey(20) & 0xFF
            if key in (27, ord("q")) or cv2.getWindowProperty(window, cv2.WND_PROP_VISIBLE) < 1:
                return state["points"]
            if key == ord("c"):
                state["points"].clear()
                state["message"] = ""
                print("[MEASURE] Đã xóa tất cả điểm đánh dấu.")
            elif key in (ord("u"), 8) and state["points"]:
                state["points"].pop()
                state["message"] = ""
                print("[MEASURE] Đã bỏ điểm đánh dấu cuối.")
    finally:
        if cap is not None:
            cap.release()
        cv2.destroyAllWindows()


def main():
    parser = argparse.ArgumentParser(description="Chương trình hiệu chuẩn camera bằng bàn cờ A3 (15x10 giao điểm trong)")
    parser.add_argument("mode", nargs="?", choices=("calibrate", "set-world", "measure"),
                        help="Mặc định calibrate; các lệnh không có tên chế độ vẫn tương thích cách chạy cũ")
    parser.add_argument("--images_glob", type=str, default="", help="Mẫu đường dẫn ảnh (vd: 'calib_data/*.jpg')")
    parser.add_argument("--video", type=str, default="", help="Đường dẫn file video quay bàn cờ")
    parser.add_argument("--camera", type=int, default=-1, help="Index Camera để chạy trực tiếp (vd: 0 cho webcam máy tính, 1 cho camera ngoài)")
    parser.add_argument("--nx", type=int, default=None, help="Số góc trong theo hàng: calib mặc định 15; world đọc metadata")
    parser.add_argument("--ny", type=int, default=None, help="Số góc trong theo cột: calib mặc định 10; world đọc metadata")
    parser.add_argument("--square_size_mm", type=float, default=None,
                        help="Cạnh ô thực tế đã đo (mm): calib mặc định 25; world đọc metadata")
    parser.add_argument("--world", action="store_true", help="Tương thích chế độ cũ: xác lập world và đo ngay")
    parser.add_argument("--calibration_file", default="", help="File nội tại .npz/.json cho set-world hoặc --world cũ")
    parser.add_argument("--world_file", default="", help="File hệ bàn đã xác nhận .npz/.json cho measure")
    parser.add_argument("--reference_image", "--image", dest="reference_image", default="",
                        help="Ảnh gốc cho set-world hoặc measure, thay cho webcam; phải đúng độ phân giải")
    parser.add_argument("--plane_z_mm", type=float, default=0.0, help="Zw có dấu của mặt phẳng đo (mm), mặc định 0")
    parser.add_argument("--image_width", type=int, default=None, help="Chỉ bổ sung nếu file cũ thiếu image_size")
    parser.add_argument("--image_height", type=int, default=None, help="Chỉ bổ sung nếu file cũ thiếu image_size")
    parser.add_argument("--save_dir", type=str, default="calib_output", help="Thư mục lưu kết quả căn chuẩn")
    parser.add_argument("--show", action="store_true", help="Hiển thị xem trước các điểm góc khi chạy batch")
    parser.add_argument("--video_max_frames", type=int, default=45, help="Số frame tối đa trích xuất từ video")
    parser.add_argument("--video_skip", type=int, default=10, help="Số bước nhảy frame khi đọc video")
    args = parser.parse_args()

    if args.world and args.mode is not None:
        parser.error("Không kết hợp --world cũ với tên chế độ; dùng set-world rồi measure.")
    if args.reference_image and args.camera >= 0:
        parser.error("Chỉ chọn một nguồn: --camera hoặc --reference_image/--image.")
    if args.mode == "measure":
        if not args.world_file:
            parser.error("measure cần --world_file calib_output/world_reference.json (hoặc .npz)")
        if (args.calibration_file or args.images_glob or args.video
                or any(value is not None for value in (args.nx, args.ny, args.square_size_mm,
                                                       args.image_width, args.image_height))):
            parser.error("measure chỉ dùng thông số trong --world_file; không thu mẫu hoặc ghi đè metadata.")
        if args.plane_z_mm != 0:
            parser.error("measure dùng homography mặt bàn Zb=0, không dùng --plane_z_mm khác 0.")
        world = load_world_reference(args.world_file)
        run_measure_mode(world, max(args.camera, 0), image_path=args.reference_image or None)
        return
    if args.mode == "set-world":
        if not args.calibration_file:
            parser.error("set-world cần --calibration_file calib_output/camera_calibration.npz (hoặc .json)")
        if args.images_glob or args.video or args.world_file:
            parser.error("set-world dùng --camera hoặc --reference_image và file nội tại, không dùng nguồn batch/--world_file.")
        if args.plane_z_mm != 0:
            parser.error("set-world xác lập mặt bàn Zb=0.")
        calibration = load_calibration_file(args.calibration_file, args.nx, args.ny,
                                             args.square_size_mm, args.image_width, args.image_height)
        run_world_mode(calibration, max(args.camera, 0), args.save_dir, 0.0,
                       reference_image=args.reference_image or None, setup_only=True)
        return
    # Ưu tiên --world trước --camera; không vô tình mở chế độ chụp hiệu chuẩn.
    if args.world:
        if args.world_file or args.reference_image:
            parser.error("--world cũ dùng webcam; để dùng ảnh hoặc file hệ bàn, chọn set-world/measure.")
        if not args.calibration_file:
            parser.error("--world cần --calibration_file calib_output/camera_calibration.npz (hoặc .json)")
        if args.images_glob or args.video:
            parser.error("--world dùng webcam; không kết hợp với --images_glob/--video.")
        calibration = load_calibration_file(
            args.calibration_file, args.nx, args.ny, args.square_size_mm,
            args.image_width, args.image_height)
        run_world_mode(calibration, max(args.camera, 0), args.save_dir, args.plane_z_mm)
        return
    if (args.calibration_file or args.world_file or args.reference_image
            or args.image_width is not None or args.image_height is not None):
        parser.error("calibrate không dùng --calibration_file/--world_file/--reference_image; chọn set-world hoặc measure.")
    args.nx = 15 if args.nx is None else args.nx
    args.ny = 10 if args.ny is None else args.ny
    args.square_size_mm = 25.0 if args.square_size_mm is None else args.square_size_mm
    make_object_points((args.nx, args.ny), args.square_size_mm)

    # Trường hợp 1: Chạy camera trực tiếp
    if args.camera >= 0:
        run_live_camera_calibration(
            cam_idx=args.camera,
            nx=args.nx,
            ny=args.ny,
            square_size_mm=args.square_size_mm,
            save_dir=args.save_dir
        )
        return

    # Trường hợp 2: Lấy ảnh từ video
    img_paths = []
    if args.video:
        video_frames_dir = os.path.join(args.save_dir, "video_frames")
        img_paths = collect_images_from_video(
            args.video, video_frames_dir,
            max_frames=args.video_max_frames,
            skip=args.video_skip
        )
    # Trường hợp 3: Lấy từ glob ảnh
    elif args.images_glob:
        img_paths = sorted(glob.glob(args.images_glob))
    else:
        # Nếu người dùng chạy python calibrate_camera_a3.py mà không truyền tham số:
        # Tự động gợi ý mở camera trực tiếp hoặc hướng dẫn CLI
        print("\n[CHỌN CHẾ ĐỘ HIỆU CHUẨN]")
        print(" Không có tham số đầu vào được chỉ định.")
        print(" 1. Chạy với Camera trực tiếp:       python calibrate_camera_a3.py --camera 0 (hoặc --camera 1)")
        print(' 2. Chạy với thư mục ảnh chụp sẵn:  python calibrate_camera_a3.py --images_glob "data/*.jpg"')
        print(' 3. Chạy với video quay bàn cờ:     python calibrate_camera_a3.py --video "calib.mp4"')
        print("\nĐang tự động thử mở Camera Index 0...")
        run_live_camera_calibration(
            cam_idx=0,
            nx=args.nx,
            ny=args.ny,
            square_size_mm=args.square_size_mm,
            save_dir=args.save_dir
        )
        return

    if not img_paths:
        print(f"[LỖI] Không tìm thấy ảnh nào từ đường dẫn: {args.images_glob}")
        sys.exit(1)

    run_batch_calibration(
        img_paths,
        nx=args.nx,
        ny=args.ny,
        square_size_mm=args.square_size_mm,
        save_dir=args.save_dir,
        show=args.show
    )


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, OSError, cv2.error) as exc:
        print(f"[LỖI] {exc}", file=sys.stderr)
        sys.exit(1)
