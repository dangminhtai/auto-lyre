using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoLyre.Data.Properties;
using AutoLyre.App.Core;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;
using PropertyChanged;
using Stylet;

namespace AutoLyre.App.ViewModels;

public class PianoSheetViewModel : Screen
{
    private static readonly Settings Settings = Settings.Default;

    private readonly MainWindowViewModel _main;
    private uint _bars = 1;
    private uint _beats;
    private uint _shorten = 1;

    public PianoSheetViewModel(MainWindowViewModel main) { _main = main; }

    [OnChangedMethod(nameof(Update))] public char Delimiter { get; set; } = '.';

    [OnChangedMethod(nameof(Update))]
    public KeyValuePair<Keyboard.Layout, string> SelectedLayout
    {
        get => SettingsPage.SelectedLayout;
        set => SettingsPage.SelectedLayout = value;
    }

    public PlaylistViewModel PlaylistView => _main.PlaylistView;

    public SettingsPageViewModel SettingsPage => _main.SettingsView;

    public string Result { get; private set; } = string.Empty;

    public void Clear() => Result = string.Empty;

    [OnChangedMethod(nameof(Update))]
    public uint Bars
    {
        get => _bars;
        set => SetAndNotify(ref _bars, Math.Max(value, 0));
    }

    [OnChangedMethod(nameof(Update))]
    public uint Beats
    {
        get => _beats;
        set => SetAndNotify(ref _beats, Math.Max(value, 0));
    }

    [OnChangedMethod(nameof(Update))]
    public uint Shorten
    {
        get => _shorten;
        set => SetAndNotify(ref _shorten, Math.Max(value, 1));
    }

    public void Update()
    {
        Result = string.Empty;
        var midi = _main.PlayerView?.Arrangement?.Midi;
        if (midi is null) return;

        if (Bars == 0 && Beats == 0)
            return;

        var layout = SettingsPage.SelectedLayout.Key;
        var instrument = SettingsPage.SelectedInstrument.Key;

        // A sheet describes key presses, not sustained note segments. Avoid duplicating a
        // held note at each bar boundary when splitting the display into lines.
        var onsets = midi.Clone();
        foreach (var track in onsets.GetTrackChunks())
        {
            using var manager = track.ManageNotes();
            foreach (var note in manager.Objects) note.Length = 1;
        }
        var split = onsets.SplitByGrid(new SteppedGrid(new BarBeatTicksTimeSpan(Bars, Beats, 0)));

        var sb = new StringBuilder();
        foreach (var bar in split)
        {
            var notes = bar.GetNotes();
            if (notes.Count == 0)
                continue;

            long last = 0;

            foreach (var note in notes.OrderBy(n => n.Time))
            {
                var id = (int)note.NoteNumber;

                if (!LyrePlayer.TryGetKey(layout, instrument, id, out var key)) continue;

                var difference = note.Time - last;
                var dotCount = difference / Shorten;

                sb.Append(new string(Delimiter, (int)dotCount));
                sb.Append(key.ToString().Last());

                last = note.Time;
            }

            sb.AppendLine();
        }

        Result = sb.ToString();
    }

    protected override void OnActivate() => Update();
}
