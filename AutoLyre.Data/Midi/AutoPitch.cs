using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AutoLyre.Data.Midi;

public enum PitchChange { Exact, Octave, Rounded }

public sealed class AutoPitch
{
    public const string Version = "1";
    public const int MinimumOffset = -27;
    public const int MaximumOffset = 24;
    private readonly int[] _notes;

    private AutoPitch(int offset, int[] notes) { Offset = offset; _notes = notes; }

    public int Offset { get; }

    public static AutoPitch Analyze(IEnumerable<int> source, IEnumerable<int> playable,
        CancellationToken cancellationToken = default)
    {
        var notes = Validate(playable);
        var histogram = source.GroupBy(n => n).Select(g => (Note: g.Key, Count: g.LongCount())).ToArray();
        if (histogram.Any(n => n.Note < 0 || n.Note > 127))
            throw new ArgumentOutOfRangeException(nameof(source));

        // Lexicographic comparison makes the objective explicit and reproducible.
        (long Exact, long Unmatched, long Distance, int Shift, int Offset)? best = null;
        var chosen = 0;
        for (var offset = MinimumOffset; offset <= MaximumOffset; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long exact = 0, unmatched = 0, distance = 0;
            foreach (var (note, count) in histogram)
            {
                var pitch = note - offset;
                if (Array.IndexOf(notes, pitch) >= 0) { exact += count; continue; }
                var sameClass = notes.Where(n => (n - pitch) % 12 == 0).ToArray();
                if (sameClass.Length == 0) unmatched += count;
                else distance += sameClass.Min(n => Math.Abs(n - pitch)) * count;
            }
            var score = (exact, -unmatched, -distance, -Math.Abs(offset), offset);
            if (best is null || score.CompareTo(best.Value) > 0) { best = score; chosen = offset; }
        }
        return new AutoPitch(chosen, notes);
    }

    public static AutoPitch Direct(IEnumerable<int> playable) => new(0, Validate(playable));

    public int Map(int source, out PitchChange change)
    {
        if (source < 0 || source > 127) throw new ArgumentOutOfRangeException(nameof(source));
        var pitch = source - Offset;
        if (Array.IndexOf(_notes, pitch) >= 0) { change = PitchChange.Exact; return pitch; }
        var sameClass = _notes.Where(n => (n - pitch) % 12 == 0).ToArray();
        change = sameClass.Length > 0 ? PitchChange.Octave : PitchChange.Rounded;
        return (sameClass.Length > 0 ? sameClass : _notes)
            .OrderBy(n => Math.Abs(n - pitch)).ThenBy(n => n).First();
    }

    private static int[] Validate(IEnumerable<int> playable)
    {
        var notes = playable.Distinct().OrderBy(n => n).ToArray();
        if (notes.Length == 0 || notes.Any(n => n < 0 || n > 127))
            throw new ArgumentException("Instrument must have valid MIDI notes.", nameof(playable));
        return notes;
    }
}
