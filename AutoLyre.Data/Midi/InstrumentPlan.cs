using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RawMidi = Melanchall.DryWetMidi.Core.MidiFile;

namespace AutoLyre.Data.Midi;

public sealed record PlannedAction(int ActionId, long Time, long Length, int Velocity);

/// <summary>The transport encodes action IDs as note numbers solely for timing, never for sound.</summary>
public sealed class InstrumentPlan
{
    public const string Version = "3-profile-actions";
    private InstrumentPlan(InstrumentProfile profile, RawMidi source, IEnumerable<PlannedAction> actions,
        AutoArrangement? arrangement)
    {
        Profile = profile; Arrangement = arrangement;
        var normalized = new List<PlannedAction>();
        foreach (var key in actions.GroupBy(a => a.ActionId))
        {
            var line = key.GroupBy(a => a.Time).Select(g => g.OrderByDescending(a => a.Length)
                .ThenByDescending(a => a.Velocity).First()).OrderBy(a => a.Time).ToArray();
            for (var i = 0; i < line.Length; i++)
            {
                var a = line[i];
                var length = Math.Max(1, a.Length);
                if (i + 1 < line.Length) length = Math.Min(length, line[i + 1].Time - a.Time);
                normalized.Add(a with { Length = length });
            }
        }
        Actions = normalized.OrderBy(a => a.Time).ThenBy(a => a.ActionId).ToArray();
        Transport = source.Clone();
        foreach (var track in Transport.GetTrackChunks())
        {
            var metadata = track.GetTimedEvents().Where(e => e.Event is not ChannelEvent).ToArray();
            track.Events.Clear();
            track.Events.AddRange(metadata.ToTrackChunk().Events);
        }
        var events = Actions.SelectMany(a => new[] {
            new TimedEvent(new NoteOnEvent((SevenBitNumber)a.ActionId, (SevenBitNumber)Math.Clamp(a.Velocity, 1, 127)), a.Time),
            new TimedEvent(new NoteOffEvent((SevenBitNumber)a.ActionId, (SevenBitNumber)0), a.Time + a.Length)
        }).OrderBy(e => e.Time).ThenBy(e => e.Event is NoteOffEvent ? 0 : 1);
        Transport.Chunks.Add(events.ToTrackChunk());
    }
    public InstrumentProfile Profile { get; }
    public IReadOnlyList<PlannedAction> Actions { get; }
    public RawMidi Transport { get; }
    public AutoArrangement? Arrangement { get; }

    public static InstrumentPlan Create(RawMidi source, IReadOnlyList<bool> selected,
        InstrumentProfile profile, CancellationToken token = default)
    {
        profile.RequirePlayable();
        token.ThrowIfCancellationRequested();
        var clone = source.Clone();
        var tracks = clone.GetTrackChunks().ToArray();
        if (tracks.Length != selected.Count) throw new ArgumentException("Track selection changed.");
        // Normalize zero-velocity events before complete-note extraction.
        foreach (var track in tracks)
        {
            var normalized = track.GetTimedEvents().Select(e => e.Event is NoteOnEvent n && n.Velocity == 0
                ? new TimedEvent(new NoteOffEvent(n.NoteNumber, (SevenBitNumber)0) { Channel = n.Channel }, e.Time) : e).ToArray();
            track.Events.Clear(); track.Events.AddRange(normalized.ToTrackChunk().Events);
        }
        var input = tracks.Where((_, i) => selected[i]).SelectMany(t => t.GetNotes()).ToArray();
        var actions = new List<PlannedAction>();
        if (profile.Mode == InstrumentMode.Percussion)
        {
            foreach (var note in input.Where(n => n.Channel == 9))
            {
                token.ThrowIfCancellationRequested();
                // GM bass drums / low toms -> Don; other percussion -> Ka (arrangement heuristic).
                var kind = (int)note.NoteNumber is 35 or 36 or 41 or 43 or 45 or 47 or 48 or 50 or 64 or 66
                    ? SoundKind.Don : SoundKind.Ka;
                var action = profile.Actions.FirstOrDefault(a => a.Kind == kind);
                if (action != null) actions.Add(new(action.Id, note.Time, note.Length, note.Velocity));
            }
            return new(profile, source, actions, null);
        }

        var singles = profile.Actions.Where(a => a.Kind == SoundKind.Note).ToArray();
        if (singles.Length == 0) throw new InvalidOperationException("Profile needs calibrated single-note actions.");
        var allowed = singles.Select(a => a.Pitches[0]).ToArray();
        if (profile.Mode == InstrumentMode.Vocal)
        {
            var voices = tracks.Select((t, i) => selected[i] ? t.GetNotes().Where(n => n.Channel != 9).ToArray() : Array.Empty<Note>()).ToArray();
            var melody = MelodyAnalysis.Detect(voices, token);
            var candidates = melody.Melody.Count > 0 ? melody.Melody.AsEnumerable() : voices.SelectMany(n => n);
            var line = candidates.GroupBy(n => n.Time).OrderBy(g => g.Key)
                .Select(g => g.OrderByDescending(n => n.NoteNumber).First()).ToArray();
            var pitch = AutoPitch.Analyze(line.Select(n => (int)n.NoteNumber), allowed, token);
            var mapped = MelodyContour.Map(line.Select(n => (int)n.NoteNumber).ToArray(), pitch.Offset, allowed, token);
            for (var i = 0; i < line.Length; i++)
            {
                var n = line[i];
                var length = Math.Max(1, n.Length);
                if (i + 1 < line.Length) length = Math.Min(length, line[i + 1].Time - n.Time);
                actions.Add(new(singles.First(a => a.Pitches[0] == mapped[i]).Id, n.Time, length, n.Velocity));
            }
            return new(profile, source, actions, null);
        }

        var arranged = AutoArrangement.Create(clone, selected, allowed, cancellationToken: token, optimizeContour: true);
        if (profile.Mode == InstrumentMode.Chord)
        {
            // Match only exact transposed source pitch-class sets, not already-rounded notes.
            foreach (var onset in input.Where(n => n.Channel != 9).GroupBy(n => n.Time))
            {
                token.ThrowIfCancellationRequested();
                var remaining = onset.OrderBy(n => n.NoteNumber).ToList();
                var classes = remaining.Select(n => ((n.NoteNumber - arranged.Pitch.Offset) % 12 + 12) % 12).ToHashSet();
                var chord = profile.Actions.Where(a => a.Kind == SoundKind.Chord
                    && a.Pitches.Select(p => p % 12).ToHashSet().SetEquals(classes))
                    .OrderBy(a => a.Pitches.Sum(p => remaining.Min(n => Math.Abs(p - (n.NoteNumber - arranged.Pitch.Offset)))))
                    .ThenBy(a => a.Id).FirstOrDefault();
                if (chord != null && remaining.Count >= chord.Pitches.Select(p => p % 12).Distinct().Count())
                {
                    actions.Add(new(chord.Id, onset.Key, remaining.Min(n => n.Length), remaining.Max(n => (int)n.Velocity)));
                    // A chord sample cannot replace a melody tone in another register.
                    remaining.RemoveAll(n => chord.Pitches.Contains(n.NoteNumber - arranged.Pitch.Offset));
                }
                foreach (var n in remaining)
                {
                    var mapped = arranged.Pitch.Map(n.NoteNumber, out _);
                    actions.Add(new(singles.First(a => a.Pitches[0] == mapped).Id, n.Time, n.Length, n.Velocity));
                }
            }
        }
        else
            foreach (var n in arranged.Midi.GetNotes())
                actions.Add(new(singles.First(a => a.Pitches[0] == n.NoteNumber).Id, n.Time, n.Length, n.Velocity));
        return new(profile, source, actions, arranged);
    }
}
