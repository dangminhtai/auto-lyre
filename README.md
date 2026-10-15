# Auto Lyre

Ứng dụng Windows để phát đàn trong Genshin Impact từ bản nhạc MIDI. Chọn bài, nhấn **Phát**; ứng dụng tự điều chỉnh nốt theo loại đàn.

## Sử dụng

1. Mở Genshin Impact và giao diện chơi đàn.
2. Mở Auto Lyre, chọn **Chọn bản nhạc** rồi chọn tệp `.mid` hoặc `.midi`.
3. Trong **Thiết lập đàn**, chọn đúng loại đàn và bố cục bàn phím. Chọn đường dẫn trò chơi nếu ứng dụng chưa tìm thấy.
4. Nhấn **Phát**. **Tạm dừng** giữ vị trí bài; **Dừng** đưa bài về đầu. Chuyển khỏi cửa sổ trò chơi sẽ tạm dừng phát.

Hỗ trợ hiện tại: Đàn Cầm Của Gió, Đàn Hoa, Đàn Lyre Cũ. Không cần chọn tông hoặc nốt gốc. Nốt ngoài khả năng của đàn sẽ được điều chỉnh; không đảm bảo mọi bản MIDI nghe giống nguyên bản.

Thuật toán v2 tự tìm giọng giai điệu theo track/kênh và ưu tiên giọng đó khi chọn cách dịch tông. Khi các ứng viên không rõ ràng hoặc chỉ có một giọng, ứng dụng dùng cách chọn toàn bài của v1. Việc nhận diện có thể sai; xem [checklist và kết quả v2](docs/auto-key-v2-checklist.md).

## Phát triển

Solution: `AutoLyre.sln`. Mã giao diện: `AutoLyre.App`; bộ xử lý MIDI: `AutoLyre.Data`.

Yêu cầu Windows, .NET SDK có hỗ trợ dự án WPF .NET 6 và .NET 6 Desktop Runtime để chạy bản phụ thuộc runtime.

```powershell
dotnet build AutoLyre.sln -c Release
dotnet run --project tests/AutoMidiVerification -c Release --no-build -- --ui
```

Chương trình sau khi build: `AutoLyre.App/bin/Release/net6.0-windows10.0.22621.0/AutoLyre.exe`.

Theo dõi kết quả kiểm tra tại [docs/khoi-tao.md](docs/khoi-tao.md). Dự án đang phát triển từ bản rút gọn hiện có; thông báo nguồn được giữ tại `docs/upstream`. Chưa chọn giấy phép riêng cho phần phát triển mới.
