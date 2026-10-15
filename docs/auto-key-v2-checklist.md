# Auto Lyre — checklist chọn tông v2

Phạm vi lần sửa này: nhận diện giọng giai điệu tự động và chấm điểm dịch tông có trọng số. Không thêm bộ chọn key/track trên giao diện. Đây là bước MVP của audit, không tuyên bố đã giải quyết toàn bộ phối khí.

- [x] Giữ metadata nốt theo track/channel để nhận diện giọng.
- [x] Chấm giọng bằng độ đơn âm, độ liên tục, trường độ và âm vực; xử lý trường hợp thiếu chắc chắn.
- [x] Chuẩn hóa trọng số theo nhóm để phần đệm dày không lấn át giai điệu.
- [x] Giữ v1 làm đối chứng và phương án dự phòng.
- [x] Kiểm thử phản ví dụ audit, track đảo thứ tự, giai điệu thấp hơn phần đệm, dữ liệu một track và đầu vào rỗng.
- [x] Chạy regression WPF và kho MIDI; so sánh v1/v2, không gọi tỷ lệ khớp nốt là độ chính xác nhận diện tông.
- [x] Ghi kết quả và giới hạn để đưa vào commit.

## Thiết kế đã triển khai

Mỗi cặp track/channel là một ứng viên giọng. Điểm ứng viên gồm đơn âm (45%), độ liên tục cao độ (25%), trường độ tương đối (20%) và âm vực (10%); ứng viên dưới 8 nốt bị giảm điểm. Chênh lệch dưới 0,04 giữa hai ứng viên đầu dẫn đến fallback v1. Đây là hằng số heuristic, chưa được hiệu chỉnh bằng tập melody có nhãn; điểm chênh lệch không phải xác suất nhận diện đúng.

Giọng được chọn nhận 80% tổng trọng số, phần còn lại 20%, bất kể số lượng nốt phần đệm. Trong mỗi nhóm, trường độ được điều chỉnh căn bậc hai và giới hạn; velocity chỉ điều chỉnh nhẹ. Vẫn vét cạn 52 mức dịch trên đúng tập phím của mỗi đàn. Không bổ sung lựa chọn thủ công trên UI.

Đầu vào chỉ có một track/channel dùng v1, kể cả khi chứa cả melody và hợp âm. Chưa tự tách các bè trộn trong cùng giọng. Chưa xử lý giọng giai điệu chuyển qua lại giữa nhiều track. Vẫn ánh xạ từng nốt độc lập; chưa tối ưu va chạm hay đường giai điệu bằng quy hoạch động.

## Kết quả ngày 15/10/2026

- **PASS:** build Release ra `artifacts/v2-test`, 0 lỗi; còn cảnh báo dependency/Fody kế thừa.
- **PASS:** bộ fixture và WPF cuối cùng đạt 19 nhóm, 0 lỗi. Kiểm thử gồm phản ví dụ audit, đảo track, melody thấp hơn hợp âm, hai channel trong một track, fallback một giọng/không rõ ràng, lỗi trọng số và hủy xử lý.
- **PASS:** phản ví dụ audit: v1 chọn +1, melody khớp 16/56; v2 tự chọn giọng 56 nốt, chọn 0 và khớp 56/56. Đây là fixture có thời gian và track được dựng thêm từ các dãy cao độ của audit.
- **PASS:** corpus 415 MIDI × 3 loại đàn, 0 lỗi kiểm tra; giữ số nốt hợp lệ, phím hợp lệ và dữ liệu nguồn. Phát hiện ứng viên ở 261 bài, fallback không rõ ràng 85 bài và một giọng 69 bài.
- **NOT TESTED:** nghe A/B trong game, nhãn melody chuẩn, độ chính xác chọn tông theo người nghe.

| Đàn | Khớp giọng được chọn v1 | Khớp giọng được chọn v2 | Khớp toàn bài v1 → v2 |
| --- | ---: | ---: | ---: |
| Cầm Của Gió / Hoa | 76.019/89.918 (84,54%) | 84.751/89.918 (94,25%) | 82,39% → 79,36% |
| Lyre Cũ | 67.873/89.918 (75,48%) | 81.165/89.918 (90,27%) | 76,76% → 72,80% |

Các số trên dùng cùng giọng do v2 tự chọn để đo cả hai phiên bản, không phải giọng giai điệu do người gán nhãn. Tổng nốt mỗi đàn là 319.720; tỷ lệ toàn bài giảm thể hiện đánh đổi phần đệm. Không bài nào trong corpus giảm số nốt khớp của giọng được chọn; đây là quan sát trên corpus, không phải bảo đảm cho mọi MIDI.

Lần kiểm thử đầu có một assertion sai: ép offset 0 cho melody bốn cao độ dù có nhiều mức dịch giữ nguyên toàn bộ giọng. Đã sửa test để kiểm tra điều cần bảo toàn (nhận đúng giọng và đủ nốt khớp), rồi chạy lại toàn bộ fixture/WPF đạt 19/19. Corpus đã đạt ngay lần chạy trước; không đổi thuật toán sau lần đo corpus.

Lệnh tái hiện:

```powershell
dotnet build tests/AutoMidiVerification/AutoMidiVerification.csproj -c Release -p:OutDir=D:/auto-lyre/artifacts/v2-test/
dotnet artifacts/v2-test/AutoMidiVerification.dll --ui
dotnet artifacts/v2-test/AutoMidiVerification.dll --corpus 'D:\KhoMidi' artifacts/auto-midi-v2-benchmark.json
```

Thay đường dẫn kho MIDI bằng thư mục nhạc thực tế. Báo cáo JSON chứa tên tệp/hash được giữ trong `artifacts/` và không commit. Commit lần này gồm mã, test, checklist, README và hai file audit người dùng cung cấp; đích push là `origin/main`.

Các bước tiếp theo chưa thuộc MVP này: ánh xạ chuỗi melody bằng quy hoạch động, tối ưu hợp âm/va chạm, nhận diện chuyển tông từng đoạn. Chưa có dữ liệu melody gán nhãn và đánh giá nghe A/B để xác nhận chất lượng trên nhạc thực tế.
