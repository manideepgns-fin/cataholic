using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace Cataholic;

// `--scenario grid` / `gridoutline` (window grid): the grid is on from the start (Scenario.Prepare), then Notepad is dragged by its
// title bar with REAL input — SetCursorPos + mouse_event, as the `touch` scenario does for the cat — slowly across the
// grid, and let go with its left edge on the centre line. The trace has the cat's CPU seconds (`gcpu t=…`) from 6 s to 16.5 s. The cloud test shoots: 3 s and 6 s (grid on, nothing moving:
// the CPU at rest), 9 s (mid-drag: grid + bands), 13 s (left edge on the line: mint), 17 s (released: all gone).
static class GridScenario
{
    static Win32.RECT frame;                      // Notepad's visible frame (pixels) when it was grabbed
    static Point at;                              // the pointer, in pixels

    public static void Run(bool fullDrag, Action<double, Action> when, Rect assumed, Action<string> log)
    {
        // `grid` drags with window contents showing (the LOCATIONCHANGE path); `gridoutline` with them off (the CI desktop's
        // own setting) — the window then only jumps on release, and the grid follows the pointer instead
        GuideNative.SetFullWindowDrag(fullDrag);
        double dx = 0;
        // a second window, up and to the left, so that mid-drag its right and bottom edges come within `Near` of Notepad's left and top: the blue dashes
        Process? other = null;
        when(1.0, () => { try { other = Process.Start("notepad.exe"); } catch { } });
        when(3.5, () =>
        {
            other?.Refresh();
            if (other != null && other.MainWindowHandle != IntPtr.Zero)
                Win32.SetWindowPos(other.MainWindowHandle, IntPtr.Zero, Display.Px(20), Display.Px(100), Display.Px(310), Display.Px(200), Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        });
        when(19.0, () => { try { other?.Kill(); } catch { } });
        Point grab = default;
        when(5.5, () =>
        {
            // ci-run.ps1 put Notepad at `assumed` (its window rect; the visible frame is a few pixels inside)
            var np = CatGravity.Windows().Where(w => Math.Abs(w.Rect.Left - assumed.Left) < 30 && Math.Abs(w.Rect.Top - assumed.Top) < 30)
                                         .Select(w => (int?)w.Id).FirstOrDefault();
            var h = np is int id ? new IntPtr(id) : IntPtr.Zero;
            if (h == IntPtr.Zero || !Win32.FrameBounds(h, out frame))
                frame = new Win32.RECT { Left = Display.Px(assumed.Left), Top = Display.Px(assumed.Top), Right = Display.Px(assumed.Right), Bottom = Display.Px(assumed.Bottom) };
            // grab the empty part of the title bar (Win11's Notepad has its tabs at the left and the buttons at the right)
            grab = new Point(frame.Left + 0.55 * (frame.Right - frame.Left), frame.Top + 15);
            // … and land its left edge on the centre line of the display
            Win32.PrimaryMonitor(out var mon, out _, out _);
            dx = Math.Round((mon.Left + mon.Right) / 2.0) - frame.Left;
            log($"grid notepad={(h == IntPtr.Zero ? "assumed" : "found")} frame={frame.Left},{frame.Top},{frame.Right},{frame.Bottom} grab={grab.X:0},{grab.Y:0} dx={dx:0} {WindowGuides.Trace}");
        });
        // the cat process's CPU seconds through the drag (the cloud log only has 3-4 s steps): where the cost is — setup at the first
        // movement, the glide itself, or holding still on the line
        foreach (var t in new[] { 6.0, 6.5, 7.0, 7.5, 8.0, 8.6, 9.0, 10.0, 11.0, 12.0, 13.0, 14.4, 14.9, 15.5, 16.5 })
            when(t, () => log($"gcpu t={t:0.0} {Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds:0.000}"));
        when(6.0, () => Move(grab));
        when(6.4, () => Mouse(Win32.MOUSEEVENTF_LEFTDOWN));
        when(6.6, () => Glide(new Point(grab.X + 0.4 * dx, grab.Y + 40), 2.0));      // off every line: grid + bands
        when(10.0, () => Glide(new Point(grab.X + dx, grab.Y), 2.0));                // left edge onto the centre line: mint
        when(14.5, () => Mouse(Win32.MOUSEEVENTF_LEFTUP));                           // let go: after 0.4 s + 0.3 s everything fades
        when(16.0, () =>
        {
            Win32.FrameBounds(new IntPtr(CatGravity.Windows().Where(w => Math.Abs(w.Rect.Top - assumed.Top) < 60).Select(w => w.Id).FirstOrDefault()), out var f);
            log($"grid released: notepad now {f.Left},{f.Top},{f.Right},{f.Bottom} {Wallpaper.Info} {WindowGuides.Trace}");
        });
    }

    static void Move(Point px)
    {
        at = px;
        Win32.SetCursorPos((int)Math.Round(px.X), (int)Math.Round(px.Y));
        Win32.mouse_event(1 /* MOUSEEVENTF_MOVE */, 0, 0, 0, UIntPtr.Zero);        // a real (zero) move through the input queue, so the title bar's move loop sees one
    }
    static void Mouse(uint flag) => Win32.mouse_event(flag, 0, 0, 0, UIntPtr.Zero);

    /// Slide the pointer to `to` over `secs` (30 steps a second), button held by the caller.
    static void Glide(Point to, double secs)
    {
        var from = at; var t0 = DateTime.Now;
        Later.Every(1.0 / 30, t =>
        {
            double u = Math.Min((DateTime.Now - t0).TotalSeconds / secs, 1);
            Move(new Point(from.X + (to.X - from.X) * u, from.Y + (to.Y - from.Y) * u));
            if (u >= 1) t.Cancel();
        });
    }
}
