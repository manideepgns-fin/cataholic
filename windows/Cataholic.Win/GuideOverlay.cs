using System;
using System.Collections.Generic;
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

    delegate bool MonitorEnumProc(IntPtr m, IntPtr dc, ref Win32.RECT r, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Win32.POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref Win32.MONITORINFO i);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint param, StringBuilder buf, uint winIni);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    /// "Show window contents while dragging" — off, and a window's rect only changes when you let go (nothing to draw).
    public static bool FullWindowDrag => SystemParametersInfo(0x26 /* SPI_GETDRAGFULLWINDOWS */, 0, out int v, 0) && v != 0;

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

/// One click-through, see-through window over the screen being used; it only draws lines. A bare layered window
/// (WS_EX_LAYERED | TRANSPARENT | NOACTIVATE | TOOLWINDOW, a STATIC control's class) that we fill ourselves: the picture is
/// rendered in WPF, copied into a premultiplied DIB and handed to UpdateLayeredWindow — that call is also the fade (a
/// constant alpha), so a fade costs a blit, not a redraw. Nothing exists on screen between drags.
sealed class GuideOverlay
{
    IntPtr hwnd, memDc, dib, oldBmp, bits;
    int w, h;
    GuideNative.Mon mon;
    double alpha;                       // 0…1: how far faded in
    bool visible;
    Later? fade;
    RenderTargetBitmap? rtb;
    LumaMap? luma;
    int lumaDrag = -1; IntPtr lumaMon;

    /// What the picture is made of.
    HashSet<Guide> guides = new(), lit = new();
    Rect moving;                        // the dragged window, in this display's DIPs

    public void Show(GuideNative.Mon m, HashSet<Guide> guidesNow, HashSet<Guide> litNow, Rect movingNow)
    {
        fade?.Cancel();
        if (!Ensure(m)) return;
        guides = guidesNow; lit = litNow; moving = movingNow;
        if (lumaDrag != WindowGuides.Drags || lumaMon != m.Handle) { luma = Wallpaper.Map(m.Px); lumaDrag = WindowGuides.Drags; lumaMon = m.Handle; }   // once per drag, not per frame
        Render();
        if (!visible)
        {
            alpha = 0; Apply();
            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | 0x40 /* SWP_SHOWWINDOW */);
            visible = true;
        }
        else Apply();
        if (alpha < 1) Ramp(1, 0.15, null);
    }

    /// Mac: 0.4 s after you let go, a 0.3 s fade, then out of the way. Reduce Motion: no fade, just gone.
    public void FadeOut()
    {
        if (!visible) return;
        fade?.Cancel();
        fade = Later.After(0.4, () => { if (Fx.Reduced) Hide(); else Ramp(0, 0.3, Hide); });
    }

    public void Hide()
    {
        fade?.Cancel();
        if (!visible) return;
        visible = false; alpha = 0;
        GuideNative.ShowWindow(hwnd, 0);
    }

    void Ramp(double to, double secs, Action? done)
    {
        fade?.Cancel();
        if (Fx.Reduced) { alpha = to; Apply(); done?.Invoke(); return; }
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
        if (hwnd != IntPtr.Zero) { GuideNative.DestroyWindow(hwnd); hwnd = IntPtr.Zero; }
        FreeBitmap();
        visible = false;
    }

    // ── the window and its bitmap ──
    bool Ensure(GuideNative.Mon m)
    {
        int nw = m.Px.Right - m.Px.Left, nh = m.Px.Bottom - m.Px.Top;
        if (nw <= 0 || nh <= 0) return false;
        if (hwnd == IntPtr.Zero)
        {
            int ex = Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | 0x8 /* WS_EX_TOPMOST */;
            hwnd = GuideNative.CreateWindowEx(ex, "Static", null, 0x80000000 /* WS_POPUP */, m.Px.Left, m.Px.Top, nw, nh, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return false;
        }
        if (dib == IntPtr.Zero || nw != w || nh != h || m.Scale != mon.Scale)
        {
            FreeBitmap();
            var screen = GuideNative.GetDC(IntPtr.Zero);
            memDc = GuideNative.CreateCompatibleDC(screen);
            GuideNative.ReleaseDC(IntPtr.Zero, screen);
            var bmi = new GuideNative.BITMAPINFOHEADER { biSize = Marshal.SizeOf<GuideNative.BITMAPINFOHEADER>(), biWidth = nw, biHeight = -nh, biPlanes = 1, biBitCount = 32 };
            dib = GuideNative.CreateDIBSection(memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) return false;
            oldBmp = GuideNative.SelectObject(memDc, dib);
            rtb = new RenderTargetBitmap(nw, nh, 96 * m.Scale, 96 * m.Scale, PixelFormats.Pbgra32);
            w = nw; h = nh;
        }
        mon = m;
        return true;
    }
    void FreeBitmap()
    {
        if (memDc != IntPtr.Zero && oldBmp != IntPtr.Zero) GuideNative.SelectObject(memDc, oldBmp);
        if (dib != IntPtr.Zero) GuideNative.DeleteObject(dib);
        if (memDc != IntPtr.Zero) GuideNative.DeleteDC(memDc);
        dib = memDc = oldBmp = bits = IntPtr.Zero; rtb = null;
    }

    /// Put the bitmap on screen at the current fade.
    void Apply()
    {
        if (hwnd == IntPtr.Zero || dib == IntPtr.Zero) return;
        var pos = new Win32.POINT { X = mon.Px.Left, Y = mon.Px.Top };
        var size = new GuideNative.SIZE { cx = w, cy = h };
        var from = new Win32.POINT();
        var blend = new GuideNative.BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = (byte)Math.Round(alpha * 255), AlphaFormat = 1 /* AC_SRC_ALPHA: premultiplied */ };
        GuideNative.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref pos, ref size, memDc, ref from, 0, ref blend, 2 /* ULW_ALPHA */);
    }

    void Render()
    {
        if (rtb == null) return;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) Draw(dc);
        rtb.Clear();
        rtb.Render(dv);
        rtb.CopyPixels(Int32Rect.Empty, bits, w * h * 4, w * 4);
    }

    // ── the look (GuideView) ──
    // (owner 13 Oct 2026: "better styled grid lines" → "understand the wallpaper"): hairline grid with a dot at each
    // crossing and brighter centre lines with a small crosshair, all INKED BY THE WALLPAPER under them — dark ink over
    // light patches, light ink over dark ones, changing along a line as the wallpaper changes, each with a faint opposite
    // twin so it still reads over a window. Neighbour edges are a calm blue dash; an aligned line glows mint green.
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

    void Draw(DrawingContext dc)
    {
        double hair = 1 / mon.Scale;
        // The window's shadow on all four sides (owner 13 Oct 2026: "a greyed out movement shower on all four sides, very
        // light"): a faint band at the window's height across the screen and one at its width down the screen.
        var r = moving;
        var band = Paint(Color.FromArgb(20, 128, 128, 128));                         // white 0.5 @ 0.08
        foreach (var (ax, ay, aw, ah) in new[] { (0, r.Top, r.Left, r.Height), (r.Right, r.Top, W - r.Right, r.Height),
                                                 (r.Left, 0, r.Width, r.Top), (r.Left, r.Bottom, r.Width, H - r.Bottom) })
            if (aw > 0 && ah > 0) dc.DrawRectangle(band, null, new Rect(ax, ay, aw, ah));

        foreach (var g in guides) if (!g.Smart && !lit.Contains(g)) Inked(dc, g, g.Major ? 1 : hair, g.Major ? 2 : 1);
        // a dot where two grid lines cross
        var xs = new List<double>(); var ys = new List<double>();
        foreach (var g in guides) if (!g.Smart) (g.Vertical ? xs : ys).Add(g.At);
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
        foreach (var g in guides) if (g.Smart && !lit.Contains(g)) Solid(dc, g, Blue, 1, dashed: true);
        foreach (var g in lit) Solid(dc, g, Mint, 1.5, glow: true);
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

    void Solid(DrawingContext dc, Guide g, Color color, double width, bool dashed = false, bool glow = false)
    {
        double a = Snap(g.At), len = g.Vertical ? H : W;
        LineGeometry Line(double o) => g.Vertical ? new LineGeometry(new Point(a + o, 0), new Point(a + o, len)) : new LineGeometry(new Point(0, a + o), new Point(len, a + o));
        Pen P(Color c, double thick) => new(Paint(c), thick) { DashStyle = dashed ? new DashStyle(new[] { 4.0, 4.0 }, 0) : DashStyles.Solid };
        dc.DrawGeometry(null, P(Alpha(Colors.Black, 0.25), width), Line(width + width / 2));
        // shadowBlurRadius 8 in colour @ 0.9 → three widening strokes at falling opacity (an effect on a full-screen layered window would be software too)
        if (glow) foreach (var (gw, ga) in new[] { (11.0, 0.07), (7.0, 0.13), (4.0, 0.25) }) dc.DrawGeometry(null, P(Alpha(color, ga), gw), Line(width / 2));
        dc.DrawGeometry(null, P(color, width), Line(width / 2));
    }
}
