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
| 11 | **Quét ô vuông & Lưới Grid tương tác (Bàn cờ 5x5, Ô 5cm)** | Tự động xác định không gian và khoảng cách đều đặn mỗi ô trong phạm vi 4 điểm calib, **chuẩn hóa bàn cờ 5x5 với kích thước ô 5cm (50.0mm)**. Tự động tính toán bước nhảy metric trong hệ Robot Base và ánh xạ phối cảnh projective lên camera qua $H_{\text{robot}\to\text{pixel}}$, đảm bảo khoảng cách giữa các điểm ô tiếp theo luôn chuẩn xác 50mm. Tích hợp thuật toán Thị giác bám nét mực thật, khóa chặt 4 đỉnh ranh giới ô vuông và tâm ô để Robot THL300 di chuyển chuẩn xác tuyệt đối đến từng tọa độ. | ✅ Hoàn thành |
| 12 | **Đánh dấu & Vẽ ký hiệu X / O (Virtual & Physical Robot)** | **Chế độ ảo:** Click chuột phải lên ô vuông trên Camera Live để luân phiên đổi ký hiệu `None ➜ X ➜ O ➜ None` (vẽ X màu đỏ coral, O màu xanh lục neon bo viền với đường kính chuẩn hóa 2.75cm tại tâm ô). **Chế độ Robot thật:** Nút `[✍ VẼ CHỮ X]` và `[✍ VẼ CHỮ O]` điều khiển Robot THL300 cầm bút vẽ trực tiếp vào ô giấy, chuẩn hóa tọa độ Trục 3 (Axis 3): khi vẽ hạ bút tại $Z_{\text{work}} = 0.0\text{mm}$, khi nhấc bút an toàn và kết thúc tại $Z_{\text{safe}} = 20.0\text{mm}$, kiểm tra giới hạn $R \le 300\text{mm}$ và hạ bút vẽ chữ O đúng tâm ô vuông với **đường kính chuẩn xác 2.75cm ($27.5\text{mm}$, bán kính $R = 13.75\text{mm}$)** qua quỹ đạo nội suy 36 cung tròn mượt mà. | ✅ Hoàn thành |

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
