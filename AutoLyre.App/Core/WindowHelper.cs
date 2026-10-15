using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AutoLyre.Data.Properties;
using Microsoft.Win32;

namespace AutoLyre.App.Core;

public static class WindowHelper
{
    public static string? InstallLocation => Registry.LocalMachine
        .OpenSubKey(@"SOFTWARE\launcher", false)
        ?.GetValue("InstPath") as string;

    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
    private static string GenshinProcessName
        => Path.GetFileNameWithoutExtension(Settings.Default.GenshinLocation);

    public static bool IsGameFocused()
    {
        var genshinWindow = FindWindowByProcessName(GenshinProcessName);
        return genshinWindow != null &&
            IsWindowFocused((IntPtr)genshinWindow);
    }

    public static void EnsureGameOnTop()
    {
        var genshinWindow = FindWindowByProcessName(GenshinProcessName);
        if (genshinWindow is null) return;

        SwitchToThisWindow((IntPtr)genshinWindow, true);
    }

    private static bool IsWindowFocused(IntPtr windowPtr)
    {
        var hWnd = GetForegroundWindow();
        return hWnd.Equals(windowPtr);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    private static IntPtr? FindWindowByProcessName(string? processName)
    {
        // Discover the actual running game; no installation-path picker is needed.
        foreach (var name in new[] { "GenshinImpact", "YuanShen" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try { if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle; }
                    catch (InvalidOperationException) { /* Game exited during enumeration. */ }
                }
            }
        }
        return null;
    }

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);
}
