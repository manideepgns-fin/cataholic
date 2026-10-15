using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Cataholic;

/// How bright the wallpaper is, patch by patch (owner 13 Oct 2026: "understand the wallpaper"): a tiny 64 × 40 grey map
/// of the screen as the Desktop shows it.
sealed class LumaMap
{
    public const int W = 64, H = 40;
    readonly byte[] v;
    public LumaMap(byte[] v) { this.v = v; }
    /// min / max / mean brightness 0…1 — for the trace: a map with min = max would mean the picture never got read.
    public string Stats() => $"luma min={v.Min() / 255.0:0.00} max={v.Max() / 255.0:0.00} mean={v.Average(b => (double)b) / 255:0.00}";
    /// Brightness 0…1 at a point given as fractions of the screen (0,0 = top-left).
    public double At(double fx, double fy)
    {
        int x = Math.Min(W - 1, Math.Max(0, (int)(fx * W))), y = Math.Min(H - 1, Math.Max(0, (int)(fy * H)));
        return v[y * W + x] / 255.0;
    }
}

/// Read from the wallpaper FILE (no permission needed; the live screen would need a screen capture), drawn the way the
/// Desktop fills the screen (Settings → Personalization → Background → "Choose a fit": Fill / Fit / Stretch / Tile /
/// Center / Span; the registry has it as WallpaperStyle + TileWallpaper) into the grey map. Cached per screen + file + fit.
/// A solid-colour background is that colour. No readable file and no colour → null, and the lines fall back to light-on-dark.
/// One wallpaper for every display (per-monitor wallpapers would need IDesktopWallpaper COM).
static class Wallpaper
{
    static readonly Dictionary<string, LumaMap> cache = new();
    /// For the trace / report: what was resolved the last time.
    public static string Info = "";
    static string error = "";

    public static LumaMap? Map(Win32.RECT mon)
    {
        string? path = FilePath();
        string desktop = @"HKEY_CURRENT_USER\Control Panel\Desktop";
        string style = Registry.GetValue(desktop, "WallpaperStyle", "10") as string ?? "10", tile = Registry.GetValue(desktop, "TileWallpaper", "0") as string ?? "0";
        var bg = Background();
        DateTime stamp = path == null ? default : File.GetLastWriteTimeUtc(path);
        string key = $"{mon.Left},{mon.Top},{mon.Right},{mon.Bottom}|{path}|{stamp.Ticks}|{style}|{tile}|{bg}";
        if (cache.TryGetValue(key, out var m)) return m;
        error = "";
        var img = path == null ? null : Image(path, mon, style, tile == "1" && style == "0", bg);
        m = img ?? Solid(bg);
        Info = $"wallpaper={(path ?? "none")} style={style} tile={tile} bg={bg} → {(img != null ? "image" : path == null ? "solid colour (no file)" : "solid colour (picture unreadable: " + error + ")")} {m.Stats()}";
        if (cache.Count > 8) cache.Clear();
        cache[key] = m;
        return m;
    }

    /// The current wallpaper's file: what Windows reports, else the transcoded copy a slideshow / theme keeps. null: none (solid colour).
    static string? FilePath()
    {
        var sb = new StringBuilder(520);
        string? p = GuideNative.SystemParametersInfo(0x73 /* SPI_GETDESKWALLPAPER */, (uint)sb.Capacity, sb, 0) ? sb.ToString() : null;
        if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
        string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
        return File.Exists(t) ? t : null;
    }

    static Color Background()
    {
        try
        {
            var parts = (Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Colors", "Background", "0 0 0") as string ?? "0 0 0").Split(' ').Select(byte.Parse).ToArray();
            if (parts.Length == 3) return Color.FromRgb(parts[0], parts[1], parts[2]);
        }
        catch { }
        return Colors.Black;
    }

    static LumaMap Solid(Color c)
    {
        var map = new byte[LumaMap.W * LumaMap.H];
        Array.Fill(map, Luma(c.R, c.G, c.B));
        return new LumaMap(map);
    }
    static byte Luma(byte r, byte g, byte b) => (byte)Math.Round(0.299 * r + 0.587 * g + 0.114 * b);

    static LumaMap? Image(string path, Win32.RECT mon, string style, bool tiled, Color bg)
    {
        try
        {
            // the picture's real size from its header, then a small decode (the map is 64 × 40; no need for 4K)
            int iw, ih;
            using (var probe = File.OpenRead(path)) { var f = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None); iw = f.PixelWidth; ih = f.PixelHeight; }
            var img = new BitmapImage();
            using (var fs = File.OpenRead(path))
            {
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = Math.Min(iw, 640); img.StreamSource = fs; img.EndInit();
            }
            img.Freeze();

            double sw = mon.Right - mon.Left, sh = mon.Bottom - mon.Top;
            double kx = LumaMap.W / sw, ky = LumaMap.H / sh;                        // screen pixels → map cells
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.Fant);
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, LumaMap.W, LumaMap.H));
                if (tiled)
                {
                    // Tile: the picture at its own size, repeated from the top-left corner
                    var brush = new ImageBrush(img) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, iw * kx, ih * ky) };
                    dc.DrawRectangle(brush, null, new Rect(0, 0, LumaMap.W, LumaMap.H));
                }
                else
                {
                    // where the picture lands on this display, in screen pixels relative to its top-left
                    if (style == "22")                                              // Span: one picture filling the bounding box of all displays
                    {
                        var all = GuideNative.Monitors();
                        double l = all.Min(a => a.Px.Left), t = all.Min(a => a.Px.Top), r = all.Max(a => a.Px.Right), b = all.Max(a => a.Px.Bottom);
                        double vw = r - l, vh = b - t;
                        double sc = Math.Max(vw / iw, vh / ih), dw = iw * sc, dh = ih * sc;
                        Draw(dc, img, l + (vw - dw) / 2 - mon.Left, t + (vh - dh) / 2 - mon.Top, dw, dh, kx, ky);
                    }
                    else
                    {
                        double sc = style switch
                        {
                            "6" => Math.Min(sw / iw, sh / ih),                      // Fit: all of it, bars of the background colour
                            "0" => 1,                                               // Center: its own size
                            "2" => 0,                                               // Stretch: the screen's shape (below)
                            _ => Math.Max(sw / iw, sh / ih),                        // Fill (10, and anything else): aspect-fill, centred — the Mac's default
                        };
                        if (style == "2") Draw(dc, img, 0, 0, sw, sh, kx, ky);
                        else Draw(dc, img, (sw - iw * sc) / 2, (sh - ih * sc) / 2, iw * sc, ih * sc, kx, ky);
                    }
                }
            }
            var rtb = new RenderTargetBitmap(LumaMap.W, LumaMap.H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var px = new byte[LumaMap.W * LumaMap.H * 4];
            rtb.CopyPixels(px, LumaMap.W * 4, 0);
            var map = new byte[LumaMap.W * LumaMap.H];
            for (int i = 0; i < map.Length; i++) map[i] = Luma(px[i * 4 + 2], px[i * 4 + 1], px[i * 4]);
            return new LumaMap(map);
        }
        catch (Exception e) { error = e.GetType().Name; return null; }             // an unreadable picture: a solid map of the background colour
    }

    static void Draw(DrawingContext dc, BitmapSource img, double x, double y, double w, double h, double kx, double ky) =>
        dc.DrawImage(img, new Rect(x * kx, y * ky, w * kx, h * ky));
}
