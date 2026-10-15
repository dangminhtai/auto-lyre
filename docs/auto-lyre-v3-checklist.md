# Auto Lyre V3 — nhạc cụ và kế hoạch phát

Luồng: chọn đàn → chọn MIDI → phát. Một dropdown, không chỉnh key, quãng tám, track hoặc bố cục bàn phím. Dùng các phím PC mặc định của profile.

- [x] Registry 12 profile riêng, kiểu âm thanh/action, cách giữ phím, nguồn và trạng thái xác minh.
- [x] Chặn profile thiếu dữ liệu ở engine và nút Phát; không fallback về C major.
- [x] Tách MIDI âm nhạc khỏi lịch action bàn phím; phím hợp âm không phải cao độ đơn.
- [x] Nhánh đàn giai điệu giữ v2, tối ưu đường giai điệu khi nhận diện được giọng đơn âm.
- [x] Nhánh trống giữ channel 10, phân loại Ka/Don, không dịch tông.
- [x] Nhánh hợp âm/vocal chạy bằng profile đủ dữ liệu; profile thực chưa xác minh vẫn khóa.
- [x] Nhả phím khi dừng, đổi đàn, đổi bài, mất focus; quản lý giữ nốt theo profile.
- [x] UI một dropdown đàn trên màn hình chính, thông báo lý do chưa hỗ trợ.
- [x] Regression, corpus, kiểm tra ảnh UI và ghi kết quả thực tế.

## Bằng chứng và giới hạn ban đầu

Hai tài liệu người dùng cung cấp là bản nghiên cứu. Không nạp trực tiếp JSON provisional làm config thực thi. Ba đàn cũ có mapping tài liệu; Arataki/Festive có tài liệu phím Ka/Don. Bảy profile còn lại thiếu cao độ, phím, cách giữ âm hoặc voicing đã xác minh; không cho phát trước khi có dữ liệu đó.

Đối chiếu web trong lượt triển khai: trang [SoundFont MIDI mapping](https://github.com/StarryCosmosPiano/Genshin-Impact-Instrument-SoundFont/blob/main/docs/MIDI-Mapping.md) xác nhận nguồn là sample zones của SF2, không phải bàn phím trong game. Kết quả tìm kiếm trang [Festive Drum](https://genshin-impact.fandom.com/wiki/Festive_Drum) ghi A/L cho Ka, S/K cho Don. Truy cập trực tiếp trang Arataki bị giới hạn; trang HoYoDex trả 402. Không gọi những profile dựa trên tài liệu là đã đo âm thanh trong game.

Không thể tái tạo màu âm của bộ trống GM bằng hai tiếng Ka/Don. Quy tắc chọn tiếng trống là phép phối gần đúng, tách biệt với xác minh phím A/S. Kiểm tra tai nghe trong game và hiệu chuẩn bảy profile còn lại vẫn cần dữ liệu thực tế.

## Thiết kế được triển khai

- `InstrumentProfile`: ID ổn định, mode, bảng action bất biến, phím PC, tập cao độ hoặc loại tiếng gõ, Tap/Hold, nguồn và lý do khóa. ID action trong lịch phát không phải cao độ phát ra.
- `InstrumentPlan`: chuyển MIDI sang lịch action, giữ tempo/thời điểm, tách nhánh theo mode. Không sửa nguồn. Scheduler sử dụng NoteOn/Off để giữ nhịp nhưng payload chỉ là ID action, không đưa ra loa MIDI.
- Đàn giai điệu: v2 chọn dịch tông, quy hoạch động tối ưu chuỗi đơn âm theo sai khác cao độ/quãng/hướng đi; không cưỡng ép phần đa âm thành một giọng. Nốt có pitch class chơi được vẫn giữ pitch class.
- Hợp âm: chỉ kích hoạt action hợp âm khi tập pitch class của các nốt đồng thời sau dịch tông khớp chính xác. Giữ riêng nốt khác quãng mà voicing hợp âm không phát ra. Đây là bộ nhận hợp âm cơ bản, chưa phải tối ưu toàn cục các thế đảo/hợp âm rải; không sử dụng số trigger của SoundFont làm phím thật.
- Vocal: chọn giọng bằng heuristic, nếu chưa phân biệt được thì chọn nốt cao tại mỗi onset; quy hoạch động theo bảng nốt đã hiệu chuẩn, cắt nốt tại onset tiếp theo để bảo đảm đơn âm. Nhánh này mới kiểm thử bằng profile giả lập; hành vi giữ âm/đơn âm của Vodyanitsa chưa được xác minh.
- Trống: đọc nốt hoàn chỉnh trên channel index 9. GM bass drum/tom thấp và một số tiếng trầm được phối Don, phần còn lại Ka. Không chuyển những nốt có cao độ ở các kênh khác thành trống; file thiếu phần trống bị khóa Phát với giải thích.
- Gộp action trùng phím/cùng onset, cắt chồng phím tại lần đánh lại. Hold gửi KeyUp trước KeyDown khi đánh lại và nhả tất cả lúc dừng. Chưa có số đo tốc độ retrigger tối đa trong game, không tự đặt giới hạn vật lý giả định.
- UI: một dropdown 12 profile. Profile chưa đủ dữ liệu vẫn chọn được để xem lý do, nhưng engine từ chối chuẩn bị và nút Phát bị khóa. Tự tìm tiến trình `GenshinImpact` hoặc `YuanShen`; không yêu cầu đường dẫn cài đặt hoặc chọn layout.

## Trạng thái 12 profile

| Profile | Trạng thái | Cần bổ sung trước khi bật |
| --- | --- | --- |
| Windsong / Floral / Vintage | Đang bật theo tài liệu | Đánh giá âm thanh, độ trễ trong game |
| Arataki / Festive | Đang bật theo tài liệu phím | Đánh giá phối Ka/Don bằng nghe thực tế |
| Nightwind Horn | Khóa | Bảng cao độ thực tế và thử giữ/nhả âm |
| Ukulele / Lingering Euphonia | Khóa | Phím đơn/hợp âm, cao độ và voicing thực tế |
| Leaping Spirit Piano / Harmonic Keys | Khóa | Bảng cao độ, phím và kiểu giữ âm |
| Djem Djem Drum | Khóa | Phím vật lý và tiếng gõ tương ứng |
| Vodyanitsa | Khóa | 14 cao độ/phím, khả năng đa âm và giữ giọng |

## Kiểm tra 15/10/2026

- **PASS — Build:** Release 0.3.0 tại `artifacts/v3-test`, 0 lỗi; còn cảnh báo InputSimulator/Fody kế thừa.
- **PASS — Fixture:** catalog, chặn bảy profile, mapping ba đàn cũ, trống channel 10 và tempo, action hợp âm, giữ melody ở quãng cao, vocal đơn âm, giữ/nhả/đánh lại, thứ tự NoteOff trước NoteOn và contour ở biên âm vực. Profile chord/vocal trong test là dữ liệu tổng hợp, không được đăng ký trong ứng dụng.
- **PASS — Corpus:** 415 MIDI × 5 profile được bật = 2.075 kế hoạch, 900.638 action. Phím/ID hợp lệ, không còn chồng cùng phím sau chuẩn hóa và nguồn MIDI giữ nguyên. Không phải số đo độ giống bản gốc.
- **PASS — WPF:** một dropdown/12 mục, không có bộ chọn key hoặc layout trên màn hình chính; chuyển sang profile khóa hủy playback cũ; trống với bài thiếu kênh 10 bị khóa; chuyển về đàn hỗ trợ chuẩn bị lại được.
- **PASS — Ảnh:** đã xem màn hình sẵn sàng 512×480 và profile Vodyanitsa bị khóa ở 392×700; không chồng lấn, lý do khóa đọc được. Ảnh nằm trong `artifacts/` và không commit.
- **NOT TESTED:** đánh phím/âm thanh trong game, giữ âm thật, độ trễ, nhãn melody chuẩn và nghe A/B. Bảy profile khóa cần dữ liệu hiệu chuẩn, chưa thể coi là hỗ trợ chơi đủ 12 chế độ.

Lỗi phát hiện trong quá trình làm: test contour đi lên thất bại với mức phạt đảo hướng ban đầu; tăng mức phạt đảo hướng vượt chi phí dịch một quãng tám, chạy lại đạt. Lỗi build thiếu `System.IO` ở file test đã được sửa.

Lệnh tái hiện (thay đường dẫn kho nhạc):

```powershell
dotnet build tests/AutoMidiVerification/AutoMidiVerification.csproj -c Release -p:OutDir=D:/auto-lyre/artifacts/v3-test/
dotnet artifacts/v3-test/AutoMidiVerification.dll --profile-corpus 'D:\KhoMidi' --ui
```
