# Khởi tạo Auto Lyre

Mục tiêu: một màn hình tiếng Việt để chọn bản nhạc MIDI và phát đàn trong Genshin Impact. Giữ bố cục rút gọn hiện có, màu nhấn của hệ thống, nút Phát nổi bật và thiết lập đàn thu gọn.

- [x] Tạo dự án riêng tại `D:\auto-lyre`, không sửa mã của dự án cũ.
- [x] Đổi solution, namespace, tên chương trình và phiên bản phát triển thành Auto Lyre 0.1.0.
- [x] Giữ tự động điều chỉnh nốt, không có bộ chọn C3/D3 hoặc trang Sheet/Playlist.
- [x] Việt hóa giao diện chính, thiết lập đàn, hộp thoại và lỗi trong luồng sử dụng.
- [x] Bỏ cập nhật từ repo cũ; giữ cấu hình ứng dụng mới tách biệt bằng tên assembly mới.
- [x] Giữ thông báo nguồn kế thừa trong `docs/upstream`, chưa chọn giấy phép riêng cho phần phát triển mới.
- [x] Biên dịch Release và chạy kiểm tra tự động.
- [x] Kiểm tra ảnh giao diện có bài và mở thiết lập; đã xuất thêm ảnh trạng thái rỗng.
- [ ] Kiểm thử thao tác phát đàn trực tiếp trong Genshin Impact.

Đây là dự án kế thừa bản rút gọn, không phải bản viết lại độc lập. Các lớp nội bộ cho chức năng cũ vẫn còn để giữ tương thích với bộ xử lý và kiểm thử; giao diện không cung cấp các chức năng đó.

Tên Auto Lyre, Genshin Impact, MIDI và các chuẩn bàn phím được giữ nguyên. Hộp chọn tệp dùng thành phần Windows nên các nhãn hệ điều hành phụ thuộc ngôn ngữ Windows.

## Kết quả kiểm tra ngày 15/10/2026

- **PASS — Build:** `dotnet build AutoLyre.sln -c Release --nologo -v quiet`: 0 lỗi, 8 cảnh báo từ InputSimulator và Fody kế thừa.
- **PASS — Kiểm thử:** `dotnet run --project tests/AutoMidiVerification -c Release --no-build -- --ui`: 15 nhóm đạt, 0 nhóm lỗi. Bao gồm ánh xạ nốt, bảo toàn MIDI gốc, hủy xử lý, thay bài, dừng về đầu, lỗi mở tệp và tích hợp WPF.
- **PASS — Rà ảnh:** đã xem `artifacts/simple-player-ready.png` (512 × 480) và `artifacts/simple-player-settings.png` (392 × 700). Các điều khiển hiển thị đầy đủ, không chồng lấn; tên đàn và nút chọn bài đã Việt hóa.
- **NOT TESTED — Chơi đàn trong game:** chưa đo/đối chiếu âm thanh hoặc gửi phím thực tế trong Genshin Impact ở lần tách dự án này. Bộ kiểm thử UI không gửi phím vào trò chơi.
- **NOT TESTED — Hộp chọn tệp Windows:** tiêu đề và bộ lọc được Việt hóa trong mã; chưa thao tác thủ công hộp thoại gốc của hệ điều hành.

Git chỉ được quản lý cục bộ trong `D:\auto-lyre`; chưa thêm remote hoặc đẩy mã lên mạng. Không sao chép thư mục build, dữ liệu cấu hình cá nhân, kho MIDI hoặc lịch sử Git của dự án cũ.
