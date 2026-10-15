using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Melanchall.DryWetMidi.Interaction;

namespace AutoLyre.Data.Midi;

/// <summary>Deterministic voice heuristic; confidence is a score margin, not a probability.</summary>
public sealed class MelodyAnalysis
{
    private MelodyAnalysis(HashSet<Note> melody, string method) { Melody = melody; Method = method; }
    public HashSet<Note> Melody { get; }
    public string Method { get; }

    public static MelodyAnalysis Detect(IReadOnlyList<Note[]> tracks, CancellationToken token)
    {
        var voices = tracks.SelectMany(t => t.GroupBy(n => (int)n.Channel)
            .Select(g => g.OrderBy(n => n.Time).ThenBy(n => n.NoteNumber).ToArray()))
            .Where(v => v.Length > 0).ToArray();
        token.ThrowIfCancellationRequested();
        if (voices.Length == 0) return new(new(), "empty");
        // A single stream supplies no reliable melody/accompaniment separation.
        if (voices.Length == 1) return new(new(), "fallback-single-voice");
        var durationScale = Math.Max(1, Median(voices.SelectMany(v => v).Select(n => (double)n.Length)));
        var candidates = voices.Select(v =>
        {
            token.ThrowIfCancellationRequested();
            var onsets = v.GroupBy(n => n.Time).ToArray();
            var line = onsets.Select(g => g.OrderByDescending(n => n.NoteNumber).First()).ToArray();
            var overlap = 0;
            long end = -1;
            foreach (var n in line) { if (n.Time < end) overlap++; end = Math.Max(end, n.EndTime); }
            var monophony = ((double)line.Length / v.Length) * (1.0 - (double)overlap / line.Length);
            var continuity = line.Length < 2 ? 0 : 1 - line.Zip(line.Skip(1),
                (a, b) => Math.Min(24, Math.Abs((int)a.NoteNumber - b.NoteNumber)) / 24.0).Average();
            var sustained = Math.Min(2, Median(v.Select(n => (double)n.Length)) / durationScale) / 2;
            var register = Math.Clamp((Median(v.Select(n => (double)(int)n.NoteNumber)) - 36) / 48, 0, 1);
            var score = .45 * monophony + .25 * continuity + .20 * sustained + .10 * register;
            // Tiny ornaments must not outrank the song's sustained voices.
            score *= Math.Min(1, line.Length / 8.0);
            return (Notes: v, Score: score);
        }).OrderByDescending(c => c.Score).ToArray();
        if (candidates[0].Score - candidates[1].Score < .04)
            return new(new(), "fallback-ambiguous");
        return new(candidates[0].Notes.ToHashSet(), "voice-heuristic");
    }

    public IReadOnlyList<(int Note, double Weight)> Weights(IEnumerable<Note> source)
    {
        var notes = source.ToArray();
        if (Melody.Count == 0) return notes.Select(n => ((int)n.NoteNumber, 1.0)).ToArray();
        var median = Math.Max(1, Median(notes.Select(n => (double)n.Length)));
        double Salience(Note n) => Math.Clamp(Math.Sqrt(n.Length / median), .5, 2)
            * (.75 + .25 * (int)n.Velocity / 127.0);
        var mainSum = notes.Where(Melody.Contains).Sum(Salience);
        var otherSum = notes.Where(n => !Melody.Contains(n)).Sum(Salience);
        return notes.Select(n => ((int)n.NoteNumber, Salience(n) *
            (Melody.Contains(n) ? .8 / mainSum : .2 / Math.Max(otherSum, double.Epsilon)))).ToArray();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
}
