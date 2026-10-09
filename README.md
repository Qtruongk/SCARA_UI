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

## Chức năng chính
- Kết nối với Robot Controller qua giao thức TCP.
- Quản lý và theo dõi trạng thái hệ thống (Kết nối, Servo, Cảnh báo).
- Hỗ trợ tính toán và thực thi quỹ đạo chuyển động (S-Curve trajectory).
- Hệ thống ghi log tập trung.

## Yêu cầu và Cài đặt
1. Mở project (`Test_1.sln`) bằng Visual Studio.
2. Đảm bảo các thư viện trong thư mục `Libs` được tham chiếu đầy đủ.
3. Build và chạy project.
4. (Tùy chọn) Thay đổi IP, Port mặc định tại file `Constants.cs` nếu cần.

## Tài liệu kỹ thuật
Vui lòng tham khảo file `ARCHITECTURE.md` để nắm rõ kiến trúc chi tiết, sơ đồ thư mục và hướng dẫn sửa lỗi (troubleshooting) cơ bản.
