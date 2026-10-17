using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Melanchall.DryWetMidi.Interaction;

namespace AutoLyre.App.Audio;

public interface IAudioTranscriber
{
    Task<string> ConvertAsync(string source, IProgress<string> progress, CancellationToken cancellationToken);
}

public sealed class AudioTranscriber : IAudioTranscriber
{
    private static readonly SemaphoreSlim RuntimeLock = new(1, 1);
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoLyre", "Audio");
    private static readonly string Runtime = Path.Combine(Root, "runtime-v1");
    private static readonly string Python = Path.Combine(Runtime, "Scripts", "python.exe");
    private static string Assets => Path.Combine(AppContext.BaseDirectory, "Audio");

    public async Task<string> ConvertAsync(string source, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Không tìm thấy tệp âm thanh.", source);
        if (!new[] { ".wav", ".mp3" }.Contains(Path.GetExtension(source).ToLowerInvariant()))
            throw new InvalidOperationException("Hãy chọn bản thu WAV hoặc MP3.");
        var job = Path.Combine(Root, "BaiDaChuyen", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        var output = Path.Combine(job, Path.GetFileNameWithoutExtension(source) + ".mid");
        try
        {
            await EnsureRuntime(progress, cancellationToken);
            progress.Report("Đang khởi động bộ nhận dạng piano…");
            await RunProcessAsync(Python, new[] { "-u", Path.Combine(Assets, "transcribe_audio.py"), Path.GetFullPath(source), output },
                Path.Combine(job, "conversion.log"), cancellationToken, line =>
                {
                    try
                    {
                        using var json = JsonDocument.Parse(line);
                        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("stage", out var value)
                            || value.ValueKind != JsonValueKind.String) return;
                        var stage = value.GetString();
                        progress.Report(stage switch
                        {
                            "loading" => "Đang nạp mô hình piano…",
                            "decoding" => "Đang đọc âm thanh…",
                            "transcribing" => "Đang nhận dạng nốt piano — có thể mất vài phút…",
                            "writing" => "Đang tạo bản MIDI…",
                            _ => "Đang hoàn tất bản MIDI…"
                        });
                    }
                    catch (JsonException) { }
                });
            cancellationToken.ThrowIfCancellationRequested();
            var midi = Melanchall.DryWetMidi.Core.MidiFile.Read(output);
            if (midi.GetNotes().Count == 0) throw new InvalidOperationException("Không nhận dạng được nốt piano trong bản thu này.");
            return output;
        }
        catch
        {
            // Only task-owned outputs are removed. Keep the local log for diagnosis.
            if (File.Exists(output)) File.Delete(output);
            var partial = Path.ChangeExtension(output, ".partial.mid");
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }

    private static async Task EnsureRuntime(IProgress<string> progress, CancellationToken token)
    {
        await RuntimeLock.WaitAsync(token);
        try
        {
            var marker = Path.Combine(Runtime, "ready-v1");
            if (File.Exists(marker) && File.Exists(Python)) return;
            progress.Report("Lần đầu: đang cài bộ chuyển nhạc, cần kết nối mạng…");
            Directory.CreateDirectory(Root);
            var log = Path.Combine(Root, "setup.log");
            if (!File.Exists(Python))
            {
                // Use an installed, supported Python; never alter global packages.
                Exception? last = null;
                foreach (var candidate in new[] { ("py", new[] { "-3.12", "-m", "venv", Runtime }), ("python", new[] { "-m", "venv", Runtime }) })
                {
                    try { await RunProcessAsync(candidate.Item1, candidate.Item2, log, token); last = null; break; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { last = ex; }
                }
                if (last is not null) throw new InvalidOperationException("Cần cài Python 3.12 để chuyển audio thành MIDI.", last);
            }
            await RunProcessAsync(Python, new[] { "-m", "pip", "install", "--disable-pip-version-check", "torch==2.5.1", "torchaudio==2.5.1", "--index-url", "https://download.pytorch.org/whl/cpu" }, log, token);
            await RunProcessAsync(Python, new[] { "-m", "pip", "install", "--disable-pip-version-check", "-r", Path.Combine(Assets, "requirements.txt") }, log, token);
            await RunProcessAsync(Python, new[] { "-c", "import torch, transkun, imageio_ffmpeg, moduleconf; from transkun import ModelTransformer" }, log, token);
            await File.WriteAllTextAsync(marker, "transkun 2.0.1 / torch 2.5.1 CPU", token);
        }
        finally { RuntimeLock.Release(); }
    }

    public static async Task RunProcessAsync(string executable, string[] arguments, string logPath,
        CancellationToken token, Action<string>? output = null)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONUNBUFFERED"] = "1";
        start.Environment["PIP_DEFAULT_TIMEOUT"] = "120";
        using var process = new Process { StartInfo = start };
        using var log = new StreamWriter(logPath, append: true, Encoding.UTF8) { AutoFlush = true };
        var sync = new object();
        process.Start();
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        async Task Drain(StreamReader reader, bool report)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (sync) log.WriteLine(line);
                if (report) output?.Invoke(line);
            }
        }
        await Task.WhenAll(Drain(process.StandardOutput, true), Drain(process.StandardError, false), process.WaitForExitAsync());
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Không hoàn tất chuyển nhạc. Kiểm tra tệp âm thanh hoặc kết nối mạng nếu đang cài lần đầu. Nhật ký: " + logPath);
    }
}
