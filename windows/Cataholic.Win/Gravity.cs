using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;

namespace Cataholic;

// CatGravity — what the cat can stand on (owner 27 Sep 2026, step 2 of "make it lively": "gravity"). Floors are the
// Dock line (here: the top of the taskbar = the bottom of the work area) and the TOP EDGES of the ordinary app windows
// on screen, from EnumWindows (front to back). Coordinates: screen DIPs, y DOWN from the top of the primary monitor.
static class CatGravity
{
    /// Near the top of the screen the cat HANGS on its pin (the old home); anywhere lower it stands on a floor.
    public const double HangZone = 110;
    /// The feet sit this far below the character box's top (feet ≈ y 49 of the 62-pt box).
    public static double Feet => 49 * CatLayout.Zoom;
    public const double G = 2600;             // pt/s² — snappy, cartoon gravity

    public readonly record struct Floor(double Y, double X0, double X1, int? Window);
    public readonly record struct Win(int Id, Rect Rect);

    static readonly uint Me = (uint)Process.GetCurrentProcess().Id;
    // the desktop and the taskbar are "windows" to EnumWindows — and screen-wide ones; never floors, never full-screen apps
    static readonly HashSet<string> Shell = new() { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
    static readonly Win32.EnumWindowsProc Cb = Visit;       // kept in a static: the GC must not collect it mid-enumeration
    static List<Win> acc = new();

    /// Every ordinary window on screen, FRONT to BACK (the order EnumWindows returns), in DIPs.
    public static List<Win> Windows()
    {
        Display.Refresh();                                   // the taskbar may have moved, hidden or changed size since last time
        acc = new();
        Win32.EnumWindows(Cb, IntPtr.Zero);
        return acc;
    }
    static bool Visit(IntPtr h, IntPtr _)
    {
        if (!Win32.IsWindowVisible(h) || Win32.IsIconic(h) || Win32.Cloaked(h)) return true;
        if ((Win32.ExStyle(h) & Win32.WS_EX_TOOLWINDOW) != 0) return true;
        Win32.GetWindowThreadProcessId(h, out uint pid);
        if (pid == Me || Shell.Contains(Win32.ClassName(h))) return true;
        if (!Win32.FrameBounds(h, out var rc) || rc.Right <= rc.Left || rc.Bottom <= rc.Top) return true;
        var r = Display.ToDip(rc);
        if (r.Width >= 160 && r.Height >= 80) acc.Add(new Win((int)h.ToInt64(), r));
        return true;
    }

    /// The Dock line: the top of the taskbar, across the whole display.
    public static Floor Ground() => new(Display.Visible.Bottom, Display.Frame.Left, Display.Frame.Right, null);

    /// Where she stands on a window: its top edge — or, when that edge is too high for her head to clear the top of the
    /// screen (a maximised window's top IS the screen's top), the line where her box top meets the top bar, so she walks
    /// along the top of the window just under it (owner 13 Oct 2026). null: the window is too short to stand on.
    public static double? Ledge(Rect r)
    {
        double y = Math.Max(r.Top, Math.Max(Display.Visible.Top, CatMove.MinTop) + Feet);
        return y < r.Bottom - 40 ? y : null;
    }

    /// The highest floor under `x` whose surface is at or below `fromY` (y-down). A window top counts only where you can
    /// SEE it: no window in front covers the edge or the spot the cat would sit in.
    public static Floor FloorBelow(double x, double fromY, List<Win> wins)
    {
        var best = Ground();
        for (int i = 0; i < wins.Count; i++)
        {
            var w = wins[i];
            if (Ledge(w.Rect) is not double top || top < fromY - 0.5 || top >= best.Y || x < w.Rect.Left + 6 || x > w.Rect.Right - 6) continue;
            bool hidden = false;
            for (int j = 0; j < i && !hidden; j++)
                hidden = Contains(wins[j].Rect, x, top + 1) || Contains(wins[j].Rect, x, top - 6);
            if (!hidden) best = new Floor(top, w.Rect.Left, w.Rect.Right, w.Id);
        }
        return best;
    }
    // CGRect.contains is half-open: min inclusive, max exclusive
    static bool Contains(Rect r, double x, double y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    /// Is something holding the cat up right now (feet at `feetY`, centre `x`)? ±3 pt.
    public static bool Supported(double x, double feetY, List<Win> wins) =>
        Math.Abs(FloorBelow(x, feetY - 3, wins).Y - feetY) <= 3;
}
