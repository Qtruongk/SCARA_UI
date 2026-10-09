# SCARA Robot Controller UI

## Giới thiệu
Đây là dự án phần mềm điều khiển cho Robot SCARA. Hệ thống cung cấp giao diện người dùng (UI) để kết nối, giám sát trạng thái phần cứng và điều khiển, cũng như tính toán quỹ đạo chuyển động của robot.

## Cấu trúc thư mục
- **Core/**: Chứa logic chính của ứng dụng và giao diện.
  - **Models/**: Định nghĩa cấu trúc dữ liệu và trạng thái của robot.
  - **Services/**: Xử lý logic nghiệp vụ, kết nối phần cứng và toán học chuyển động.
  - **UI/**: Giao diện người dùng.
  - **Utilities/**: Các hàm tiện ích và hằng số cấu hình (IP, Port, thông số S-curve).
- **Libs/**: Chứa các file thư viện quan trọng như driver điều khiển (`TsRemoteLib.dll`) và thư viện ghi log.
- **Docs/**: Chứa các tài liệu liên quan đến dự án.
- **Assets/THL300/**: CAD IGES THL300 gốc, mesh được nhúng trong ứng dụng và metadata nguồn.
- **Tools/THL300/**: Công cụ chuyển đổi và kiểm tra CAD; chỉ cần khi tạo lại mesh.

## Chức năng chính
- Kết nối với Robot Controller qua giao thức TCP.
- Quản lý và theo dõi trạng thái hệ thống (Kết nối, Servo, Cảnh báo).
- Hỗ trợ tính toán và thực thi quỹ đạo chuyển động (S-Curve trajectory).
- Hệ thống ghi log tập trung.
- Digital Twin 3D ngay giữa Home, cập nhật theo vị trí phản hồi thực của robot.

## Digital Twin 3D trên Home

Khung 3D đọc `GetPsnFbkJoint()` và `GetPsnFbkWorld()` từ controller: J1/J2 quay hai tay đòn, J3 nâng/hạ trục và J4 xoay đầu công cụ. Một lần đọc chạy ở nền với chu kỳ yêu cầu 100 ms; tốc độ cập nhật thực tế phụ thuộc controller và mạng. Các thao tác điều khiển cũng chạy ở nền để cửa sổ tiếp tục vẽ mô hình khi robot đang di chuyển.

- Vùng giữa Home hiển thị THL300 trên mặt lưới XY mở, phủ toàn bộ nền hình 3D và không có vách hay viền hộp. Lưới căn theo tọa độ thực, có bước 50/100 mm ở góc nhìn mặc định và tự giãn bước khi thu nhỏ để tránh quá dày. Các trục XYZ màu có nhãn theo mm (X/Y ±600, Z -100…800); mặt gá robot là Z=0, mặt lưới ở Z=-100. Camera căn theo vùng robot, độc lập với kích thước mặt lưới; góc nhìn trực giao giữ mặt lưới phủ kín cả khi nhìn ngang thấp.
- Cụm nút ở góc trên phải xoay trái/phải, nâng/hạ góc nhìn; giữ nút mũi tên để xoay liên tục, nút nhà đặt lại góc nhìn. Kéo chuột để xoay, cuộn để zoom, nhấp đúp cũng đặt lại góc nhìn.
- Vòng nét đứt bán kính 300 mm là mốc tham chiếu tầm với, không phải biên va chạm hay toàn bộ vùng làm việc. Bảng thông số nằm dưới hình 3D, hiển thị X/Y/Z (mm), C (°) trong frame WORLD của controller và J1/J2/J4 (°), J3 (mm). Các số lấy trực tiếp cùng mẫu phản hồi dùng để cập nhật mô hình, không lấy từ vị trí CAD tính toán. Bảng xếp thành hai hàng trên cửa sổ nhỏ; Home thu gọn chiều cao SYSTEM LOG để giữ chỗ cho hình 3D.
- Mô hình dùng trực tiếp 4 solid từ `THL300.igs` trong file `THL300.zip` được cung cấp, giữ hình học và màu vật liệu CAD. Mesh được nhúng vào `.exe`; ứng dụng chạy không cần cài thư viện CAD hay tải model qua mạng.
- Tâm khớp và chiều dài hai tay đòn là 125/175 mm theo CAD và [bản vẽ THL300 của hãng](https://www.shibaura-machine.co.jp/documents/en/product/robot/download/th/pdf_new/S-2GA23-4-THL300-ENG.pdf). Trục J3 tăng theo chiều lên: CAD ở J3=160 mm, nên dịch spindle bằng `J3 − 160`. Gốc hiển thị nằm trên mặt gá robot; gốc controller thấp hơn 48 mm.
- Trước khi nhận phản hồi, các số hiển thị `—`. Khi mất dữ liệu/ngắt kết nối, tư thế và số đo cuối được giữ nguyên, bảng báo `STALE/OFFLINE` và làm mờ số đo cũ. Kết nối lại báo `WAITING` cho tới mẫu mới; trạng thái `LIVE` chỉ xuất hiện khi có phản hồi hợp lệ. Các trạng thái này cũng có trong API `FeedbackState`.
- Hiệu chỉnh dấu/offset encoder dùng profile riêng `%LOCALAPPDATA%\SCARA_UI\thl300-digital-twin.xml`; profile SCARA mẫu cũ không được áp dụng. Kích thước CAD cố định và không bị profile thay đổi. J4 dùng góc khớp cục bộ; `UseWorldToolYaw` là tùy chọn khi cấu hình controller cần lấy hướng từ C.

Khung 3D không gửi lệnh chuyển động và không tự chạy animation khi chưa kết nối. Mô hình đã được kiểm tra bằng dữ liệu phản hồi giả lập; cần đối chiếu lần đầu với robot/controller thực và các frame BASE/TOOL đang sử dụng. Nguồn CAD, hash, quy trình tạo lại mesh và quy ước tọa độ nằm trong `Tools/THL300/README.md`.

## Kiểm tra Digital Twin không cần robot

Build ứng dụng, sau đó chạy `Tests/RunDigitalTwinChecks.ps1` với `-AppPath` trỏ tới `Test_1.exe` đã build, `-CompilerPath` trỏ tới Roslyn `csc.exe` và `-ReferencePath` trỏ tới thư mục reference assemblies của .NET Framework 4.7.2. Bộ kiểm tra đưa vị trí mẫu vào mô hình, kiểm tra 4 trục, phản hồi lỗi/mất kết nối, chuyển trang và layout ở hai kích thước cửa sổ. Bộ kiểm tra không gọi Connect hoặc gửi lệnh tới robot.

## Yêu cầu và Cài đặt
1. Mở project (`Test_1.sln`) bằng Visual Studio.
2. Đảm bảo các thư viện trong thư mục `Libs` được tham chiếu đầy đủ.
3. Build và chạy project.
4. (Tùy chọn) Thay đổi IP, Port mặc định tại file `Constants.cs` nếu cần.

## Tài liệu kỹ thuật
Vui lòng tham khảo file `ARCHITECTURE.md` để nắm rõ kiến trúc chi tiết, sơ đồ thư mục và hướng dẫn sửa lỗi (troubleshooting) cơ bản.
