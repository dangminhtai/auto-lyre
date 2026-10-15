using System;
using System.Collections.Generic;
using System.Linq;
using WindowsInput;
using WindowsInput.Native;
using static AutoLyre.App.Core.Keyboard;

namespace AutoLyre.App.Core;

public static class LyrePlayer
{
    private static readonly IInputSimulator Input = new InputSimulator();

    public static void PressKey(VirtualKeyCode key) => Input.Keyboard.KeyDown(key);
    public static void ReleaseKey(VirtualKeyCode key) => Input.Keyboard.KeyUp(key);

    public static void NoteDown(int noteId, Layout layout, Instrument instrument)
        => InteractNote(noteId, layout, instrument, Input.Keyboard.KeyDown);

    public static void NoteUp(int noteId, Layout layout, Instrument instrument)
        => InteractNote(noteId, layout, instrument, Input.Keyboard.KeyUp);

    public static void PlayNote(int noteId, Layout layout, Instrument instrument)
        => InteractNote(noteId, layout, instrument, Input.Keyboard.KeyPress);

    public static bool TryGetKey(Layout layout, Instrument instrument, int noteId, out VirtualKeyCode key)
    {
        var keys = GetLayout(layout);
        var notes = GetNotes(instrument);
        return TryGetKey(keys, notes, noteId, out key);
    }

    private static bool TryGetKey(
        this IEnumerable<VirtualKeyCode> keys, IList<int> notes,
        int noteId, out VirtualKeyCode key)
    {
        var keyIndex = notes.IndexOf(noteId);
        key = keys.ElementAtOrDefault(keyIndex);

        return keyIndex != -1;
    }

    private static void InteractNote(
        int noteId, Layout layout, Instrument instrument,
        Func<VirtualKeyCode, IKeyboardSimulator> action)
    {
        if (TryGetKey(layout, instrument, noteId, out var key))
            action.Invoke(key);
    }
}
