using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Guide = Cataholic.WindowGuides.Guide;

namespace Cataholic;

/// The few Win32 calls the window grid needs that the cat does not (the rest of the app's P/Invokes live in Win32.cs;
/// these stay here so the grid merges without touching that file).
static class GuideNative
{
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;

    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int ex, string cls, string? name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    /// Same call with no bitmap and no position: only the blend changes (a fade costs no upload).
    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
    public static extern bool UpdateLayeredWindowBlend(IntPtr h, IntPtr dst, IntPtr pos, IntPtr size, IntPtr src, IntPtr from, uint key, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref Win32.POINT pos, ref SIZE size, IntPtr src, ref Win32.POINT from, uint key, ref BLENDFUNCTION blend, uint flags);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint colorRef);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public IntPtr hIconSm;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);

    /// A window class that is nothing but a colour (WM_ERASEBKGND paints the brush; the window procedure is the system's own),
    /// for the shadow bands: with one constant alpha they need no bitmap at all.
    public static string? BandClass()
    {
        const string name = "CataholicGuideBand";
        if (bandClass) return name;
        var c = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), style = 3 /* CS_HREDRAW | CS_VREDRAW */,
            lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
            hInstance = GetModuleHandle(null), hbrBackground = CreateSolidBrush(0x808080), lpszClassName = name,
        };
        bandClass = RegisterClassEx(ref c) != 0;
        return bandClass ? name : null;
    }
    static bool bandClass;

    /// A click-through, topmost, never-activated layered popup of class `cls` (TOOLWINDOW: the cat's gravity must not take it for a window).
    public static IntPtr NewWindow(string cls)
    {
        int ex = Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | 0x8 /* WS_EX_TOPMOST */;
        return CreateWindowEx(ex, cls, null, 0x80000000 /* WS_POPUP */, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
    }

    delegate bool MonitorEnumProc(IntPtr m, IntPtr dc, ref Win32.RECT r, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Win32.POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref Win32.MONITORINFO i);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, StringBuilder buf, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    /// "Show window contents while dragging" — off, and a window's rect only changes when you let go (nothing to draw).
    public static bool FullWindowDrag => SystemParametersInfo(0x26 /* SPI_GETDRAGFULLWINDOWS */, 0, out int v, 0) && v != 0;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);
    /// Test scenarios only: turn "Show window contents while dragging" on or off for this session (not saved).
    public static void SetFullWindowDrag(bool on) => SystemParametersInfo(0x25 /* SPI_SETDRAGFULLWINDOWS */, on ? 1u : 0u, IntPtr.Zero, 0);

    /// A display: its pixel rect and its DPI scale (1 = 96 dpi).
    public readonly record struct Mon(IntPtr Handle, Win32.RECT Px, double Scale);

    public static List<Mon> Monitors()
    {
        var list = new List<Mon>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref Win32.RECT r, IntPtr d) => { list.Add(Info(m, r)); return true; }, IntPtr.Zero);
        return list;
    }
    static Mon Info(IntPtr m, Win32.RECT r)
    {
        double scale = GetDpiForMonitor(m, 0, out uint dx, out _) == 0 && dx > 0 ? dx / 96.0 : 1;
        return new Mon(m, r, scale);
    }
    /// The display the middle of this window is on (or the nearest).
    public static Mon MonitorOf(Win32.RECT w)
    {
        var m = MonitorFromPoint(new Win32.POINT { X = (w.Left + w.Right) / 2, Y = (w.Top + w.Bottom) / 2 }, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        return GetMonitorInfo(m, ref mi) ? Info(m, mi.rcMonitor) : new Mon(m, new Win32.RECT { Right = 1024, Bottom = 768 }, 1);
    }
}

/// A bitmap in memory (a DIB section) — what UpdateLayeredWindow copies from. Painted by WPF once, then only ever copied.
sealed class Pic
{
    public readonly IntPtr Dc;
    public readonly int W, H;
    readonly IntPtr dib, old;
    Pic(IntPtr dc, IntPtr dib, IntPtr old, int w, int h) { Dc = dc; this.dib = dib; this.old = old; W = w; H = h; }

    /// `w` × `h` pixels at `scale` (DPI / 96); `draw` speaks DIPs.
    public static Pic? Paint(int w, int h, double scale, Action<DrawingContext> draw)
    {
        if (w <= 0 || h <= 0) return null;
        var screen = GuideNative.GetDC(IntPtr.Zero);
        var dc = GuideNative.CreateCompatibleDC(screen);
        GuideNative.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new GuideNative.BITMAPINFOHEADER { biSize = Marshal.SizeOf<GuideNative.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dib = GuideNative.CreateDIBSection(dc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero) { GuideNative.DeleteDC(dc); return null; }
        var old = GuideNative.SelectObject(dc, dib);
        var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var c = dv.RenderOpen()) draw(c);
        rtb.Render(dv);
        rtb.CopyPixels(Int32Rect.Empty, bits, w * h * 4, w * 4);
        return new Pic(dc, dib, old, w, h);
    }

    public void Free()
    {
        GuideNative.SelectObject(Dc, old);
        GuideNative.DeleteObject(dib);
        GuideNative.DeleteDC(Dc);
    }
}

/// One of the overlay's windows and where it is now. Moving it is SetWindowPos; its picture changes only when `Key` does.
sealed class Piece
{
    public IntPtr Hwnd;
    public bool Shown;
    public int X, Y, W, H;                // pixels, on the desktop
    public string Key = "";               // what picture is in the window now
    public Pic? Pic;                      // …and where it came from (to put it back if a blend-only update is refused)
}

/// The grid while a window is dragged on one display — drawn ONCE per drag, then only pieces are moved
/// (owner 14 Oct 2026: "the grid must not repaint the whole screen during a drag" — a screen-sized layered window is
/// software-composited, so every redraw costs the screen's pixels: a 4K display would take several cores; same cause as the herd).
///   • grid  — one click-through layered window the size of the display: squares, centre crosshair, dots, inked by the
///             wallpaper. Rendered when the drag first reaches this display; after that only its constant alpha changes (the fade).
///   • bands — four plain-colour windows (white 0.5 at 0.08) around the dragged window; SetLayeredWindowAttributes alpha,
///             no bitmap; resized every frame.
///   • blue dashes / mint glows — a small pool of thin windows, each holding one line's picture (rendered once per
///             length and shared by every window of that kind); moved, shown and hidden as the lines come and go.
/// All of them are WS_EX_LAYERED | TRANSPARENT | NOACTIVATE | TOOLWINDOW | TOPMOST (TOOLWINDOW: the cat's gravity must not
/// take them for windows). Nothing exists on screen between drags.
sealed class GuideOverlay
{
    /// For the trace: full-screen uploads (the grid's picture), thin-window uploads, blend-only updates (fades), window
    /// moves/resizes, and blend-only updates the system refused (then the picture is uploaded again).
    public static int Renders, StripPics, Blends, Moves, Refused;
    /// …and what the once-per-drag setup took (ms): reading the wallpaper, painting the grid.
    public static double LumaMs, PaintMs;

    GuideNative.Mon mon;
    int w, h;
    double alpha;                       // 0…1: how far faded in
    double target;                      // …and where it is heading (1 during a drag, 0 once let go)
    Later? fade;
    LumaMap? luma;
    int lumaDrag = -1, gridDrag = -1; IntPtr lumaMon;

    readonly Piece grid = new();
    Pic? gridPic;
    readonly Piece[] bands = { new(), new(), new(), new() };
    bool noBands;
    readonly Dictionary<string, Pic> pics = new();            // the thin pictures, by key
    sealed class Pool { public readonly List<Piece> Free = new(); public readonly Dictionary<Guide, Piece> Used = new(); }
    readonly Pool blue = new(), mint = new();

    IEnumerable<Piece> Every() => new[] { grid }.Concat(bands).Concat(blue.Free).Concat(blue.Used.Values).Concat(mint.Free).Concat(mint.Used.Values);

    /// The drag is on this display: make sure the picture is there for it and the pieces are where they belong.
    public void Update(GuideNative.Mon m, HashSet<Guide> guides, HashSet<Guide> lit, Rect moving)
    {
        if (target != 1) fade?.Cancel();                   // a fade-out from the last drag, or its wait
        if (!Ensure(m)) return;
        if (!grid.Shown) alpha = 0;
        if (gridPic == null || gridDrag != WindowGuides.Drags)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (lumaDrag != WindowGuides.Drags || lumaMon != m.Handle) { luma = Wallpaper.Map(m.Px); lumaDrag = WindowGuides.Drags; lumaMon = m.Handle; }
            LumaMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            FreeGrid();
            gridPic = Pic.Paint(w, h, mon.Scale, dc => DrawGrid(dc, guides));
            PaintMs = sw.Elapsed.TotalMilliseconds;
            gridDrag = WindowGuides.Drags;
            if (gridPic == null) return;
            Upload(grid, gridPic, mon.Px.Left, mon.Px.Top);
        }
        else if (!grid.Shown) Blend(grid);                 // back on this display within the same drag: the same picture, from nothing
        Place(grid, mon.Px.Left, mon.Px.Top, w, h, IntPtr.Zero);

        Bands(moving);
        Sync(blue, guides.Where(g => g.Smart && !lit.Contains(g)).ToList(), glow: false);
        Sync(mint, lit.ToList(), glow: true);

        if (target != 1) { target = 1; Ramp(1, 0.15, null); }
    }

    /// Mac: 0.4 s after you let go, a 0.3 s fade, then out of the way. Reduce Motion: no fade, just gone.
    public void FadeOut()
    {
        if (!grid.Shown) return;
        fade?.Cancel();
        target = 0;
        fade = Later.After(0.4, () => { if (Fx.Reduced) Hide(); else Ramp(0, 0.3, Hide); });
    }

    public void Hide()
    {
        fade?.Cancel();
        target = 0; alpha = 0;
        foreach (var p in Every()) HidePiece(p);
        foreach (var pool in new[] { blue, mint }) { pool.Free.AddRange(pool.Used.Values); pool.Used.Clear(); }
    }

    void Ramp(double to, double secs, Action? done)
    {
        fade?.Cancel();
        if (Fx.Reduced || alpha == to) { alpha = to; Apply(); done?.Invoke(); return; }
        double from = alpha; var t0 = DateTime.Now;
        fade = Later.Every(1.0 / 30, t =>
        {
            double u = Math.Min((DateTime.Now - t0).TotalSeconds / secs, 1);
            alpha = from + (to - from) * u;
            Apply();
            if (u >= 1) { t.Cancel(); done?.Invoke(); }
        });
    }

    public void Dispose()
    {
        fade?.Cancel();
        foreach (var p in Every()) if (p.Hwnd != IntPtr.Zero) { GuideNative.DestroyWindow(p.Hwnd); p.Hwnd = IntPtr.Zero; p.Shown = false; }
        blue.Free.Clear(); blue.Used.Clear(); mint.Free.Clear(); mint.Used.Clear();
        FreeGrid();
        FreePics();
    }

    // ── the windows ──
    bool Ensure(GuideNative.Mon m)
    {
        int nw = m.Px.Right - m.Px.Left, nh = m.Px.Bottom - m.Px.Top;
        if (nw <= 0 || nh <= 0) return false;
        if (grid.Hwnd == IntPtr.Zero && !Make(grid, "Static")) return false;
        if (nw != w || nh != h || m.Scale != mon.Scale)             // a different display (or mode): nothing of the old picture fits
        {
            Hide();
            FreeGrid();
            FreePics();
            w = nw; h = nh;
        }
        mon = m;
        return true;
    }
    void FreeGrid() { gridPic?.Free(); gridPic = null; grid.Pic = null; }
    void FreePics()
    {
        foreach (var p in pics.Values) p.Free();
        pics.Clear();
        foreach (var p in Every()) if (p != grid) p.Pic = null;
    }
    static bool Make(Piece p, string cls)
    {
        p.Hwnd = GuideNative.NewWindow(cls);
        return p.Hwnd != IntPtr.Zero;
    }

    static GuideNative.BLENDFUNCTION Blendf(double alpha) => new() { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = (byte)Math.Round(alpha * 255), AlphaFormat = 1 /* AC_SRC_ALPHA: premultiplied */ };

    /// Give a window its picture (and place), at the current fade. The only call that copies pixels.
    void Upload(Piece p, Pic pic, int x, int y)
    {
        var pos = new Win32.POINT { X = x, Y = y };
        var size = new GuideNative.SIZE { cx = pic.W, cy = pic.H };
        var from = new Win32.POINT();
        var blend = Blendf(alpha);
        GuideNative.UpdateLayeredWindow(p.Hwnd, IntPtr.Zero, ref pos, ref size, pic.Dc, ref from, 0, ref blend, 2 /* ULW_ALPHA */);
        p.X = x; p.Y = y; p.W = pic.W; p.H = pic.H; p.Pic = pic;
        if (p == grid) Renders++; else StripPics++;
    }

    /// The fade: only the constant alpha changes — no bitmap is passed, nothing is copied.
    void Blend(Piece p)
    {
        var blend = Blendf(alpha);
        Blends++;
        if (GuideNative.UpdateLayeredWindowBlend(p.Hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, 2)) return;
        Refused++;
        if (p.Pic != null) Upload(p, p.Pic, p.X, p.Y);
    }

    void BandAlpha(Piece b) => GuideNative.SetLayeredWindowAttributes(b.Hwnd, 0, (byte)Math.Round(20 * alpha), 2 /* LWA_ALPHA */);    // white 0.5 @ 0.08 = 20/255

    /// Put the whole picture at the current fade.
    void Apply()
    {
        if (grid.Shown) Blend(grid);
        foreach (var b in bands) if (b.Shown) BandAlpha(b);
        foreach (var p in blue.Used.Values.Concat(mint.Used.Values)) if (p.Shown) Blend(p);
    }

    /// Show a window at a place (a hidden one comes in just above `below`, or on top when that is zero); a shown one is
    /// moved (and resized if it has to be) and nothing else.
    void Place(Piece p, int x, int y, int cw, int ch, IntPtr below)
    {
        bool size = p.W != cw || p.H != ch;
        if (p.Shown && !size && p.X == x && p.Y == y) return;
        uint f = Win32.SWP_NOACTIVATE | (size ? 0 : Win32.SWP_NOSIZE);
        var after = IntPtr.Zero;
        if (p.Shown) f |= Win32.SWP_NOZORDER;
        else { f |= 0x40 /* SWP_SHOWWINDOW */; after = below == IntPtr.Zero ? Win32.HWND_TOPMOST : below; }
        Win32.SetWindowPos(p.Hwnd, after, x, y, cw, ch, f);
        p.X = x; p.Y = y; p.W = cw; p.H = ch; p.Shown = true;
        Moves++;
    }
    static void HidePiece(Piece p)
    {
        if (!p.Shown) return;
        GuideNative.ShowWindow(p.Hwnd, 0);
        p.Shown = false;
    }

    // ── what moves: the bands ──
    /// The dragged window's shadow on all four sides (owner 13 Oct 2026: "a greyed out movement shower on all four sides,
    /// very light"): a faint band at the window's height across the screen and one at its width down the screen — four
    /// rectangles that do not overlap, clipped to the display. Under the grid, as before.
    void Bands(Rect r)
    {
        if (noBands) return;
        double s = mon.Scale;
        int l = (int)Math.Round(r.Left * s), t = (int)Math.Round(r.Top * s), rt = (int)Math.Round(r.Right * s), b = (int)Math.Round(r.Bottom * s);
        var rects = new[] { (0, t, l, b), (rt, t, w, b), (l, 0, rt, t), (l, b, rt, h) };
        for (int i = 0; i < 4; i++)
        {
            var (x0, y0, x1, y1) = rects[i];
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(w, x1); y1 = Math.Min(h, y1);
            if (x1 <= x0 || y1 <= y0) { HidePiece(bands[i]); continue; }
            if (bands[i].Hwnd == IntPtr.Zero)
            {
                var cls = GuideNative.BandClass();
                if (cls == null || !Make(bands[i], cls)) { noBands = true; return; }
            }
            if (!bands[i].Shown) BandAlpha(bands[i]);
            Place(bands[i], mon.Px.Left + x0, mon.Px.Top + y0, x1 - x0, y1 - y0, grid.Hwnd);
        }
    }

    // ── what comes and goes: dashes and glows ──
    /// One line's thin window: where, how big, which picture. The line is at `At` pixels across the window and runs its length.
    readonly record struct Strip(string Key, int X, int Y, int W, int H, bool Vertical, int At, int Len);

    /// null: the line is off this display.
    Strip? Geometry(Guide g, bool glow)
    {
        double sc = mon.Scale;
        bool v = g.Vertical;
        int along = v ? h : w, extent = v ? w : h;
        int p = (int)Math.Round(g.At * sc);                                          // = Snap(): one device pixel
        // dash: the line and its shadow, 2 pt across; glow: its widest stroke reaches 4.75 pt before the line and 6.25 after
        int before = glow ? (int)Math.Ceiling(5 * sc) : 0, after = (int)Math.Ceiling((glow ? 6.5 : 2) * sc);
        int lo = Math.Max(0, p - before), hi = Math.Min(extent, p + after);
        if (hi <= lo) return null;
        string key = $"{(glow ? 'g' : 'd')}{(v ? 'v' : 'h')}|{along}|{p - lo}|{hi - lo}|{sc}";
        return v ? new Strip(key, lo, 0, hi - lo, along, true, p - lo, along) : new Strip(key, 0, lo, along, hi - lo, false, p - lo, along);
    }

    void Sync(Pool pool, List<Guide> wanted, bool glow)
    {
        var specs = new Dictionary<Guide, Strip>();
        foreach (var g in wanted) if (Geometry(g, glow) is Strip s) specs[g] = s;
        foreach (var (g, p) in pool.Used.ToList())
            if (!specs.ContainsKey(g)) { HidePiece(p); pool.Used.Remove(g); pool.Free.Add(p); }
        foreach (var (g, s) in specs)
        {
            if (!pool.Used.TryGetValue(g, out var p))
            {
                int i = pool.Free.FindIndex(f => f.Key == s.Key);                    // prefer a window that already holds this picture
                if (i < 0) i = pool.Free.Count - 1;
                if (i >= 0) { p = pool.Free[i]; pool.Free.RemoveAt(i); }
                else { p = new Piece(); if (!Make(p, "Static")) continue; }
                pool.Used[g] = p;
            }
            int x = mon.Px.Left + s.X, y = mon.Px.Top + s.Y;
            if (p.Key != s.Key)
            {
                if (!pics.TryGetValue(s.Key, out var cached))
                {
                    cached = Pic.Paint(s.W, s.H, mon.Scale, dc => Solid(dc, s.Vertical, s.At / mon.Scale, s.Len / mon.Scale, glow ? Mint : Blue, glow ? 1.5 : 1, dashed: !glow, glow: glow));
                    if (cached == null) continue;
                    pics[s.Key] = cached;
                }
                Upload(p, cached, x, y);
                p.Key = s.Key;
            }
            Place(p, x, y, s.W, s.H, IntPtr.Zero);
        }
    }

    // ── the look (GuideView) ──
    // (owner 13 Oct 2026: "better styled grid lines" → "understand the wallpaper"): hairline grid with a dot at each
    // crossing and brighter centre lines with a small crosshair, all INKED BY THE WALLPAPER under them — dark ink over
    // light patches, light ink over dark ones, changing along a line as the wallpaper changes, each with a faint opposite
    // twin so it still reads over a window. Neighbour edges are a calm blue dash; an aligned line glows mint green.
    // (The inked line stays under the mint glow of a line that lights up — the grid is not repainted on landing.)
    static readonly Color Blue = Color.FromArgb(230, 89, 173, 255);                 // srgb 0.35 0.68 1.0 @ 0.9
    /// "You got it" (owner 13 Oct 2026: the orange "needs to be some other color") — a mint green that reads as
    /// correct, stays clear of the cats' orange, and glows on dark and light wallpapers alike.
    static readonly Color Mint = Color.FromArgb(255, 61, 230, 158);                 // srgb 0.24 0.90 0.62
    const double Step = 12;                                                         // a line changes ink every 12 pt at most

    static readonly Dictionary<Color, SolidColorBrush> brushes = new();
    static SolidColorBrush Paint(Color c)
    {
        if (!brushes.TryGetValue(c, out var b)) { b = new SolidColorBrush(c); b.Freeze(); brushes[c] = b; }
        return b;
    }
    static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B);

    double W => w / mon.Scale;
    double H => h / mon.Scale;
    bool Light(double x, double y) => (luma?.At(x / Math.Max(1, W), y / Math.Max(1, H)) ?? 0) > 0.55;
    /// Ink for a spot: (line, twin). `strength` 1 = grid, more = centre lines and marks.
    static (Color, Color) Ink(bool light, double strength = 1) => light
        ? (Alpha(Colors.Black, Math.Min(0.85, 0.32 * strength)), Alpha(Colors.White, 0.3))
        : (Alpha(Colors.White, Math.Min(0.95, 0.34 * strength)), Alpha(Colors.Black, 0.28));

    /// A device-pixel-aligned position, so a hairline is one crisp pixel.
    double Snap(double v) => Math.Round(v * mon.Scale) / mon.Scale;

    /// The part of the picture that never changes during a drag: the squares, the dots, the centre crosshair.
    void DrawGrid(DrawingContext dc, HashSet<Guide> all)
    {
        double hair = 1 / mon.Scale;
        var guides = all.Where(g => !g.Smart).ToList();
        foreach (var g in guides) Inked(dc, g, g.Major ? 1 : hair, g.Major ? 2 : 1);
        // a dot where two grid lines cross
        var xs = new List<double>(); var ys = new List<double>();
        foreach (var g in guides) (g.Vertical ? xs : ys).Add(g.At);
        foreach (var x in xs) foreach (var y in ys)
        {
            var (c, t) = Ink(Light(x, y), 2.6);
            dc.DrawEllipse(Paint(c), new Pen(Paint(t), 0.75), new Point(x, y), 2, 2);
        }
        double? cx = null, cy = null;
        foreach (var g in guides) if (g.Major) { if (g.Vertical) cx = g.At; else cy = g.At; }
        if (cx is double ccx && cy is double ccy)
        {
            var (fg, tw) = Ink(Light(ccx, ccy), 3);
            var plus = new GeometryGroup();
            plus.Children.Add(new LineGeometry(new Point(ccx - 9, ccy), new Point(ccx + 9, ccy)));
            plus.Children.Add(new LineGeometry(new Point(ccx, ccy - 9), new Point(ccx, ccy + 9)));
            plus.Children.Add(new EllipseGeometry(new Point(ccx, ccy), 5, 5));
            dc.DrawGeometry(null, new Pen(Paint(tw), 3.5), plus);
            dc.DrawGeometry(null, new Pen(Paint(fg), 1.5), plus);
        }
    }

    /// A grid line drawn in short pieces, each inked by the wallpaper under its middle. Neighbouring pieces with the
    /// same ink are one rectangle (the look is the same; the picture is a few dozen shapes, not thousands).
    void Inked(DrawingContext dc, Guide g, double width, double strength)
    {
        double a = Snap(g.At), len = g.Vertical ? H : W;
        double runStart = 0; bool runLight = false;
        void Flush(double end)
        {
            if (end <= runStart) return;
            var (c, t) = Ink(runLight, strength);
            // the twin sits one line-width beside the line, the line on top
            foreach (var (color, o) in new[] { (t, width), (c, 0.0) })
                dc.DrawRectangle(Paint(color), null, g.Vertical ? new Rect(a + o, runStart, width, end - runStart) : new Rect(runStart, a + o, end - runStart, width));
        }
        for (double s = 0; s < len; s += Step)
        {
            double e = Math.Min(len, s + Step), m = (s + e) / 2;
            bool light = g.Vertical ? Light(a, m) : Light(m, a);
            if (s == 0) runLight = light;
            else if (light != runLight) { Flush(s); runStart = s; runLight = light; }
        }
        Flush(len);
    }

    /// A neighbour's dash or a lit line's glow, in a thin picture of its own: the line is `a` pt across the picture and `len` pt long.
    static void Solid(DrawingContext dc, bool vertical, double a, double len, Color color, double width, bool dashed = false, bool glow = false)
    {
        LineGeometry Line(double o) => vertical ? new LineGeometry(new Point(a + o, 0), new Point(a + o, len)) : new LineGeometry(new Point(0, a + o), new Point(len, a + o));
        Pen P(Color c, double thick) => new(Paint(c), thick) { DashStyle = dashed ? new DashStyle(new[] { 4.0, 4.0 }, 0) : DashStyles.Solid };
        dc.DrawGeometry(null, P(Alpha(Colors.Black, 0.25), width), Line(width + width / 2));
        // shadowBlurRadius 8 in colour @ 0.9 → three widening strokes at falling opacity (an effect on a layered window would be software too)
        if (glow) foreach (var (gw, ga) in new[] { (11.0, 0.07), (7.0, 0.13), (4.0, 0.25) }) dc.DrawGeometry(null, P(Alpha(color, ga), gw), Line(width / 2));
        dc.DrawGeometry(null, P(color, width), Line(width / 2));
    }
}
