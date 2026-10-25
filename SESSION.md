# TIẾN ĐỘ CÔNG VIỆC - PHASE 1

**Đề tài:** Nghiên cứu, phát triển hệ thống điều khiển và giám sát Robot SCARA gắp chuyển Wafer bán dẫn tích hợp Thị giác máy tính  
**Cập nhật:** 25/10/2026  

---

## Tiến độ Phase 1

| STT | Hạng mục công việc | Mô tả chi tiết | Trạng thái |
| :---: | :--- | :--- | :---: |
| 1 | **Giao tiếp Robot & Giám sát** | Kết nối TCP, bật/tắt Servo, dừng khẩn cấp an toàn (`ProgramStop` + `ProgramBreak`), Watchdog phản hồi chu kỳ 100ms, hệ thống log tập trung. | ✅ Hoàn thành |
| 2 | **Mô phỏng 3D Digital Twin** | Dựng 4 khâu CAD gốc từ `THL300.igs`, lưới tọa độ mở theo mm, vòng giới hạn bán kính 300mm, cập nhật chuyển động thực tế theo mẫu phản hồi controller. | ✅ Hoàn thành |
| 3 | **Quy hoạch quỹ đạo S-Curve** | Giải thuật S-Curve 7 pha, điều khiển Jogging từng trục/tọa độ World (Guide), tự động vẽ biên dạng hình học (Square, Circle). | ✅ Hoàn thành |
| 4 | **Tối ưu hiển thị & Tỉ lệ DPI** | Cấu hình DPI Unawareness, triệt tiêu lỗi lệch layout và méo khung vẽ 3D trên các màn hình có tỉ lệ hiển thị (scale) khác nhau. | ✅ Hoàn thành |
| 5 | **Tích hợp Camera Vision (Live)** | Thu phát video trực tiếp từ webcam (Logitech Brio 100), chọn thiết bị, độ phân giải 1280x720, đo FPS thời gian thực, quản lý hủy luồng an toàn. | ✅ Hoàn thành |
| 6 | **Căn chuẩn & Biến đổi Tọa độ** | Nạp thông số nội tại camera, giải ma trận Hand-Eye 4x4 và Homography phẳng chuyển đổi Pixel sang Robot Base (mục tiêu sai số $\le 1\text{mm}$). | ✅ Hoàn thành |
| 7 | **Bộ công cụ Calib Python** | Bộ script Python thu thập mẫu bàn cờ trên robot, thuật toán giải Hand-Eye Eye-to-Hand và kiểm định sai số còn dư. | ✅ Hoàn thành |
| 8 | **Tái cấu trúc UI & An toàn hệ thống** | Bố cục 4 tab (Home, Guide, Shapes, Camera Vision), bảng màu Navy Slate, thanh trượt Splitter, cơ chế bắt crash log toàn cục (`crash.log`). | ✅ Hoàn thành |
| 9 | **Kiểm thử thực tế với phần cứng** | Kết nối trực tiếp robot THL300 và camera Logitech Brio 100 thật để kiểm tra luồng video và thu thập điểm khớp hoàn tất ma trận Hand-Eye. | 🟡 Đang chờ phần cứng |
