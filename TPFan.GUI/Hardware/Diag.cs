using System;
using System.IO;

namespace TPFan.GUI.Hardware;

/// <summary>
/// Central diagnostic logger that writes to the same <c>gui.log</c> the
/// app already uses (via <c>App.Log</c>). <see cref="Console.WriteLine"/>
/// and <see cref="System.Diagnostics.Debug.WriteLine"/> are useless in a
/// published WPF app — there is no console and usually no debugger
/// attached, so their output is silently discarded. Routing sensor and
/// poll diagnostics through this class makes them visible in the file the
/// user is already asked to check.
/// </summary>
public static class Diag
{
    /// <summary>
    /// Writes <paramref name="message"/> to <c>gui.log</c> (best effort).
    /// Falls back to <see cref="Console.WriteLine"/> if the App facade
    /// isn't reachable. Never throws.
    /// </summary>
    public static void Log(string message)
    {
        try { TPFan.GUI.App.Log(message); }
        catch { /* best effort */ }
        try { Console.WriteLine(message); }
        catch { /* best effort */ }
    }
}
