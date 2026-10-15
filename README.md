# Auto Lyre

Ứng dụng Windows để phát đàn trong Genshin Impact từ bản nhạc MIDI. Chọn bài, nhấn **Phát**; ứng dụng tự điều chỉnh nốt theo loại đàn.

## Sử dụng

1. Mở Genshin Impact và giao diện chơi đàn.
2. Mở Auto Lyre và chọn đúng **Loại đàn**.
3. Nhấn **Chọn bản nhạc** rồi chọn tệp `.mid` hoặc `.midi`.
4. Nhấn **Phát**. **Tạm dừng** giữ vị trí bài; **Dừng** đưa bài về đầu. Chuyển khỏi cửa sổ trò chơi sẽ tạm dừng phát.

V3 có 12 profile. Đang bật theo mapping tài liệu: Đàn Cầm Của Gió, Đàn Hoa, Đàn Lyre Cũ, Trống Arataki và Trống Lễ Hội. Bảy profile còn lại hiển thị **Chưa hỗ trợ** và khóa phát cho đến khi có bảng phím/âm thanh đủ xác minh. Xem [checklist V3](docs/auto-lyre-v3-checklist.md).

Chương trình tự tìm cửa sổ Genshin Impact đang chạy và dùng phím PC mặc định của profile. Không cần chọn key, quãng tám, track hoặc bàn phím. Nhánh trống đọc kênh MIDI 10 rồi phối sang hai tiếng Ka/Don; bài không có phần trống sẽ được thông báo. Mọi chuyển đổi nằm trong bộ nhớ, không ghi đè MIDI gốc.

Đàn giai điệu giữ cách chọn tông ưu tiên melody của v2 và bổ sung tối ưu chuỗi nốt khi nhận diện được một giọng đơn âm. Phím trùng cùng thời điểm được gộp, nốt cùng phím chồng nhau được cắt ở lần đánh lại. Việc nhận diện có thể sai; không đảm bảo mọi bản MIDI nghe giống nguyên bản. Engine hợp âm và vocal được kiểm thử bằng profile giả lập; Ukulele, Dư Âm và Vodyanitsa thực vẫn khóa vì chưa hiệu chuẩn trong game.

## Phát triển

Solution: `AutoLyre.sln`. Mã giao diện: `AutoLyre.App`; bộ xử lý MIDI: `AutoLyre.Data`.

Yêu cầu Windows, .NET SDK có hỗ trợ dự án WPF .NET 6 và .NET 6 Desktop Runtime để chạy bản phụ thuộc runtime.

```powershell
dotnet build AutoLyre.sln -c Release
dotnet run --project tests/AutoMidiVerification -c Release --no-build -- --ui
```

Chương trình sau khi build: `AutoLyre.App/bin/Release/net6.0-windows10.0.22621.0/AutoLyre.exe`.

Theo dõi kết quả kiểm tra tại [docs/khoi-tao.md](docs/khoi-tao.md). Dự án đang phát triển từ bản rút gọn hiện có; thông báo nguồn được giữ tại `docs/upstream`. Chưa chọn giấy phép riêng cho phần phát triển mới.
