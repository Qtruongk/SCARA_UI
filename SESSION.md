# TIẾN ĐỘ DỰ ÁN

**Đề tài:** Nghiên cứu, phát triển hệ thống điều khiển và giám sát Robot SCARA gắp chuyển Wafer bán dẫn tích hợp Thị giác máy tính  
**Cập nhật:** 29/10/2026  

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
| 12 | **Đánh dấu & Vẽ ký hiệu X / O (Virtual & Physical Robot)** | **Chế độ ảo:** Click chuột phải lên ô vuông trên Camera Live để luân phiên đổi ký hiệu `None ➜ X ➜ O ➜ None` (vẽ X màu đỏ coral, O màu xanh lục neon với bo viền). **Chế độ Robot thật:** Nút `[✍ VẼ CHỮ X]` và `[✍ VẼ CHỮ O]` điều khiển Robot THL300 cầm bút vẽ trực tiếp vào ô giấy, tự động nâng bút an toàn $Z_{\text{safe}} = Z_{\text{work}} + 10\text{mm}$, kiểm tra giới hạn $R \le 300\text{mm}$ và hạ bút vẽ nét chéo hoặc quỹ đạo tròn 36 cung trơn tru. | ✅ Hoàn thành |

---

## Phase 2: Chu trình gắp/đặt Wafer thực tế (Pick & Place)

| STT | Hạng mục công việc | Mô tả chi tiết | Trạng thái |
| :---: | :--- | :--- | :---: |
| 1 | **Tiếp nhận thiết bị & phôi mẫu** | Đã có hộp chứa **FOUP** (*Front Opening Unified Pod*) và phôi mẫu **Wafer** (phiến silicon bán dẫn) thực tế để phục vụ đo đạc và thử nghiệm. | ✅ Hoàn thành |
| 2 | **Thiết kế tay gắp khâu cuối (End-Effector / Vacuum Gripper)** | Đã giao nhiệm vụ thiết kế: Đo đạc kích thước khe chứa (*slot clearance*), hành trình ra vào FOUP, biên dạng tiếp xúc và cơ cấu hút chân không/ngàm giữ Wafer. | 🔄 Đang thực hiện |
| 3 | **Gia công & Lắp ráp tay gắp** | Chế tạo cơ khí/in 3D tay gắp khâu cuối, lắp đặt giác hút chân không và tích hợp lên cụm trục 4 (khâu cuối) của Robot SCARA THL300. | ⏳ Chờ thực hiện |
| 4 | **Thực nghiệm chu trình Pick & Place Wafer** | Thực hiện chu trình gắp/đặt Wafer tự động vào/ra hộp FOUP. | ⏳ Chờ thực hiện |

---

## Phase 3: Tích hợp Thị giác máy tính (Vision-Guided Wafer Handling)

| STT | Hạng mục công việc | Mô tả chi tiết | Trạng thái |
| :---: | :--- | :--- | :---: |
| 1 | **Nghiên cứu & Phát triển mô hình AI (Deep Learning)** | Thu thập và gán nhãn tập dữ liệu ảnh Wafer & hộp FOUP trong điều kiện phản xạ quang học thực tế; huấn luyện mô hình AI (YOLO / Segmentation) để phát hiện vị trí phôi, phân loại trạng thái khay và phát hiện lỗi bề mặt (mẻ cạnh, xước vỡ); tối ưu hóa suy luận thời gian thực (ONNX Runtime / TensorRT) tích hợp vào phần mềm C#. | ⏳ Chờ thực hiện |
| 2 | **Nhận diện tâm và góc định hướng Wafer (Pre-alignment)** | Tự động phát hiện biên dạng tròn phiến Wafer để xác định tâm thực $(X, Y)$ và dò vết khuyết (Notch/Flat) tính góc xoay $\theta$, phục vụ căn chỉnh góc trước khi gắp. | ⏳ Chờ thực hiện |
| 3 | **Nhận diện vị trí khe & Ánh xạ khay FOUP (Wafer Mapping)** | Ứng dụng Camera nhận diện sự hiện diện của Wafer trong từng slot của FOUP, phát hiện lỗi đặt chéo (*Cross-slot*) hoặc nhầm tầng (*Double-slot*). | ⏳ Chờ thực hiện |
| 4 | **Bù trừ sai lệch tọa độ tự động (Vision-Guided Offset)** | Tự động tính toán sai lệch vị trí/góc xoay thực tế so với danh định và bù trừ trực tiếp vào quỹ đạo di chuyển của Robot SCARA để gắp/đặt chính xác tuyệt đối. | ⏳ Chờ thực hiện |
| 5 | **Kiểm định chất lượng & Giám sát an toàn sau gắp** | Kết hợp mô hình AI và xử lý ảnh để kiểm tra phôi sau khi hút (phát hiện mẻ cạnh, trượt phôi, lệch giác hút) và giám sát liên tục quá trình chuyển giao Wafer an toàn. | ⏳ Chờ thực hiện |
