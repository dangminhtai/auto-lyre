using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoLyre.Data.Entities;
using AutoLyre.App.Core;
using AutoLyre.App.ViewModels;
using AutoLyre.App.Views;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using ModernWpf.Controls;
using Stylet;
using StyletIoC;
using Wpf.Ui.Mvvm.Contracts;
using Wpf.Ui.Mvvm.Services;
using FileModel = AutoLyre.Data.Midi.MidiFile;
using AutoLyre.App.Audio;

internal static class UiVerification
{
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        app.Resources.MergedDictionaries.Add(new ModernWpf.ThemeResources());
        app.Resources.MergedDictionaries.Add(new ModernWpf.Controls.XamlControlsResources());
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ThemeType.Light });
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Resources[Stylet.Xaml.View.ViewManagerResourceKey] = new ViewManager(new ViewManagerConfig
        {
            ViewFactory = type => (UIElement)Activator.CreateInstance(type)!,
            ViewAssemblies = new() { typeof(LyrePlayerView).Assembly }
        });
        var builder = new StyletIoCBuilder();
        builder.Bind<IEventAggregator>().ToInstance(new EventAggregator());
        builder.Bind<IThemeService>().To<ThemeService>().InSingletonScope();
        builder.Bind<Windows.Media.Playback.MediaPlayer>().ToFactory(_ =>
        {
            var media = new Windows.Media.Playback.MediaPlayer();
            media.SystemMediaTransportControls.DisplayUpdater.Type = Windows.Media.MediaPlaybackType.Music;
            return media;
        }).InSingletonScope();
        using var container = builder.BuildContainer();
        var main = new MainWindowViewModel(container, container.Get<IThemeService>());
        try
        {
            main.SettingsView.SelectedInstrument = Keyboard.InstrumentNames.First();
            main.SettingsView.SelectedProfile = AutoLyre.Data.Midi.InstrumentProfiles.All[0];
            main.SettingsView.SelectedLayout = Keyboard.LayoutNames.First();
            main.SettingsView.UseSpeakers = false;
            main.SettingsView.HoldNotes = false;
            main.SettingsView.MergeNotes = false;
            var settings = new SettingsPageView();
            settings.InitializeComponent();
            settings.DataContext = main.SettingsView;
            settings.Measure(new Size(900, 900));
            settings.Arrange(new Rect(0, 0, 900, 900));
            var elements = Descendants(settings).ToArray();
            // Legacy settings view is not hosted by the V3 main window.
            Program.Check(!elements.OfType<ToggleSwitch>().Any(), "Hidden feature switches are still exposed");
            Program.Check(!elements.OfType<NumberBox>().Any(n => n.GetBindingExpression(NumberBox.ValueProperty)?.ParentBinding.Path.Path == "KeyOffset"));
            Program.Check(!elements.OfType<GroupBox>().Any(n => n.Header?.ToString() == "Transpose"));
            Program.Check(!elements.OfType<ToggleSwitch>().Any(n => n.Header?.ToString() == "Transpose"));
            Program.Check(typeof(SettingsPageViewModel).GetProperty("KeyOffset") is null);

            var fixtureDirectory = Path.GetFullPath("artifacts/auto-midi-fixtures");
            Directory.CreateDirectory(fixtureDirectory);
            var path = Path.Combine(fixtureDirectory, "ui-scale.mid");
            var notes = Keyboard.GetNotes(Keyboard.Instrument.WindsongLyre)
                .Select((n, i) => new Note((SevenBitNumber)(n + 2), i == 0 ? 3000 : 120, i * 240)).ToArray();
            new MidiFile(notes.ToTrackChunk()).Write(path, overwriteFile: true);
            var history = new History(path, -27) { Transpose = Transpose.Up };
            var file = new FileModel(history);
            main.PlayerView.Handle(file);
            PumpUntil(() => !main.PlayerView.IsAnalyzing);
            Program.Check(main.PlayerView.Arrangement?.Pitch.Offset == 2, main.PlayerView.AnalysisStatus);
            Program.Check(main.PlayerView.CanHitPlayPause && main.PlayerView.AnalysisStatus == "Sẵn sàng");
            Program.Check(history.Key == -27 && history.Transpose == Transpose.Up, "Legacy history modified");
            main.PianoSheetView.Update();
            Program.Check(main.PianoSheetView.Result.Length > 0, "Sheet missing");
            var sheetKeys = new string(main.PianoSheetView.Result.Where(char.IsLetter).ToArray());
            var expectedKeys = string.Concat(Keyboard.GetLayout(Keyboard.Layout.QWERTY).Select(k => k.ToString().Last()));
            Program.Check(sheetKeys == expectedKeys, "Sheet differs from playback or duplicates held notes at bar boundaries");
            var firstArrangement = main.PlayerView.Arrangement;

            main.SettingsView.SelectedInstrument = Keyboard.InstrumentNames.Single(p => p.Key == Keyboard.Instrument.VintageLyre);
            PumpUntil(() => !main.PlayerView.IsAnalyzing && main.PlayerView.Arrangement != firstArrangement);
            Program.Check(main.PlayerView.Arrangement!.Midi.GetNotes().All(n => Keyboard.GetNotes(Keyboard.Instrument.VintageLyre).Contains((int)n.NoteNumber)));

            main.PlayerView.MidiTracks[0].IsChecked = false;
            PumpUntil(() => !main.PlayerView.IsAnalyzing);
            Program.Check(main.PlayerView.Arrangement?.Total == 0 && !main.PlayerView.CanHitPlayPause);
            Program.Check(main.PianoSheetView.Result.Length == 0);
            main.PlayerView.MidiTracks[0].IsChecked = true;
            PumpUntil(() => !main.PlayerView.IsAnalyzing);
            Program.Check(main.PlayerView.Arrangement?.Total == notes.Length);

            // Superseded analyses and closing while work is pending must not restore stale results.
            for (var i = 0; i < 8; i++) main.PlayerView.Handle(file);
            main.PlayerView.CloseFile();
            PumpUntil(() => !main.PlayerView.IsAnalyzing);
            main.PlayerView.Handle(file);
            PumpUntil(() => !main.PlayerView.IsAnalyzing);
            Program.Check(main.PlayerView.Arrangement?.Total == notes.Length);
            // Exercise the same single-file path as the picker, without opening a native dialog.
            var secondPath = Path.Combine(fixtureDirectory, "second-song.mid");
            new MidiFile(new[] { new Note((SevenBitNumber)60, 120, 0) }.ToTrackChunk()).Write(secondPath, overwriteFile: true);
            var opened = main.PlayerView.OpenMidi(secondPath);
            PumpUntil(() => opened.IsCompleted);
            opened.GetAwaiter().GetResult();
            Program.Check(main.PlaylistView.OpenedFile?.Path == secondPath && main.PlaylistView.Tracks.Count == 1);
            Program.Check(main.PlayerView.Arrangement?.Total == 1 && main.PlaylistView.Loop == PlaylistViewModel.LoopMode.Once);
            main.PlayerView.SongPosition = 0.1;
            main.PlayerView.Stop();
            Program.Check(main.PlayerView.CurrentTime == TimeSpan.Zero && main.PlayerView.Playback?.IsRunning == false);
            var invalid = main.PlayerView.OpenMidi(Path.Combine(fixtureDirectory, "missing.mid"));
            PumpUntil(() => invalid.IsCompleted);
            Program.Check(!main.PlayerView.CanHitPlayPause && main.PlayerView.Arrangement is null);

            var player = new LyrePlayerView();
            player.InitializeComponent();
            player.DataContext = main.PlayerView;
            Program.Check(!Descendants(player).OfType<Expander>().Any());
            Program.Check(Descendants(player).OfType<ComboBox>().Count() == 1, "Only the instrument dropdown is exposed");
            Program.Check(Descendants(player).OfType<ComboBox>().Single().Items.Count == 12);
            var buttons = Descendants(player).OfType<Button>().ToArray();
            Program.Check(buttons.Count(b => b.Content?.ToString() != "Chọn đường dẫn Genshin…") == 6,
                "File, audio, cancel, play/pause and stop belong on the main screen");
            var converted = new TaskCompletionSource<string>();
            main.PlayerView.AudioConverter = new FakeAudio((_, progress, _) =>
            { progress.Report("Đang nhận dạng nốt piano…"); return converted.Task; });
            var conversion = main.PlayerView.ConvertAudio("fixture.wav");
            Program.Check(main.PlayerView.IsConvertingAudio && !main.PlayerView.CanHitPlayPause && !main.PlayerView.CanChooseFile);
            Render(player, 392, 640, "artifacts/audio-converting.png");
            converted.SetResult(path);
            PumpUntil(() => conversion.IsCompleted);
            Program.Check(!main.PlayerView.IsConvertingAudio && main.PlayerView.CanHitPlayPause && main.PlaylistView.OpenedFile?.Path == path);
            Program.Check(main.PlayerView.CanSaveConvertedMidi && main.PlayerView.ConvertedMidiPath == path);
            main.PlayerView.AudioConverter = new FakeAudio(async (_, _, token) =>
            { await Task.Delay(30000, token); return path; });
            var cancelled = main.PlayerView.ConvertAudio("fixture.wav");
            main.PlayerView.CancelAudio();
            PumpUntil(() => cancelled.IsCompleted);
            Program.Check(!main.PlayerView.IsConvertingAudio && main.PlayerView.AudioStatus.Contains("hủy"));
            var stale = new TaskCompletionSource<string>();
            main.PlayerView.AudioConverter = new FakeAudio((_, _, _) => stale.Task);
            var staleJob = main.PlayerView.ConvertAudio("fixture.wav");
            var replacement = main.PlayerView.OpenMidi(secondPath);
            PumpUntil(() => replacement.IsCompleted);
            stale.SetResult(path);
            PumpUntil(() => staleJob.IsCompleted);
            Program.Check(main.PlaylistView.OpenedFile?.Path == secondPath);
            var finalOpen = main.PlayerView.OpenMidi(path);
            PumpUntil(() => finalOpen.IsCompleted);
            Render(player, 512, 480, "artifacts/simple-player-ready.png");
            main.SettingsView.SelectedProfile = AutoLyre.Data.Midi.InstrumentProfiles.Get("vodyanitsa");
            PumpUntil(() => !main.PlayerView.IsAnalyzing);
            Program.Check(!main.PlayerView.CanHitPlayPause && main.PlayerView.Playback is null && main.PlayerView.Plan is null);
            Program.Check(main.PlayerView.AnalysisStatus.Contains("Chưa xác minh"));
            Render(player, 392, 700, "artifacts/simple-player-settings.png");
            main.SettingsView.SelectedProfile = AutoLyre.Data.Midi.InstrumentProfiles.Get("arataki_drum");
            PumpUntil(() => !main.PlayerView.IsAnalyzing);
            Program.Check(!main.PlayerView.CanHitPlayPause && main.PlayerView.AnalysisStatus.Contains("kênh 10"));
            main.SettingsView.SelectedProfile = AutoLyre.Data.Midi.InstrumentProfiles.All[0];
            PumpUntil(() => !main.PlayerView.IsAnalyzing);
            Program.Check(main.PlayerView.CanHitPlayPause);
            main.PlayerView.CloseFile();
            Render(player, 392, 400, "artifacts/simple-player-empty.png");
            Console.WriteLine("UI: compact controls, single-file replacement, stop, errors, legacy key, mapping, sheet and cancellation verified; rendered 3 previews.");
        }
        finally
        {
            main.PlayerView.Shutdown();
            app.Shutdown();
        }
    }

    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        // A real off-screen HWND is needed for theme templates and Loaded animations.
        var host = new Window
        {
            Content = element,
            Width = width,
            Height = height,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Background = Brushes.WhiteSmoke
        };
        host.Show();
        var ready = Stopwatch.StartNew();
        PumpUntil(() => ready.ElapsedMilliseconds >= 300);
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        PumpUntil(() => true);
        foreach (var control in Descendants(element).OfType<Button>().Where(b => b.ActualWidth > 0))
            Program.Check(control.Foreground is not SolidColorBrush brush || brush.Color.A > 0,
                "A rendered button has invisible text");
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        host.Content = null;
        host.Close();
    }

    private sealed class FakeAudio : IAudioTranscriber
    {
        private readonly Func<string, IProgress<string>, CancellationToken, Task<string>> _run;
        public FakeAudio(Func<string, IProgress<string>, CancellationToken, Task<string>> run) => _run = run;
        public Task<string> ConvertAsync(string source, IProgress<string> progress, CancellationToken token) => _run(source, progress, token);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            if (complete()) return;
            Thread.Sleep(5);
        } while (watch.Elapsed < TimeSpan.FromSeconds(20));
        throw new TimeoutException("WPF operation did not complete");
    }
}
