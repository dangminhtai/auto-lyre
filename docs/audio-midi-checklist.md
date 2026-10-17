# Audio → MIDI

Ngày 17/10/2026. Luồng: chọn WAV/MP3 → Transkun V2 pretrained chạy cục bộ → MIDI thô → engine profile V3 → sẵn sàng nhấn Phát. Không gửi audio lên dịch vụ ngoài và không sửa nguồn. Không tự phát bất ngờ sau một tác vụ nhận dạng dài.

- [x] Nút Audio → MIDI, trạng thái chuyển và Hủy, UI không bị khóa.
- [x] Runtime Python riêng, có luồng cài lần đầu; cố định phiên bản dependency trực tiếp và model.
- [x] Giải mã WAV/MP3, chạy pretrained, ghi MIDI mới và kiểm tra trước khi nạp.
- [x] Hủy process tree, không nạp kết quả cũ khi đổi bài/đóng ứng dụng.
- [x] Kiểm thử tiến trình, xử lý lỗi, hủy và tích hợp UI.
- [x] Chạy transcription thật trên audio thử nghiệm; ghi rõ giới hạn chất lượng.

## Bằng chứng kiểm tra

- PASS: build Release, không lỗi. Còn cảnh báo InputSimulator/Fody có từ trước.
- PASS: chạy `--audio-real artifacts/audio-tests/piano-like.wav --ui`: 26 nhóm qua, 0 lỗi. Checkpoint 2.0 được nạp với `strict=True`, mẫu WAV tạo được 3 action hợp lệ cho Windsong Lyre.
- PASS: chuyển mẫu WAV sang MP3 bằng FFmpeg rồi chạy `--audio-real artifacts/audio-tests/piano-like.mp3 --ui`: 26 nhóm qua, 0 lỗi; tạo được 3 action hợp lệ.
- PASS: kiểm tra truyền nguyên đường dẫn Unicode/ký tự shell, thoát lỗi, hủy tiến trình, đọc đồng thời stdout/stderr; UI tự nạp kết quả, khóa phát khi chuyển, hủy và bỏ qua kết quả tác vụ cũ.
- Mẫu thử do `tests/AutoMidiVerification/make_audio_fixture.py` tạo: 3 giây âm tổng hợp có họa âm C4/E4/G4, không phải bản thu piano thực. Kết quả xác nhận tích hợp, không phải benchmark độ chính xác hoặc sai lệch onset.
- Runtime hiện tại đã cài thành công trong venv riêng bằng uv sau khi pip tải torch bị timeout; ứng dụng sau đó chạy các bước pip/import kiểm tra và tự tạo marker sẵn sàng. Đã tăng timeout pip lên 120 giây.
- NOT TESTED: cài mới hoàn toàn chỉ bằng nút UI trên máy Windows sạch. Python 3.12 là điều kiện tiên quyết; dependency `ncls` đã phải build từ nguồn trên máy này, máy khác có thể cần Microsoft C++ Build Tools. Dependency gián tiếp chưa có lockfile đầy đủ.
- NOT TESTED: bản piano thật dài, chất lượng nghe so với bản gốc, phát bản chuyển đổi trong Genshin đang mở. CPU có thể xử lý lâu; không cam kết nhận đúng mọi nốt.

## Chạy lại

```powershell
python tests/AutoMidiVerification/make_audio_fixture.py
dotnet build tests/AutoMidiVerification/AutoMidiVerification.csproj -c Release -p:OutDir=D:/auto-lyre/artifacts/audio-test/
dotnet artifacts/audio-test/AutoMidiVerification.dll --audio-real artifacts/audio-tests/piano-like.wav --ui
```

Nguồn: [Transkun](https://github.com/Yujia-Yan/Transkun), [gói 2.0.1](https://pypi.org/project/transkun/2.0.1/), [CLI chính thức](https://github.com/Yujia-Yan/Transkun/blob/main/transkun/transcribe.py). Gói 2.0.1 chứa checkpoint `pretrained/2.0.pt`. Transkun là bộ nhận dạng piano; bản thu có giọng hát/nhiều nhạc cụ không có bảo đảm chất lượng.
