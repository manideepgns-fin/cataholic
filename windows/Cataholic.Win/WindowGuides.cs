using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace Cataholic;

// WindowGuides — a grid while you move a window (WindowGuides.swift; owner 13 Oct 2026: "when apps are moved … it shows
// grids on the screen, once you properly place within the lines there is a small happy vibration").
// Drag any window (any app) → dashed lines fade in: a square grid fitted to that screen (centre lines stronger) always,
// plus the edges of other windows once the moving window comes within `Near` of them. An edge or the centre of the
// moving window landing on a line lights it up. (No haptic tap: Windows has no trackpad haptics API for desktop apps —
// the mint light is the whole signal.)
// No permission and no global mouse hook: WinEvents tell us when ANY window starts / ends a move (MOVESIZESTART/END)
// and, while it is being moved, where it is (LOCATIONCHANGE, hooked only for that window's thread and only for the
// length of the drag — so the resting cost is two events per drag, nothing otherwise). Window rects come from the same
// enumeration the cat's gravity uses (DWM frame bounds, never GetWindowRect). Each monitor has its own click-through
// overlay (GuideOverlay), shown only during a drag and hidden after the fade. The overlay is one screen-sized picture
// of the grid, painted ONCE per drag (a screen-sized layered window is software-composited — repainting it 30×/s cost
// ~1/3 of a core at 1024×768 and would be ~10× that at 4K), plus small windows that are only MOVED while you drag:
// four bands around the window, a thin window per blue dash, one per lit line.
// Coordinates: the grid, the guides and the moving window are all in the DIPs of the MONITOR they are on, y down from
// that monitor's top-left (mixed-DPI desktops get 220-pt squares on each).
static class WindowGuides
{
    public static bool IsOn => Prefs.Bool("windowGuides", false);
    public static void SetOn(bool on) { Prefs.Set("windowGuides", on); if (on) Start(); else Stop(); }

    public const double Near = 60;           // a neighbour's edge shows up once we're this close
    public const double Snap = 4;            // ≤ this from a line counts as "on" it
    const double MinGap = 1.0 / 30;          // move the pieces at most 30 times a second

    public readonly record struct Guide(bool Vertical, double At, bool Smart, bool Major = false);

    static readonly uint Me = (uint)Process.GetCurrentProcess().Id;
    static readonly GuideNative.WinEventProc OnEvent = Event;          // kept in statics: the GC must not collect them while hooked
    static IntPtr startEndHook, moveHook;

    static IntPtr target;                                              // the window being dragged (0: none)
    static Win32.RECT start, last;                                     // its frame (pixels) when the drag began / at the last look
    static List<Win32.RECT> others = new();                            // every other window's frame (pixels)
    static bool moving, dirty;
    static bool full;                                                  // "Show window contents while dragging" was on when this drag began
    static Win32.POINT grabbed;                                        // …and if not, the pointer then (the window only jumps on release: we follow the pointer)
    static HashSet<Guide> lit = new();
    static readonly Dictionary<IntPtr, GuideOverlay> overlays = new();
    static Later? tick;
    static string lastDrawn = "";

    /// For the trace: drags seen, location events, frames (position updates applied) and how long the last one took (ms), then the
    /// overlay's counters: gups = full-screen uploads (must be 1 per display per drag), gstrips = thin-window uploads (one per
    /// kind of line), gblends = fade updates (no pixels copied), gmoves = SetWindowPos calls.
    public static int Drags, Events, Draws, LitNow;
    public static double DrawMs, DrawMaxMs;
    public static string Trace => $"grid={(startEndHook != IntPtr.Zero ? "on" : "off")} drags={Drags} locs={Events} gdraws={Draws} gdrawMs={DrawMs:0.0} gmaxMs={DrawMaxMs:0.0} glumaMs={GuideOverlay.LumaMs:0} gpaintMs={GuideOverlay.PaintMs:0} gups={GuideOverlay.Renders} gstrips={GuideOverlay.StripPics} gblends={GuideOverlay.Blends} gmoves={GuideOverlay.Moves} grefused={GuideOverlay.Refused} lit={LitNow} fullDrag={GuideNative.FullWindowDrag}";

    public static void Start()
    {
        if (startEndHook != IntPtr.Zero) return;
        startEndHook = GuideNative.SetWinEventHook(GuideNative.EVENT_SYSTEM_MOVESIZESTART, GuideNative.EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, OnEvent,
            0, 0, GuideNative.WINEVENT_OUTOFCONTEXT | GuideNative.WINEVENT_SKIPOWNPROCESS);
    }
    public static void Stop()
    {
        if (startEndHook != IntPtr.Zero) GuideNative.UnhookWinEvent(startEndHook);
        startEndHook = IntPtr.Zero;
        End();
        foreach (var o in overlays.Values) o.Dispose();
        overlays.Clear();
    }

    static void Event(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            switch (ev)
            {
                case GuideNative.EVENT_SYSTEM_MOVESIZESTART: Begin(hwnd); break;
                case GuideNative.EVENT_SYSTEM_MOVESIZEEND: End(); break;
                // the cursor and the caret raise LOCATIONCHANGE too (idObject -9 / -8): only the window itself counts
                case GuideNative.EVENT_OBJECT_LOCATIONCHANGE when hwnd == target && idObject == 0 && idChild == 0: Events++; dirty = true; break;
            }
        }
        catch (Exception e)                                             // an exception out of a native callback would end the process: a bug in the grid must not
        {
            Scenario.Failures++;
            Console.Error.WriteLine(e);
        }
    }

    /// Every ordinary window on screen, front to back — the dragged one apart — as pixel rects (Gravity speaks DIPs of the primary monitor).
    static void Begin(IntPtr h)
    {
        End();
        uint tid = Win32.GetWindowThreadProcessId(h, out uint pid);
        if (pid == Me || !Win32.FrameBounds(h, out start)) return;
        Drags++;
        target = h; last = start; moving = false; lit = new(); lastDrawn = "";
        int self = (int)h.ToInt64();
        others = CatGravity.Windows().Where(w => w.Id != self).Select(w => Px(w.Rect)).ToList();
        full = GuideNative.FullWindowDrag;
        if (full)
            moveHook = GuideNative.SetWinEventHook(GuideNative.EVENT_OBJECT_LOCATIONCHANGE, GuideNative.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, OnEvent,
                pid, tid, GuideNative.WINEVENT_OUTOFCONTEXT | GuideNative.WINEVENT_SKIPOWNPROCESS);
        else Win32.GetCursorPos(out grabbed);
        dirty = true;                                                   // look once right away, then on every 30th of a second that the window moved (outline drag: always)
        tick = Later.Every(MinGap, _ => { if (dirty || !full) { dirty = false; Drag(); } });
    }

    static Win32.RECT Px(Rect r) => new() { Left = (int)Math.Round(r.Left * Display.Scale), Top = (int)Math.Round(r.Top * Display.Scale),
                                            Right = (int)Math.Round(r.Right * Display.Scale), Bottom = (int)Math.Round(r.Bottom * Display.Scale) };

    static void End()
    {
        if (moveHook != IntPtr.Zero) GuideNative.UnhookWinEvent(moveHook);
        moveHook = IntPtr.Zero;
        tick?.Cancel(); tick = null;
        target = IntPtr.Zero; moving = false; dirty = false; lit = new(); LitNow = 0;
        foreach (var o in overlays.Values) o.FadeOut();
    }

    static void Drag()
    {
        if (target == IntPtr.Zero) return;
        Win32.RECT r;
        if (full) { if (!Win32.FrameBounds(target, out r)) return; }
        else                                                            // outline drag: the window itself stays put until you let go, so it is where it began + how far the pointer went
        {
            Win32.GetCursorPos(out var c);
            r = start; r.Left += c.X - grabbed.X; r.Right += c.X - grabbed.X; r.Top += c.Y - grabbed.Y; r.Bottom += c.Y - grabbed.Y;
        }
        // A move keeps the size; a resize (or a window restoring itself as you pull it off the top edge) does not — that
        // starts over from the new frame — and a drag that has not left the origin shows nothing yet.
        if (r.Right - r.Left != last.Right - last.Left || r.Bottom - r.Top != last.Bottom - last.Top) { last = start = r; return; }
        last = r;
        if (r.Left == start.Left && r.Top == start.Top) return;

        var mon = GuideNative.MonitorOf(r);
        double s = mon.Scale;
        Rect Local(Win32.RECT p) => new((p.Left - mon.Px.Left) / s, (p.Top - mon.Px.Top) / s, (p.Right - p.Left) / s, (p.Bottom - p.Top) / s);
        var view = new Rect(0, 0, (mon.Px.Right - mon.Px.Left) / s, (mon.Px.Bottom - mon.Px.Top) / s);
        var win = Local(r);
        if (!moving) { moving = true; lit = new(); }

        // Always: a square grid over the whole display, sized from its current resolution.
        var guides = Squares(view);
        // As you get close: the edges of the other windows on this screen.
        double[] xs = { win.Left, win.Left + win.Width / 2, win.Right }, ys = { win.Top, win.Top + win.Height / 2, win.Bottom };
        foreach (var px in others)
        {
            var o = Local(px);
            if (!o.IntersectsWith(view)) continue;
            foreach (var x in new[] { o.Left, o.Right }) if (xs.Any(v => Math.Abs(v - x) <= Near)) guides.Add(new Guide(true, x, true));
            foreach (var y in new[] { o.Top, o.Bottom }) if (ys.Any(v => Math.Abs(v - y) <= Near)) guides.Add(new Guide(false, y, true));
        }
        var all = new HashSet<Guide>(guides);
        var on = new HashSet<Guide>(all.Where(g => (g.Vertical ? xs : ys).Any(v => Math.Abs(v - g.At) <= Snap)));
        // (Mac: a haptic tap here when a line newly lights up — `!on.subtracting(lit).isEmpty`.)
        lit = on; LitNow = on.Count;

        // nothing changed since the last frame → nothing to move
        string key = $"{mon.Handle}|{win}|{string.Join(",", on.Select(g => (g.Vertical ? "v" : "h") + g.At))}|{string.Join(",", all.Where(g => g.Smart).Select(g => (g.Vertical ? "v" : "h") + g.At))}";
        if (key == lastDrawn) return;
        lastDrawn = key;
        var sw = Stopwatch.StartNew();
        foreach (var kv in overlays) if (kv.Key != mon.Handle) kv.Value.Hide();                // the window moved to another display: that display's lines go
        if (!overlays.TryGetValue(mon.Handle, out var ov)) overlays[mon.Handle] = ov = new GuideOverlay();
        ov.Update(mon, all, on, win);
        Draws++; DrawMs = sw.Elapsed.TotalMilliseconds; DrawMaxMs = Math.Max(DrawMaxMs, DrawMs);
    }

    /// A chessboard built out from the centre (owner 13 Oct 2026: "there are centre lines and on that the squares need
    /// to be built"): the two centre lines first, then squares stepping out from them. The square is the half-width
    /// split into whole steps of about 220 pt, so the columns end exactly on the left and right edges; rows step up and
    /// down from the centre by the same amount and the outer ones are cut by the screen. Whole screen, at its current
    /// resolution: 1920 × 1080 → 240 pt squares, 1024 × 768 → 256 pt.
    public static List<Guide> Squares(Rect v)
    {
        double cx = v.Left + v.Width / 2, cy = v.Top + v.Height / 2;
        double side = (v.Width / 2) / Math.Max(1, Math.Round(v.Width / 2 / 220, MidpointRounding.AwayFromZero));
        var o = new List<Guide> { new(true, Round(cx), false, true), new(false, Round(cy), false, true) };
        for (int i = 1; i <= 40; i++)
        {
            double d = i * side;
            if (d > v.Width / 2 + 1 && d > v.Height / 2 + 1) break;
            if (d <= v.Width / 2 + 1) { o.Add(new(true, Round(cx - d), false)); o.Add(new(true, Round(cx + d), false)); }
            if (d < v.Height / 2) { o.Add(new(false, Round(cy - d), false)); o.Add(new(false, Round(cy + d), false)); }
        }
        return o;
    }
    static double Round(double x) => Math.Round(x, MidpointRounding.AwayFromZero);       // Swift's .rounded()
}
