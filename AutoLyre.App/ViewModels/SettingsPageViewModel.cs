using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AutoLyre.Data;
using AutoLyre.Data.Entities;
using AutoLyre.Data.Git;
using AutoLyre.Data.Midi;
using AutoLyre.Data.Notification;
using AutoLyre.Data.Properties;
using AutoLyre.App.Core;
using AutoLyre.App.ModernWPF;
using AutoLyre.App.ModernWPF.Animation;
using AutoLyre.App.ModernWPF.Animation.Transitions;
using JetBrains.Annotations;
using Microsoft.Win32;
using ModernWpf;
using ModernWpf.Controls;
using Stylet;
using StyletIoC;
using Wpf.Ui.Appearance;
using Wpf.Ui.Mvvm.Contracts;

namespace AutoLyre.App.ViewModels;

public class SettingsPageViewModel : Screen
{
    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly IThemeService _theme;
    private readonly MainWindowViewModel _main;

    public SettingsPageViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
        _theme = ioc.Get<IThemeService>();
        _main = main;

        // The single-purpose player always sends notes to the game at the file's tempo.
        UseSpeakers = false;
        HoldNotes = false;
        MergeNotes = false;
        SelectedSpeed = MidiSpeeds[3];

        ThemeManager.Current.ApplicationTheme = Settings.AppTheme switch
        {
            0 => ApplicationTheme.Light,
            1 => ApplicationTheme.Dark,
            _ => null
        };
    }

    public bool AutoCheckUpdates { get; set; } = Settings.AutoCheckUpdates;

    public bool CanChangeTime => PlayTimerToken is null;

    public bool CanStartStopTimer => DateTime - DateTime.Now > TimeSpan.Zero;

    public bool CanUseSpeakers { get; set; } = true;

    public bool UseSpeakers { get; set; } = Settings.UseSpeakers;
    public bool HoldNotes { get; set; } = Settings.HoldNotes;

    private void OnUseSpeakersChanged()
    {
        Settings.Modify(s => s.UseSpeakers = UseSpeakers);
        _events.Publish(this);
    }

    private void OnHoldNotesChanged()
    {
        Settings.Modify(s => s.HoldNotes = HoldNotes);
        _events.Publish(this);
    }

    public bool IncludeBetaUpdates { get; set; } = Settings.IncludeBetaUpdates;

    public bool IsCheckingUpdate { get; set; }

    public bool MergeNotes { get; set; } = Settings.MergeNotes;

    public bool NeedsUpdate => ProgramVersion < LatestVersion.Version;

    [UsedImplicitly] public CancellationTokenSource? PlayTimerToken { get; private set; }

    public static CaptionedObject<Transition>? Transition { get; set; } =
        TransitionCollection.Transitions[Settings.SelectedTransition];

    public DateTime DateTime { get; set; } = DateTime.Now;

    public GitVersion LatestVersion { get; set; } = new();

    public KeyValuePair<Keyboard.Instrument, string> SelectedInstrument { get; set; } =
        Keyboard.InstrumentNames.FirstOrDefault(p => (int)p.Key == Settings.SelectedInstrument);

    public KeyValuePair<Keyboard.Layout, string> SelectedLayout { get; set; } =
        Keyboard.LayoutNames.FirstOrDefault(p => (int)p.Key == Settings.SelectedLayout);


    public static List<MidiSpeed> MidiSpeeds { get; } = new()
    {
        new("0.25x", 0.25),
        new("0.5x", 0.5),
        new("0.75x", 0.75),
        new("Normal", 1),
        new("1.25x", 1.25),
        new("1.5x", 1.5),
        new("1.75x", 1.75),
        new("2x", 2)
    };

    public MidiSpeed SelectedSpeed { get; set; } = MidiSpeeds[Settings.SelectedSpeed];

    public static string GenshinLocation
    {
        get => Settings.GenshinLocation;
        set => Settings.GenshinLocation = value;
    }

    public string GameLocation => string.IsNullOrWhiteSpace(GenshinLocation) ? "Chưa chọn đường dẫn trò chơi" : GenshinLocation;


    public string TimerText => CanChangeTime ? "Start" : "Stop";

    [UsedImplicitly] public string UpdateString { get; set; } = string.Empty;

    public uint MergeMilliseconds { get; set; } = Settings.MergeMilliseconds;

    public static Version ProgramVersion => Assembly.GetExecutingAssembly().GetName().Version!;

    private PlaylistViewModel Playlist => _main.PlaylistView;

    public async Task<bool> TryGetLocationAsync()
    {
        var locations = new[]
        {
            // User set location
            Settings.GenshinLocation,

            // Default install location
            @"C:\Program Files\Genshin Impact\Genshin Impact Game\GenshinImpact.exe",
            @"C:\Program Files\Genshin Impact\Genshin Impact Game\YuanShen.exe",

            // Custom install location
            Path.Combine(WindowHelper.InstallLocation ?? string.Empty, @"Genshin Impact Game\GenshinImpact.exe"),
            Path.Combine(WindowHelper.InstallLocation ?? string.Empty, @"Genshin Impact Game\YuanShen.exe"),

            // Relative location
            AppContext.BaseDirectory + "GenshinImpact.exe",
            AppContext.BaseDirectory + "YuanShen.exe"
        };

        foreach (var location in locations)
        {
            if (await TrySetLocationAsync(location))
                return true;
        }

        return false;
    }

    [PublicAPI]
    public async Task SetLocation()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Chọn tệp chạy Genshin Impact",
            Filter = "Tệp chương trình (*.exe)|*.exe",
            InitialDirectory = WindowHelper.InstallLocation is null
                ? @"C:\Program Files\Genshin Impact\Genshin Impact Game\"
                : Path.Combine(WindowHelper.InstallLocation, "Genshin Impact Game")
        };

        if (openFileDialog.ShowDialog() != true) return;
        await TrySetLocationAsync(openFileDialog.FileName);
    }

    [UsedImplicitly]
    public async Task StartStopTimer()
    {
        if (PlayTimerToken is not null)
        {
            PlayTimerToken.Cancel();
            return;
        }

        PlayTimerToken = new();

        var start = DateTime - DateTime.Now;
        await Task.Delay(start, PlayTimerToken.Token)
            .ContinueWith(_ => { });

        if (!PlayTimerToken.IsCancellationRequested)
            _events.Publish(new PlayTimerNotification());

        PlayTimerToken = null;
    }

    [UsedImplicitly]
    public void OnThemeChanged()
    {
        _theme.SetTheme(ThemeManager.Current.ApplicationTheme switch
        {
            ApplicationTheme.Light => ThemeType.Light,
            ApplicationTheme.Dark => ThemeType.Dark,
            _ => _theme.GetSystemTheme()
        });

        Settings.Modify(s => s.AppTheme = (int?)ThemeManager.Current.ApplicationTheme ?? -1);
    }

    [UsedImplicitly]
    public void SetTimeToNow() => DateTime = DateTime.Now;

    private async Task<bool> TrySetLocationAsync(string? location)
    {
        if (!File.Exists(location)) return false;
        if (!Path.GetFileName(location).Equals("GenshinImpact.exe", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(location).Equals("YuanShen.exe", StringComparison.OrdinalIgnoreCase))
        {
            var dialog = new ContentDialog
            {
                Title = "Chưa đúng tệp trò chơi",
                Content = "Hãy chọn GenshinImpact.exe hoặc YuanShen.exe trong thư mục trò chơi.",

                CloseButtonText = "Đóng"
            };

            await dialog.ShowAsync();
            return false;
        }

        Settings.Modify(s => s.GenshinLocation = location);
        NotifyOfPropertyChange(() => GameLocation);

        return true;
    }

    [UsedImplicitly]
    private void OnMergeMillisecondsChanged()
    {
        Settings.Modify(s => s.MergeMilliseconds = MergeMilliseconds);
        _events.Publish(this);
    }

    [UsedImplicitly]
    private void OnMergeNotesChanged()
    {
        Settings.Modify(s => s.MergeNotes = MergeNotes);
        _events.Publish(new MergeNotesNotification(MergeNotes));
    }

    [UsedImplicitly]
    private void OnSelectedInstrumentChanged()
    {
        var instrument = (int)SelectedInstrument.Key;
        Settings.Modify(s => s.SelectedInstrument = instrument);
        _events.Publish(this);
    }

    [UsedImplicitly]
    private void OnSelectedLayoutChanged()
    {
        var layout = (int)SelectedLayout.Key;
        Settings.Modify(s => s.SelectedLayout = layout);
        _events.Publish(this);
    }

    [UsedImplicitly]
    private void OnSelectedSpeedChanged() => _events.Publish(this);

}
