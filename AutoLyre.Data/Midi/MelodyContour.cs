using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AutoLyre.Data.Midi;

/// <summary>Find a playable sequence jointly, retaining pitch classes whenever available.</summary>
public static class MelodyContour
{
    public static int[] Map(IReadOnlyList<int> source, int offset, IEnumerable<int> allowed,
        CancellationToken token = default)
    {
        var keys = allowed.Distinct().OrderBy(n => n).ToArray();
        if (keys.Length == 0) throw new ArgumentException("Empty instrument range.");
        if (source.Count == 0) return Array.Empty<int>();
        var back = new int[source.Count, keys.Length];
        var previous = Enumerable.Repeat(double.PositiveInfinity, keys.Length).ToArray();
        for (var i = 0; i < source.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var pitch = source[i] - offset;
            var hasClass = keys.Any(k => (k - pitch) % 12 == 0);
            var current = Enumerable.Repeat(double.PositiveInfinity, keys.Length).ToArray();
            for (var k = 0; k < keys.Length; k++)
            {
                if (hasClass && (keys[k] - pitch) % 12 != 0) continue;
                var pitchClassError = Math.Abs(keys[k] - pitch) % 12;
                pitchClassError = Math.Min(pitchClassError, 12 - pitchClassError);
                var local = 16 * pitchClassError + 2 * Math.Abs(keys[k] - pitch);
                if (i == 0) { current[k] = local; continue; }
                var interval = source[i] - source[i - 1];
                for (var j = 0; j < keys.Length; j++)
                {
                    var mappedInterval = keys[k] - keys[j];
                    var transition = .75 * Math.Abs(mappedInterval - interval)
                        // Reversing a phrase costs more than moving one note by an octave (24).
                        + (interval != 0 && Math.Sign(mappedInterval) != Math.Sign(interval) ? 32 : 0);
                    var score = previous[j] + transition + local;
                    if (score < current[k]) { current[k] = score; back[i, k] = j; }
                }
            }
            previous = current;
        }
        var best = Enumerable.Range(0, keys.Length).OrderBy(i => previous[i]).First();
        var result = new int[source.Count];
        for (var i = source.Count - 1; i >= 0; i--) { result[i] = keys[best]; best = back[i, best]; }
        return result;
    }
}
