using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoLyre.Data.Midi;

/// <summary>Profile-owned key lifetime. Sink callbacks allow tests without keyboard injection.</summary>
public sealed class InstrumentOutput
{
    private readonly Action<char> _down, _up, _tap;
    private readonly HashSet<char> _held = new();
    public InstrumentOutput(Action<char> down, Action<char> up, Action<char> tap)
    { _down = down; _up = up; _tap = tap; }
    public void Send(InstrumentProfile profile, InstrumentAction action, bool down)
    {
        profile.RequirePlayable();
        if (!profile.Actions.Contains(action)) throw new ArgumentException("Action is not registered in this profile.");
        if (profile.Articulation == Articulation.Tap)
        {
            if (down) _tap(action.Key);
            return;
        }
        if (down)
        {
            if (_held.Remove(action.Key)) _up(action.Key);
            _down(action.Key); _held.Add(action.Key);
        }
        else if (_held.Remove(action.Key)) _up(action.Key);
    }
    public void ReleaseAll()
    {
        foreach (var key in _held) _up(key);
        _held.Clear();
    }
}
