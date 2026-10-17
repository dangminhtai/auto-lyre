using System;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using WindowsInput.Native;
using System.Threading.Tasks;
using System.Windows;
using Windows.Media;
using Windows.Media.Playback;
using AutoLyre.Data.Entities;
using AutoLyre.Data.Midi;
using AutoLyre.Data.Notification;
using AutoLyre.Data.Properties;
using AutoLyre.App.Core;
using AutoLyre.App.Audio;
using AutoLyre.App.ModernWPF.Errors;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using Melanchall.DryWetMidi.Tools;
using ModernWpf.Controls;
using Stylet;
using StyletIoC;
using MidiFile = AutoLyre.Data.Midi.MidiFile;

namespace AutoLyre.App.ViewModels;

public class LyrePlayerViewModel : Screen,
    IHandle<MidiFile>, IHandle<MidiTrack>,
    IHandle<SettingsPageViewModel>,
    IHandle<MergeNotesNotification>,
    IHandle<PlayTimerNotification>
{
    private static readonly Settings Settings = Settings.Default;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;
    private readonly MediaPlayer? _player;
    private readonly OutputDevice? _speakers;
    private readonly PlaybackCurrentTimeWatcher _timeWatcher;
    private bool _ignoreSliderChange;
    private InputDevice? _inputDevice;
    private TimeSpan _songPosition;
    private readonly object _outputLock = new();
    private readonly HeldNotes<VirtualKeyCode> _heldKeys = new();
    private readonly HeldNotes<(int Channel, int Note)> _heldSpeakers = new();
    private CancellationTokenSource? _analysisCancellation;
    private Task _preparation = Task.CompletedTask;
    private int _generation;
    private bool _isShuttingDown;
    private readonly DispatcherTimer _focusTimer;
    private CancellationTokenSource? _audioCancellation;
    public IAudioTranscriber AudioConverter { get; set; } = new AudioTranscriber();
    public bool IsConvertingAudio { get; private set; }
    public bool CanChooseFile => !IsConvertingAudio;
    public string AudioStatus { get; private set; } = "";
    public string? ConvertedMidiPath { get; private set; }
    public bool CanSaveConvertedMidi => !IsConvertingAudio && ConvertedMidiPath is not null;
    private readonly InstrumentOutput _actionOutput = new(
        key => LyrePlayer.PressKey((VirtualKeyCode)key),
        key => LyrePlayer.ReleaseKey((VirtualKeyCode)key),
        key => LyrePlayer.TapKey((VirtualKeyCode)key));

    public AutoArrangement? Arrangement { get; private set; }
    public InstrumentPlan? Plan { get; private set; }
    public bool IsAnalyzing { get; private set; }
    public string AnalysisStatus { get; private set; } = "Chọn bản nhạc .mid để bắt đầu";

    public LyrePlayerViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _main = main;
        _timeWatcher = PlaybackCurrentTimeWatcher.Instance;

        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);

        _focusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _focusTimer.Tick += (_, _) =>
        {
            lock (_outputLock)
                if (_heldKeys.Count == 0 && (Settings.UseSpeakers || Playback?.IsRunning != true)) return;
            if (!WindowHelper.IsGameFocused())
            {
                lock (_outputLock)
                    foreach (var key in _heldKeys.Drain()) LyrePlayer.ReleaseKey(key);
                if (!Settings.UseSpeakers && Playback?.IsRunning == true)
                {
                    Playback.Stop();
                    AnalysisStatus = "Đã tạm dừng — chuyển lại Genshin để tiếp tục.";
                    UpdateButtons();
                }
            }
        };
        _focusTimer.Start();
        Application.Current.Exit += (_, _) => Shutdown();
        SelectedMidiInput = MidiInputs[0];

        _timeWatcher.CurrentTimeChanged += OnSongTick;

        // SystemMediaTransportControls is only supported on Windows 10 and later
        // https://docs.microsoft.com/en-us/uwp/api/windows.media.systemmediatransportcontrols
        if (Environment.OSVersion.Platform == PlatformID.Win32NT &&
            Environment.OSVersion.Version.Major >= 10)
        {
            _player = ioc.Get<MediaPlayer>();

            _player!.CommandManager.PlayReceived += async (_, _) => await PlayPause();
            _player!.CommandManager.PauseReceived += async (_, _) => await PlayPause();
        }

        try
        {
            if (Settings.UseSpeakers)
                _speakers = OutputDevice.GetByName("Microsoft GS Wavetable Synth");
        }
        catch (ArgumentException e)
        {
            new ErrorContentDialog(e, closeText: "Bỏ qua").ShowAsync();

            SettingsPage.CanUseSpeakers = false;
            SettingsPage.UseSpeakers = false;
        }
    }

    public BindableCollection<MidiInput> MidiInputs { get; } = new()
    {
        new("None")
    };

    public BindableCollection<MidiTrack> MidiTracks { get; } = new();

    public bool CanHitNext
    {
        get
        {
            if (Playlist.Loop is not PlaylistViewModel.LoopMode.Playlist)
                return true;

            var last = Playlist.GetPlaylist().LastOrDefault();
            return Playlist.OpenedFile != last;
        }
    }

    public bool CanHitPlayPause
    {
        get
        {
            var hasNotes = MidiTracks
                .Where(t => t.IsChecked)
                .Any(t => t.CanBePlayed);

            return !IsConvertingAudio && !IsAnalyzing && SettingsPage.SelectedProfile.CanPlay && Plan?.Actions.Count > 0 && Playback is not null
                && hasNotes
                && MaximumTime > TimeSpan.Zero;
        }
    }

    public bool CanHitPrevious => CurrentTime > TimeSpan.FromSeconds(3) || Playlist.History.Count > 1;

    public double SongPosition
    {
        get => _songPosition.TotalSeconds;
        set => SetAndNotify(ref _songPosition, TimeSpan.FromSeconds(value));
    }

    public MidiInput? SelectedMidiInput { get; set; }

    public Playback? Playback { get; private set; }

    public PlaylistViewModel Playlist => _main.PlaylistView;

    public string PlayPauseIcon => Playback?.IsRunning ?? false ? PauseIcon : PlayIcon;

    public string PlayPauseText => Playback?.IsRunning == true ? "Tạm dừng" : "Phát";

    public TimeSpan CurrentTime => _songPosition;

    public TimeSpan MaximumTime => Playlist.OpenedFile?.Duration ?? TimeSpan.Zero;

    private MusicDisplayProperties? Display =>
        _player?.SystemMediaTransportControls.DisplayUpdater.MusicProperties;

    public SettingsPageViewModel SettingsPage => _main.SettingsView;

    private static string PauseIcon => "\xEDB4";

    private static string PlayIcon => "\xF5B0";

    private SystemMediaTransportControls? Controls =>
        _player?.SystemMediaTransportControls;

    public async void Handle(MergeNotesNotification message) => await InitializePlayback();

    public async void Handle(MidiFile file) => await LoadFile(file);

    private async Task LoadFile(MidiFile file)
    {
        CloseFile();
        Playlist.OpenedFile = file;
        Playlist.Tracks.Clear();
        Playlist.Tracks.Add(file);
        Playlist.History.Clear();
        Playlist.History.Push(file);
        InitializeTracks();
        await InitializePlayback();
    }

    public async void Handle(MidiTrack track) => await InitializePlayback();

    public async void Handle(PlayTimerNotification message)
    {
        await _preparation;
        if (!(Playback?.IsRunning ?? false) && CanHitPlayPause) await PlayPause();
    }

    public async void Handle(SettingsPageViewModel message) => await InitializePlayback();

    public async Task OpenFile()
    {
        if (IsConvertingAudio) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn bài MIDI",
            Filter = "Bản nhạc MIDI (*.mid;*.midi)|*.mid;*.midi",
            Multiselect = false
        };
        if (dialog.ShowDialog() == true) await OpenMidi(dialog.FileName);
    }

    public async Task OpenMidi(string path)
    {
        CancelAudio();
        ConvertedMidiPath = null;
        await LoadMidiPath(path);
    }

    private async Task LoadMidiPath(string path)
    {
        CloseFile();
        try { await LoadFile(new MidiFile(new History(path, 0))); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
            AnalysisStatus = "Không mở được bản nhạc. Kiểm tra tệp còn tồn tại và có định dạng MIDI hợp lệ.";
        }
    }

    public async Task OpenAudio()
    {
        if (IsConvertingAudio) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn bản thu piano",
            Filter = "Âm thanh WAV hoặc MP3 (*.wav;*.mp3)|*.wav;*.mp3",
            Multiselect = false
        };
        if (dialog.ShowDialog() == true) await ConvertAudio(dialog.FileName);
    }

    public async Task ConvertAudio(string path)
    {
        if (IsConvertingAudio || _isShuttingDown) return;
        Stop();
        using var cancellation = new CancellationTokenSource();
        _audioCancellation = cancellation;
        IsConvertingAudio = true;
        ConvertedMidiPath = null;
        AudioStatus = "Đang chuẩn bị chuyển âm thanh…";
        AnalysisStatus = "Chờ chuyển âm thanh hoàn tất.";
        UpdateButtons();
        var progress = new Progress<string>(message =>
        {
            if (_audioCancellation == cancellation && !cancellation.IsCancellationRequested) AudioStatus = message;
        });
        try
        {
            var midiPath = await AudioConverter.ConvertAsync(path, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_isShuttingDown) return;
            // Loading the new file closes the previous player; the conversion has already ended.
            _audioCancellation = null;
            await LoadMidiPath(midiPath);
            ConvertedMidiPath = midiPath;
            AudioStatus = Plan?.Actions.Count > 0 ? "Đã chuyển và nạp bản nhạc. Nhấn Phát để chơi đàn." : "Đã chuyển sang MIDI.";
        }
        catch (OperationCanceledException)
        {
            AudioStatus = "Đã hủy chuyển âm thanh.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
            AudioStatus = ex is InvalidOperationException or System.IO.FileNotFoundException
                ? ex.Message : "Không chuyển được bản thu. Hãy thử một tệp WAV hoặc MP3 khác.";
        }
        finally
        {
            if (_audioCancellation == cancellation) _audioCancellation = null;
            IsConvertingAudio = false;
            if (AnalysisStatus == "Chờ chuyển âm thanh hoàn tất.")
                AnalysisStatus = Plan?.Actions.Count > 0 ? "Sẵn sàng" : "Chọn bản nhạc .mid để bắt đầu";
            UpdateButtons();
        }
    }

    public void CancelAudio() => _audioCancellation?.Cancel();

    public void SaveConvertedMidi()
    {
        if (!CanSaveConvertedMidi) return;
        var source = ConvertedMidiPath!;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Lưu bản MIDI đã chuyển",
            Filter = "Bản nhạc MIDI (*.mid)|*.mid",
            DefaultExt = ".mid", AddExtension = true, OverwritePrompt = true,
            FileName = System.IO.Path.GetFileName(source)
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (!string.Equals(System.IO.Path.GetFullPath(source), System.IO.Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                System.IO.File.Copy(source, dialog.FileName, overwrite: true);
            AudioStatus = "Đã lưu MIDI: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
            AudioStatus = "Không lưu được MIDI. Hãy kiểm tra quyền ghi hoặc chọn thư mục khác.";
        }
    }

    public void Stop()
    {
        Playback?.Stop();
        ReleaseOutputs();
        Playback?.MoveToStart();
        MoveSlider(TimeSpan.Zero);
        if (Plan?.Actions.Count > 0) AnalysisStatus = "Sẵn sàng";
        UpdateButtons();
    }

    public async Task PlayPause()
    {
        await _preparation;
        if (!CanHitPlayPause || Playback is null) return;
        var playback = Playback;
        if (playback.IsRunning)
        {
            Playback.Stop();
            AnalysisStatus = "Đã tạm dừng";
        }
        else
        {
            var time = new MetricTimeSpan(CurrentTime);
            Playback.PlaybackStart = time;
            Playback.MoveToTime(time);

            if (Settings.UseSpeakers)
                Playback.Start();
            else
            {
                WindowHelper.EnsureGameOnTop();
                await Task.Delay(100);

                if (Playback != playback || IsAnalyzing) return;
                if (WindowHelper.IsGameFocused())
                {
                    Playback.PlaybackStart = Playback.GetCurrentTime(TimeSpanType.Midi);
                    Playback.Start();
                }
                else AnalysisStatus = "Mở Genshin và trang bị đàn trước khi phát.";
            }
        }
    }

    private void DisposePlayback()
    {
        Playback? playback;
        lock (_outputLock)
        {
            playback = Playback;
            Playback = null;
        }
        if (playback is not null)
        {
            _timeWatcher.RemovePlayback(playback);
            playback.Stop();
            playback.Dispose();
        }
        ReleaseOutputs();
    }

    public void CloseFile()
    {
        CancelAudio();
        _generation++;
        _analysisCancellation?.Cancel();
        _analysisCancellation = null;
        DisposePlayback();
        Arrangement = null;
        Plan = null;
        IsAnalyzing = false;
        AnalysisStatus = "Chọn bản nhạc .mid để bắt đầu";
        MidiTracks.Clear();
        MoveSlider(TimeSpan.Zero);
        Playlist.OpenedFile = null;
        _main.PianoSheetView.Clear();
        UpdateButtons();
    }

    public void Shutdown()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        _focusTimer.Stop();
        _inputDevice?.Dispose();
        _inputDevice = null;
        _timeWatcher.CurrentTimeChanged -= OnSongTick;
        CloseFile();
    }

    public async void Next()
    {
        var next = Playlist.Next();
        if (next is null)
        {
            if (Playback is not null)
            {
                Playback.PlaybackStart = null;
                Playback.MoveToStart();
            }

            MoveSlider(TimeSpan.Zero);
            return;
        }

        await LoadFile(next);
        if (Playlist.OpenedFile == next && CanHitPlayPause)
            await PlayPause();
    }

    public void OnSelectedMidiInputChanged()
    {
        _inputDevice?.Dispose();
        _inputDevice = null;
        ReleaseOutputs();

        if (SelectedMidiInput?.DeviceName is not null
            && SelectedMidiInput.DeviceName != "None")
        {
            _inputDevice = InputDevice.GetByName(SelectedMidiInput.DeviceName);

            _inputDevice!.EventReceived += OnNoteEvent;
            _inputDevice!.StartEventsListening();
        }
    }

    public void OnSongPositionChanged()
    {
        NotifyOfPropertyChange(() => CurrentTime);


        if (!_ignoreSliderChange && Playback is not null)
        {
            var isRunning = Playback.IsRunning;
            Playback.Stop();
            Playback.MoveToTime(new MetricTimeSpan(_songPosition));

            if (Settings.UseSpeakers && isRunning)
                Playback.Start();
        }

        _ignoreSliderChange = false;
    }

    public void OnSongTick(object? sender, PlaybackCurrentTimeChangedEventArgs e)
    {
        var playback = Playback;
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Playback != playback || playback is null || !playback.IsRunning) return;
            MoveSlider((MetricTimeSpan)playback.GetCurrentTime(TimeSpanType.Metric));
            UpdateButtons();
        }));
    }

    public void Previous()
    {
        if (CurrentTime > TimeSpan.FromSeconds(3))
        {
            Playback?.Stop();
            Playback?.MoveToStart();

            MoveSlider(TimeSpan.Zero);
            Playback?.Start();
        }
        else if (Playlist.History.Count > 1)
            Playlist.Previous();
    }

    public void RefreshDevices()
    {
        MidiInputs.Clear();
        MidiInputs.Add(new("None"));

        foreach (var device in InputDevice.GetAll())
        {
            MidiInputs.Add(new(device.Name));
        }

        SelectedMidiInput = MidiInputs[0];
    }

    public void UpdateButtons()
    {
        NotifyOfPropertyChange(() => CanHitNext);
        NotifyOfPropertyChange(() => CanHitPrevious);
        NotifyOfPropertyChange(() => CanHitPlayPause);

        NotifyOfPropertyChange(() => PlayPauseIcon);
        NotifyOfPropertyChange(() => PlayPauseText);
        NotifyOfPropertyChange(() => MaximumTime);
        NotifyOfPropertyChange(() => CurrentTime);

        if (Controls is not null && Display is not null)
        {
            Controls.IsPlayEnabled = CanHitPlayPause;
            Controls.IsPauseEnabled = CanHitPlayPause;

            Controls.IsNextEnabled = false;
            Controls.IsPreviousEnabled = false;

            Controls.PlaybackStatus =
                Playlist.OpenedFile is null ? MediaPlaybackStatus.Closed :
                Playback is null ? MediaPlaybackStatus.Stopped :
                Playback.IsRunning ? MediaPlaybackStatus.Playing :
                MediaPlaybackStatus.Paused;

            var file = Playlist.OpenedFile;
            if (file is not null)
            {
                var position = $"{file.Position}/{Playlist.GetPlaylist().Count}";

                Display.Title = file.Title;
                Display.Artist = $"Auto Lyre · {CurrentTime:mm\\:ss}";
            }

            Controls.DisplayUpdater.Update();
        }
    }

    private Task InitializePlayback() => _preparation = PreparePlayback();

    private async Task PreparePlayback()
    {
        var generation = ++_generation;
        _analysisCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        DisposePlayback();
        Arrangement = null;
        Plan = null;
        _main.PianoSheetView.Clear();
        var file = Playlist.OpenedFile;
        var profile = SettingsPage.SelectedProfile;
        if (file is null || !profile.CanPlay)
        {
            AnalysisStatus = profile.BlockedReason ?? "Chọn bản nhạc .mid để bắt đầu";
            IsAnalyzing = false;
            cancellation.Dispose();
            _analysisCancellation = null;
            UpdateButtons();
            return;
        }
        IsAnalyzing = true;
        AnalysisStatus = "Đang chuẩn bị bài…";
        UpdateButtons();
        var selected = MidiTracks.Select(t => t.IsChecked).ToArray();
        try
        {
            var plan = await Task.Run(() => InstrumentPlan.Create(file.Midi, selected,
                profile, cancellation.Token), cancellation.Token);
            if (generation != _generation || file != Playlist.OpenedFile) return;
            Plan = plan;
            Arrangement = plan.Arrangement;
            AnalysisStatus = plan.Actions.Count > 0 ? "Sẵn sàng" : profile.Mode == InstrumentMode.Percussion
                ? "Bản MIDI này không có phần trống trên kênh 10." : "Bài này không có nốt đàn để phát";
            if (plan.Actions.Count == 0) return;
            var playback = plan.Transport.GetPlayback();
            Playback = playback;
            playback.Speed = SettingsPage.SelectedSpeed.Speed;
            playback.InterruptNotesOnStop = true;
            playback.EventPlayed += (_, e) =>
            {
                lock (_outputLock)
                    if (Playback == playback && e.Event is NoteEvent note)
                    {
                        var action = profile.Actions.FirstOrDefault(a => a.Id == note.NoteNumber);
                        if (action is null) return;
                        var down = note is NoteOnEvent && note.Velocity > 0;
                        if (down && !WindowHelper.IsGameFocused())
                        {
                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (Playback == playback) { playback.Stop(); AnalysisStatus = "Đã tạm dừng — chuyển lại Genshin để tiếp tục."; }
                            }));
                            return;
                        }
                        _actionOutput.Send(profile, action, down);
                    }
            };
            playback.Finished += (_, _) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Playback == playback) { Stop(); AnalysisStatus = "Đã phát xong"; }
            }));
            playback.Started += (_, _) =>
            {
                _timeWatcher.RemoveAllPlaybacks();
                _timeWatcher.AddPlayback(playback, TimeSpanType.Metric);
                _timeWatcher.Start();
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Playback == playback && playback.IsRunning) AnalysisStatus = "Đang phát";
                    UpdateButtons();
                }));
            };
            playback.Stopped += (_, _) =>
            {
                _timeWatcher.Stop();
                ReleaseOutputs();
                Application.Current.Dispatcher.BeginInvoke(new Action(UpdateButtons));
            };
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                Arrangement = null;
                Plan = null;
                System.Diagnostics.Trace.TraceError(ex.ToString());
                AnalysisStatus = "Không xử lý được bản nhạc. Hãy chọn một tệp MIDI khác.";
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsAnalyzing = false;
                _analysisCancellation = null;
                UpdateButtons();
            }
            cancellation.Dispose();
        }
    }

    private void InitializeTracks()
    {
        if (Playlist.OpenedFile?.Midi is null)
            return;

        MidiTracks.Clear();
        MidiTracks.AddRange(Playlist.OpenedFile
            .Midi.GetTrackChunks()
            .Select(t => new MidiTrack(_events, t)));
    }

    private void MoveSlider(TimeSpan value)
    {
        _ignoreSliderChange = true;
        try { SongPosition = value.TotalSeconds; }
        finally { _ignoreSliderChange = false; }
    }

    private void OnNoteEvent(object? sender, MidiEventReceivedEventArgs e)
    {
        // The single-file player accepts only planned actions; legacy live MIDI input is disabled.
    }

    private void ReleaseOutputs()
    {
        lock (_outputLock)
        {
            _actionOutput.ReleaseAll();
            foreach (var key in _heldKeys.Drain()) LyrePlayer.ReleaseKey(key);
            foreach (var key in _heldSpeakers.Drain())
                _speakers?.SendEvent(new NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)key.Note,
                    (Melanchall.DryWetMidi.Common.SevenBitNumber)0)
                { Channel = (Melanchall.DryWetMidi.Common.FourBitNumber)key.Channel });
        }
    }

    private void PlayNote(NoteEvent note, Keyboard.Layout layout, Keyboard.Instrument instrument,
        bool speakers, bool hold)
    {
        var down = note is NoteOnEvent && note.Velocity > 0;
        lock (_outputLock)
        {
            if (speakers)
            {
                var key = ((int)note.Channel, (int)note.NoteNumber);
                if (down ? _heldSpeakers.Press(key) : _heldSpeakers.Release(key))
                    _speakers?.SendEvent(down ? note.Clone() : new NoteOffEvent(note.NoteNumber,
                        (Melanchall.DryWetMidi.Common.SevenBitNumber)0)
                    { Channel = note.Channel });
                return;
            }
            if (!LyrePlayer.TryGetKey(layout, instrument, note.NoteNumber, out var physicalKey)) return;
            if (!down)
            {
                if (_heldKeys.Release(physicalKey)) LyrePlayer.ReleaseKey(physicalKey);
                return;
            }
            if (!WindowHelper.IsGameFocused())
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => Playback?.Stop()));
                return;
            }
            if (hold)
            {
                if (_heldKeys.Press(physicalKey)) LyrePlayer.PressKey(physicalKey);
            }
            else LyrePlayer.PlayNote(note.NoteNumber, layout, instrument);
        }
    }
}
