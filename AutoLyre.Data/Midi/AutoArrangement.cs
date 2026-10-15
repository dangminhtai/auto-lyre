using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Tools;
using RawMidiFile = Melanchall.DryWetMidi.Core.MidiFile;

namespace AutoLyre.Data.Midi;

public sealed class AutoArrangement
{
    private AutoArrangement(RawMidiFile midi, AutoPitch pitch, int exact, int octave, int rounded,
        int collisions, int baseline, int percussion, int droppedEvents)
    {
        Midi = midi; Pitch = pitch; Exact = exact; Octave = octave; Rounded = rounded;
        Collisions = collisions; BaselineExact = baseline; Percussion = percussion;
        DroppedEvents = droppedEvents;
    }

    public RawMidiFile Midi { get; }
    public AutoPitch Pitch { get; }
    public int Exact { get; }
    public int Octave { get; }
    public int Rounded { get; }
    public int Total => Exact + Octave + Rounded;
    public int Collisions { get; }
    public int BaselineExact { get; }
    public int Percussion { get; }
    public int DroppedEvents { get; }
    public string MelodyMethod { get; private set; } = "";
    public int MelodyTotal { get; private set; }
    public int MelodyExact { get; private set; }
    public int V1MelodyExact { get; private set; }
    public int V1Exact { get; private set; }
    public int V1Offset { get; private set; }

    public static AutoArrangement Create(RawMidiFile source, IReadOnlyList<bool> selectedTracks,
        IEnumerable<int> playable, bool merge = false, uint mergeMilliseconds = 0,
        CancellationToken cancellationToken = default)
    {
        var allowed = playable.ToArray();
        var midi = source.Clone();
        var tracks = midi.GetTrackChunks().ToArray();
        if (tracks.Length != selectedTracks.Count) throw new ArgumentException("Track selection changed.");
        var percussion = 0;
        for (var i = 0; i < tracks.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = tracks[i];
            // The pinned parser does not pair velocity-zero NoteOn events in all in-memory inputs.
            // Normalize them explicitly before note detection, without changing absolute timing.
            var timed = track.GetTimedEvents().Select(e => e.Event is NoteOnEvent on && on.Velocity == 0
                ? new TimedEvent(new NoteOffEvent(on.NoteNumber, (Melanchall.DryWetMidi.Common.SevenBitNumber)0)
                { Channel = on.Channel }, e.Time)
                : e).ToArray();
            if (selectedTracks[i]) percussion += timed.Count(e => e.Event is NoteOnEvent on && on.Channel == 9);
            // Keep absolute timing and metadata even on deselected tracks.
            var retained = timed.Where(e => e.Event is not ChannelEvent channel ||
                (selectedTracks[i] && channel.Channel != 9)).ToArray();
            track.Events.Clear();
            track.Events.AddRange(retained.ToTrackChunk().Events);
        }
        if (merge)
            midi.MergeObjects(ObjectType.Note, new ObjectsMergingSettings
            {
                VelocityMergingPolicy = VelocityMergingPolicy.Average,
                Tolerance = new MetricTimeSpan(0, 0, 0, (int)Math.Min(mergeMilliseconds, int.MaxValue))
            });

        // Reuse the same Note instances throughout detection, weighting and mapping.
        var trackNotes = tracks.Select(t => t.GetNotes().ToArray()).ToArray();
        var sourceNotes = trackNotes.SelectMany(n => n).ToArray();
        var melody = MelodyAnalysis.Detect(trackNotes, cancellationToken);
        var v1 = AutoPitch.Analyze(sourceNotes.Select(n => (int)n.NoteNumber), allowed, cancellationToken);
        var pitch = melody.Melody.Count == 0 ? v1 :
            AutoPitch.AnalyzeWeighted(melody.Weights(sourceNotes), allowed, cancellationToken);
        var melodyExact = melody.Melody.Count(n => allowed.Contains(n.NoteNumber - pitch.Offset));
        var v1MelodyExact = melody.Melody.Count(n => allowed.Contains(n.NoteNumber - v1.Offset));
        var v1Exact = sourceNotes.Count(n => allowed.Contains(n.NoteNumber - v1.Offset));
        var baseline = sourceNotes.Count(n => allowed.Contains((int)n.NoteNumber));
        var counts = new int[3];
        var droppedEvents = 0;
        var intervals = new List<(int Note, long Start, long End)>();
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = tracks[trackIndex];
            var notes = trackNotes[trackIndex];
            droppedEvents += track.Events.Count(e => e is NoteEvent) - notes.Length * 2;
            var metadata = track.GetTimedEvents().Where(e => e.Event is not NoteEvent).Cast<ITimedObject>().ToArray();
            foreach (var note in notes)
            {
                var mapped = pitch.Map(note.NoteNumber, out var change);
                counts[(int)change]++;
                note.NoteNumber = (Melanchall.DryWetMidi.Common.SevenBitNumber)mapped;
                intervals.Add((mapped, note.Time, note.EndTime));
            }
            // Rebuild from complete notes only: unmatched events must never create a stuck key.
            var rebuilt = metadata.Concat(notes).ToTrackChunk();
            track.Events.Clear();
            track.Events.AddRange(rebuilt.Events);
        }
        // Extra overlapping intervals landing on one physical key, across all channels/tracks.
        var collisions = 0;
        foreach (var group in intervals.GroupBy(n => n.Note))
        {
            long end = -1;
            foreach (var n in group.OrderBy(n => n.Start))
            {
                if (n.Start < end) collisions++;
                end = Math.Max(end, n.End);
            }
        }
        return new AutoArrangement(midi, pitch, counts[0], counts[1], counts[2], collisions, baseline, percussion, droppedEvents)
        {
            MelodyMethod = melody.Method, MelodyTotal = melody.Melody.Count,
            MelodyExact = melodyExact, V1MelodyExact = v1MelodyExact,
            V1Exact = v1Exact, V1Offset = v1.Offset
        };
    }
}
