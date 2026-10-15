using System.Collections.Generic;
using System.Linq;

namespace AutoLyre.Data.Midi;

// Caller serializes access together with output callbacks.
public sealed class HeldNotes<T> where T : notnull
{
    private readonly Dictionary<T, int> _counts = new();
    public int Count => _counts.Count;
    public bool Press(T key)
    {
        _counts.TryGetValue(key, out var count);
        _counts[key] = count + 1;
        return count == 0;
    }
    public bool Release(T key)
    {
        if (!_counts.TryGetValue(key, out var count)) return false;
        if (count > 1) { _counts[key] = count - 1; return false; }
        _counts.Remove(key);
        return true;
    }
    public T[] Drain()
    {
        var keys = _counts.Keys.ToArray();
        _counts.Clear();
        return keys;
    }
}
