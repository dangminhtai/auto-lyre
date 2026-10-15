# Kiểm tra Auto Lyre

Chạy từ thư mục gốc trên Windows với .NET 6 Desktop Runtime và SDK tương thích.

```powershell
dotnet run --project tests/AutoMidiVerification -- --ui
dotnet run --project tests/AutoMidiVerification -- --corpus 'D:\KhoMidi' artifacts/auto-midi-benchmark.json --ui
```

Thay `D:\KhoMidi` bằng thư mục nhạc thực tế. Không có tham số: chạy các bộ dữ liệu xác định và đối chiếu thuật toán chọn độ dịch nốt với phép vét cạn. `--corpus` kiểm tra đệ quy các tệp `.mid`/`.midi` với mọi loại đàn được hỗ trợ, xuất báo cáo JSON cục bộ. `--ui` tạo điều khiển WPF và mô hình giao diện thực, không gửi phím vào game. Kiểm thử thất bại trả mã thoát 1.

Dữ liệu và ảnh sinh ra nằm trong `artifacts/`, được Git bỏ qua; báo cáo có thể chứa tên tệp cá nhân nên giữ cục bộ. Xem [kết quả kiểm tra](../../docs/khoi-tao.md). Phát đàn và đối chiếu âm thanh trong game cần kiểm thử riêng.
