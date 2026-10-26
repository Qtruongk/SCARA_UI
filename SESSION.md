# TIẾN ĐỘ CÔNG VIỆC - PHASE 1

**Đề tài:** Nghiên cứu, phát triển hệ thống điều khiển và giám sát Robot SCARA gắp chuyển Wafer bán dẫn tích hợp Thị giác máy tính  
**Cập nhật:** 25/10/2026  

---

## Phase 1

| STT | Hạng mục công việc | Mô tả chi tiết | Trạng thái |
| :---: | :--- | :--- | :---: |
| 1 | **Giao tiếp Robot & Giám sát** | Kết nối TCP, bật/tắt Servo, dừng khẩn cấp an toàn (`ProgramStop` + `ProgramBreak`), Watchdog phản hồi chu kỳ 100ms, hệ thống log tập trung. | ✅ Hoàn thành |
| 2 | **Mô phỏng 3D Digital Twin** | Dựng 4 khâu CAD gốc từ `THL300.igs`, lưới tọa độ mở theo mm, vòng giới hạn bán kính 300mm, cập nhật chuyển động thực tế theo mẫu phản hồi controller. | ✅ Hoàn thành |
| 3 | **Quy hoạch quỹ đạo S-Curve** | Giải thuật S-Curve 7 pha, điều khiển Jogging từng trục/tọa độ World (Guide), tự động vẽ biên dạng hình học (Square, Circle). | ✅ Hoàn thành |
| 4 | **Tối ưu hiển thị & Tỉ lệ DPI** | Cấu hình DPI Unawareness, triệt tiêu lỗi lệch layout và méo khung vẽ 3D trên các màn hình có tỉ lệ hiển thị (scale) khác nhau. | ✅ Hoàn thành |
| 5 | **Tích hợp Camera Vision (Live)** | Thu phát video trực tiếp từ webcam (Logitech Brio 100), chọn thiết bị, tùy chọn độ phân giải linh hoạt (1280x720,...), quản lý hủy luồng an toàn. | ✅ Hoàn thành |
| 6 | **Căn chuẩn & Biến đổi Tọa độ** | Nạp thông số nội tại camera, giải ma trận Hand-Eye 4x4 và Homography phẳng chuyển đổi Pixel sang Robot Base (sai số $\le 1\text{mm}$), tích hợp cơ chế **Auto-Config Hot-Reload** và **Giao diện Calib nhanh 4 điểm (Planar Homography)** trực tiếp trên ứng dụng. | ✅ Hoàn thành |
| 7 | **Bộ công cụ Calib Python** | Bộ script Python thu thập mẫu bàn cờ trên robot, thuật toán giải Hand-Eye Eye-to-Hand và kiểm định sai số còn dư. | ✅ Hoàn thành |
| 8 | **Tái cấu trúc UI & An toàn hệ thống** | Bố cục 4 tab (Home, Guide, Shapes, Camera Vision), bảng màu Navy Slate, thanh trượt Splitter, cơ chế bắt crash log toàn cục (`crash.log`). | ✅ Hoàn thành |
| 9 | **Điều khiển Point-and-Click Camera** | Chấm điểm trên camera live, tự động tính tọa độ Robot Base qua ma trận Hand-Eye/Homography, kiểm tra $R \le 300\text{mm}$, bảng đọc tọa độ thực thời gian thực (TCP World & Joints) để so sánh vị trí tức thời, di chuyển trực tiếp tới $(X, Y)$ giữ nguyên cao độ $Z$. | ✅ Hoàn thành |
| 10 | **Kiểm thử thực tế với phần cứng** | Kết nối trực tiếp robot THL300 và camera Logitech Brio 100 thật, tích hợp tính năng Calib 4 điểm tương ứng triệt tiêu góc nghiêng camera, trực quan hóa tứ giác làm việc trên Camera Live View. | ✅ Hoàn thành |
| 11 | **Quét ô vuông & Lưới Grid tương tác** | Tự động quét và phát hiện lưới ô vuông trong phạm vi 4 điểm calib, **tích hợp thuật toán Thị giác máy tính bám nét mực thật (1D Valley Caliper + Sub-pixel Parabola + Total Least Squares)** khóa chặt các đường ranh giới và 4 đỉnh góc vào đúng nét kẻ đen trên giấy (triệt tiêu sai lệch lệch ô), loại bỏ hiển thị tâm ô vuông, vẽ Grid và highlight ranh giới từng ô vuông độc lập (#1..#N) để sẵn sàng làm việc với từng ô vuông nhỏ. | ✅ Hoàn thành |
| 12 | **Đánh dấu & Vẽ ký hiệu X / O (Virtual & Physical Robot)** | **Chế độ ảo:** Click chuột phải lên ô vuông trên Camera Live để luân phiên đổi ký hiệu `None ➜ X ➜ O ➜ None` (vẽ X màu đỏ coral, O màu xanh lục neon với bo viền). **Chế độ Robot thật:** Nút `[✍ VẼ CHỮ X]` và `[✍ VẼ CHỮ O]` điều khiển Robot THL300 cầm bút vẽ trực tiếp vào ô giấy, tự động nâng bút an toàn $Z_{\text{safe}} = Z_{\text{work}} + 10\text{mm}$, kiểm tra giới hạn $R \le 300\text{mm}$ và hạ bút vẽ nét chéo hoặc quỹ đạo tròn 36 cung trơn tru. | ✅ Hoàn thành |

---

## Phase 2

Quá trình triển khai thử nghiệm thực tế cho chu trình gắp/đặt Wafer (Pick & Place) tuân theo trình tự phụ thuộc phần cứng như sau:

1. **Đợi thiết bị & phôi mẫu:**
   - Đợi hộp chứa **FOUP** (*Front Opening Unified Pod*).
   - Đợi phôi mẫu **Wafer** (phiến silicon bán dẫn).
2. **Thiết kế tay gắp khâu cuối (End-Effector / Vacuum Gripper):**
   - Sau khi nhận hộp FOUP thực tế: tiến hành đo đạc kích thước khe chứa (*slot clearance*), hành trình ra vào và biên dạng tiếp xúc $\to$ bắt đầu **thiết kế tay gắp khâu cuối**.
3. **Thực nghiệm chu trình Pick & Place Wafer:**
   - Sau khi thiết kế và lắp đặt tay gắp hoàn chỉnh $\to$ triển khai thử nghiệm quy trình gắp/đặt Wafer tự động (kết hợp nhận diện góc xoay/tâm bằng Thị giác máy tính, chuyển động mượt 7-phase S-Curve và điều khiển hút chân không).
