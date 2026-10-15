using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// Find my cat — the pictures. A line-by-line port of the artwork in Sources/Cataholic/FindScenes.swift (FindBackdrop,
// FindPropView.draw, fish, FindNightLights) onto Gfx, the SwiftUI-GraphicsContext-alike of FindGfx.cs. The layout side
// (places, props, spots, fairness) is FindScenes.cs. Every coordinate, colour, width and opacity is the Swift's; the
// draw order is the Swift's too (later calls paint over earlier ones).

static class FindArt
{
    // ── the Swift file's private helpers and colours ──
    static Color C(double r, double g, double b) => Gfx.C(r, g, b);
    static Color Op(Color c, double a) => Gfx.Op(c, a);
    static Color White => Colors.White;
    static Color Ink => FindScene.Ink;
    static Point Pt(double x, double y) => new(x, y);
    static FRect R(double x, double y, double w, double h) => new(x, y, w, h);

    static readonly Color stone = C(0.88, 0.85, 0.80), stoneShade = C(0.78, 0.75, 0.70);
    static readonly Color wood = C(0.82, 0.62, 0.42), woodDark = C(0.62, 0.44, 0.30);
    static readonly Color leaf = C(0.48, 0.71, 0.44), leafLight = C(0.62, 0.82, 0.52);
    static readonly Color roofBlue = C(0.49, 0.53, 0.80);
    static readonly Color flagRed = C(0.92, 0.44, 0.40), flagGold = C(0.98, 0.80, 0.36);

    /// Swift's stride(from:to:by:) / stride(from:through:by:) over Doubles: from + n * by, never accumulated.
    static IEnumerable<double> Stride(double from, double limit, double by, bool through)
    {
        for (int n = 0; ; n++)
        {
            double v = from + n * by;
            if (through ? v > limit : v >= limit) yield break;
            yield return v;
        }
    }

    // ── the backdrop ──

    /// The fixed backdrop behind everything (FindBackdrop's Canvas body).
    public static void Backdrop(Gfx g, FindPlace place)
    {
        double W = FindScene.Width;
        switch (place)
        {
            case FindPlace.Kingdom: kingdom(g, W); break;
            case FindPlace.Harbour: harbour(g, W); break;
            case FindPlace.Snow: snow(g, W); break;
        }
    }

    static void cloud(Gfx g, double x, double y, double s, double alpha)
    {
        g.Blobs(new[] { R(x, y + 10 * s, 60 * s, 30 * s), R(x + 24 * s, y, 54 * s, 40 * s),
                        R(x + 60 * s, y + 12 * s, 50 * s, 28 * s) }, Op(White, 0.95), 1, alpha);
    }

    static void kingdom(Gfx g, double W)
    {
        // sky + sun + clouds
        g.Fill(Gfx.Box(0, 0, W, 360),
               Gfx.Linear(Pt(0, 0), Pt(0, 300), C(0.62, 0.82, 0.96), C(0.90, 0.95, 0.97)));
        g.Fill(Gfx.Ell(920, 36, 96, 96), Op(C(1, 0.93, 0.62), 0.45));
        g.Inked(Gfx.Ell(938, 54, 60, 60), C(1, 0.86, 0.48), 2, 0.5);
        foreach (var (x, y, s) in new (double, double, double)[] { (120, 70, 1), (300, 40, 0.75), (790, 92, 0.9), (1040, 170, 0.6), (210, 170, 0.55) })
        {
            cloud(g, x, y, s, 0.25);
        }
        // hills
        var far = new FPath().M(0, 262)
            .Q(330, 228, 160, 205)
            .Q(760, 236, 560, 262)
            .Q(W, 218, 950, 190)
            .L(W, 400).L(0, 400).Z();
        g.Inked(far, C(0.72, 0.85, 0.66), 2, 0.35);
        foreach (var (x, y) in new (double, double)[] { (70, 238), (128, 230), (242, 226), (860, 218), (930, 206), (1060, 218) })
        {
            g.Line(new[] { Pt(x, y + 6), Pt(x, y + 20) }, Op(woodDark, 0.7), 3);
            g.Blobs(new[] { R(x - 11, y - 12, 22, 22) }, C(0.56, 0.76, 0.50), 1, 0.4);
        }
        var near = new FPath().M(0, 300)
            .Q(420, 296, 220, 262)
            .L(700, 296)
            .Q(W, 290, 900, 256)
            .L(W, 400).L(0, 400).Z();
        g.Inked(near, C(0.64, 0.80, 0.56), 2, 0.4);
        // ground
        g.Fill(Gfx.Box(0, 340, W, 300),
               Gfx.Linear(Pt(0, 340), Pt(0, 640), C(0.74, 0.87, 0.56), C(0.66, 0.82, 0.50)));
        castle(g);
        // moat
        var moat = new FPath().M(0, 310)
            .C(W, 312, 300, 304, 800, 318)
            .L(W, 348)
            .C(0, 346, 800, 354, 300, 340)
            .Z();
        g.Inked(moat, C(0.55, 0.78, 0.91));
        foreach (var (x, y) in new (double, double)[] { (60, 326), (190, 334), (330, 322), (420, 334), (660, 326), (760, 336), (880, 322), (1000, 334) })
        {
            var w = new FPath().M(x, y)
                .Q(x + 14, y, x + 7, y - 5)
                .Q(x + 28, y, x + 21, y + 5);
            g.Stroke(w, Op(White, 0.75), 2, PenLineCap.Round);
        }
        // paths: from the bridge down to the bottom, a branch to the village and the market square
        var path = new FPath().M(522, 346)
            .C(430, 640, 510, 460, 420, 540)
            .L(600, 640)
            .C(578, 346, 560, 540, 590, 460)
            .Z();
        var sand = C(0.95, 0.88, 0.71);
        g.Inked(path, sand, 2, 0.45);
        var west = new FPath().M(520, 470)
            .Q(20, 470, 300, 440)
            .L(20, 498).Q(512, 502, 300, 472)
            .Z();
        g.Inked(west, sand, 2, 0.45);
        g.Inked(Gfx.Ell(730, 412, 370, 128), sand, 2, 0.45);
        var east = new FPath().M(560, 430)
            .Q(760, 452, 660, 420)
            .L(760, 482).Q(552, 470, 660, 452)
            .Z();
        g.Fill(east, sand);
        foreach (var (x, y) in new (double, double)[] { (560, 600), (470, 470), (520, 420), (860, 480), (980, 500) })   // cobbles
        {
            g.Stroke(Gfx.Ell(x, y, 9, 5), Op(Ink, 0.18), 1.2);
        }
        // bridge
        g.Inked(Gfx.RBox(516, 304, 68, 48, 3), wood);
        foreach (double y in Stride(312, 344, 8, true)) { g.Line(new[] { Pt(518, y), Pt(582, y) }, Op(woodDark, 0.55), 1.4); }
        foreach (double x in new double[] { 512, 582 })
        {
            g.Inked(Gfx.RBox(x, 298, 6, 56, 3), woodDark, 1.6);
        }
        // flowers in the grass
        ulong h = 7;
        for (int n = 0; n < 70; n++)
        {
            h = unchecked(h * 6364136223846793005UL + 1442695040888963407UL);
            double x = (double)((h >> 33) % 1000) / 1000 * 1080 + 10, y = (double)((h >> 13) % 1000) / 1000 * 270 + 360;
            var c = new[] { C(1, 0.72, 0.78), C(1, 0.92, 0.55), White }[(int)(h % 3)];
            foreach (var (dx, dy) in new (double, double)[] { (-2.2, 0), (2.2, 0), (0, -2.2), (0, 2.2) })
            {
                g.Fill(Gfx.Ell(x + dx - 1.8, y + dy - 1.8, 3.6, 3.6), c);
            }
            g.Fill(Gfx.Ell(x - 1.2, y - 1.2, 2.4, 2.4), C(0.95, 0.65, 0.25));
        }
        foreach (var hs in FindScene.Houses) { house(g, hs); }
        market(g);
        lampPost(g, 452, 418); lampPost(g, 640, 418);
    }

    // MARK: harbour

    static void harbour(Gfx g, double W)
    {
        g.Fill(Gfx.Box(0, 0, W, 260),
               Gfx.Linear(Pt(0, 0), Pt(0, 240), C(0.58, 0.82, 0.95), C(0.92, 0.96, 0.97)));
        g.Fill(Gfx.Ell(66, 30, 90, 90), Op(C(1, 0.93, 0.62), 0.45));
        g.Inked(Gfx.Ell(82, 46, 58, 58), C(1, 0.86, 0.48), 2, 0.5);
        foreach (var (x, y, s) in new (double, double, double)[] { (260, 60, 0.9), (560, 34, 0.7), (680, 120, 0.55), (420, 150, 0.5) })
        {
            cloud(g, x, y, s, 0.25);
        }
        foreach (var (x, y, s) in new (double, double, double)[] { (330, 96, 1), (366, 112, 0.8), (720, 70, 0.9), (612, 176, 0.7) })   // gulls
        {
            var gull = new FPath().M(x - 10 * s, y).Q(x, y, x - 5 * s, y - 7 * s)
                .Q(x + 10 * s, y, x + 5 * s, y - 7 * s);
            g.Stroke(gull, Op(Ink, 0.75), 2, PenLineCap.Round, PenLineJoin.Round);
        }
        // the sea
        g.Fill(Gfx.Box(0, 240, W, 230),
               Gfx.Linear(Pt(0, 240), Pt(0, 470), C(0.55, 0.80, 0.91), C(0.40, 0.66, 0.86)));
        g.Line(new[] { Pt(0, 240), Pt(W, 240) }, Op(Ink, 0.35), 2);
        ulong sh = 11;
        for (int n = 0; n < 26; n++)
        {
            sh = unchecked(sh * 6364136223846793005UL + 1442695040888963407UL);
            double x = (double)((sh >> 33) % 1000) / 1000 * 1060 + 10, y = (double)((sh >> 13) % 1000) / 1000 * 200 + 256;
            var w = new FPath().M(x, y)
                .Q(x + 12, y, x + 6, y - 4)
                .Q(x + 24, y, x + 18, y + 4);
            g.Stroke(w, Op(White, 0.7), 1.8, PenLineCap.Round);
        }
        // a sailboat on the horizon
        g.Inked(Gfx.RBox(200, 232, 30, 8, 4), C(0.90, 0.48, 0.38), 1.4);
        var sail = new FPath().M(214, 230).L(214, 204).L(229, 230).Z();
        g.Inked(sail, White, 1.4);
        // the headland and its lighthouse
        var land = new FPath().M(720, 252)
            .Q(900, 196, 790, 196)
            .L(W, 178).L(W, 300)
            .Q(720, 252, 900, 296).Z();
        g.Inked(land, C(0.70, 0.82, 0.58));
        foreach (var (x, y) in new (double, double)[] { (800, 252), (880, 262), (1000, 276), (1060, 262) })
        {
            g.Blobs(new[] { R(x, y, 30, 18) }, C(0.74, 0.72, 0.70), 1);
        }
        g.Inked(Gfx.RBox(994, 176, 58, 34, 2), C(0.98, 0.94, 0.86));
        var hr = new FPath().M(988, 178).L(1023, 154).L(1058, 178).Z();
        g.Inked(hr, C(0.84, 0.45, 0.36));
        g.Inked(Gfx.RBox(1014, 188, 14, 22, 2), woodDark, 1.6);
        var tower = new FPath().M(926, 214).L(936, 104)
            .L(972, 104).L(982, 214).Z();
        Geometry towerG = tower;
        g.Fill(towerG, White);
        g.Clip(towerG, () =>
        {
            foreach (double y in new double[] { 120, 160, 200 }) { g.Fill(Gfx.Box(920, y, 70, 18), flagRed); }
        });
        g.Stroke(towerG, Ink, 2.2, PenLineCap.Flat, PenLineJoin.Round);
        g.Inked(Gfx.RBox(930, 96, 48, 9, 2), C(0.40, 0.40, 0.46));
        g.Inked(Gfx.Box(940, 72, 28, 24), C(1, 0.90, 0.56));
        g.Line(new[] { Pt(954, 74), Pt(954, 94) }, Op(Ink, 0.5), 1.4);
        var cap = new FPath().M(936, 73).Q(972, 73, 954, 44).Z();
        g.Inked(cap, flagRed);
        g.Inked(Gfx.RBox(948, 172, 12, 22, 5), woodDark, 1.6);
        // the quay, the shack, the pier and the dock
        g.Inked(Gfx.Box(-4, 330, 266, 140), stone);
        foreach (var (x, y) in new (double, double)[] { (20, 452), (90, 456), (160, 450), (230, 440), (236, 400), (234, 360) })
        {
            g.Stroke(Gfx.RBox(x, y, 18, 8, 2), stoneShade, 1.4);
        }
        foreach (var hs in FindScene.Shacks) { house(g, hs); }
        g.Inked(Gfx.RBox(66, 352, 96, 18, 3), White, 1.6);
        g.TextCentered("FISH", 12, FontWeights.ExtraBold, C(0.30, 0.46, 0.70), 114, 361);
        foreach (double x in Stride(312, 384, 24, true)) { g.Inked(Gfx.Box(x, 296, 8, 170), woodDark, 1.6); }
        g.Inked(Gfx.Box(306, 300, 88, 166), wood);
        foreach (double x in Stride(318, 384, 12, true)) { g.Line(new[] { Pt(x, 302), Pt(x, 464) }, Op(woodDark, 0.45), 1.3); }
        g.Inked(Gfx.Box(-4, 462, W + 8, 190), wood);
        int k = 0;
        foreach (double y in Stride(484, 640, 22, false))
        {
            g.Line(new[] { Pt(0, y), Pt(W, y) }, Op(woodDark, 0.35), 1.4);
            foreach (double x in Stride(k % 2 == 0 ? 60 : 130, W, 140, false)) { g.Line(new[] { Pt(x, y - 22), Pt(x, y) }, Op(woodDark, 0.35), 1.4); }
            k += 1;
        }
        foreach (double x in new double[] { 150, 470, 860, 1050 })                                     // bollards on the edge
        {
            g.Inked(Gfx.RBox(x, 452, 16, 18, 5), C(0.40, 0.40, 0.46), 1.8);
        }
        foreach (var (x, y, c) in new (double, double, Color)[] { (268, 424, flagRed), (792, 300, flagGold), (560, 452, flagRed) })   // buoys
        {
            g.Inked(Gfx.Ell(x, y, 16, 14), c, 1.6);
            g.Line(new[] { Pt(x + 2, y + 7), Pt(x + 14, y + 7) }, White, 2);
        }
        stall(g, 562, 452, C(0.42, 0.62, 0.90), White);
        stall(g, 694, 452, C(0.36, 0.72, 0.66), White);
    }

    // MARK: snowy village

    static void snow(Gfx g, double W)
    {
        g.Fill(Gfx.Box(0, 0, W, 360),
               Gfx.Linear(Pt(0, 0), Pt(0, 300), C(0.74, 0.82, 0.93), C(0.94, 0.96, 0.99)));
        foreach (var (x, y, s) in new (double, double, double)[] { (140, 60, 1), (520, 40, 0.8), (860, 90, 0.9) })
        {
            cloud(g, x, y, s, 0.2);
        }
        // mountains with snowy caps
        foreach (var (x, top, w, c) in new (double, double, double, double)[] { (-40, 120, 420, 0.70), (300, 90, 460, 0.66), (700, 130, 460, 0.72) })
        {
            var m = new FPath().M(x, 300).L(x + w / 2, top).L(x + w, 300).Z();
            g.Inked(m, C(c, c + 0.06, c + 0.16), 2, 0.35);
            var cap = new FPath().M(x + w / 2 - 44, top + 50).L(x + w / 2, top)
                .L(x + w / 2 + 44, top + 50).L(x + w / 2 + 22, top + 40)
                .L(x + w / 2 + 4, top + 54).L(x + w / 2 - 18, top + 38).Z();
            g.Inked(cap, White, 1.6, 0.35);
        }
        var hills = new FPath().M(0, 300)
            .Q(520, 296, 260, 250)
            .Q(W, 286, 820, 246)
            .L(W, 400).L(0, 400).Z();
        g.Inked(hills, C(0.95, 0.97, 1.0), 2, 0.3);
        foreach (var (x, y) in new (double, double)[] { (60, 286), (96, 280), (420, 286), (600, 278), (640, 286), (910, 268), (950, 276) })
        {
            smallPine(g, x, y, 0.55);
        }
        // the ground
        g.Fill(Gfx.Box(0, 330, W, 310),
               Gfx.Linear(Pt(0, 330), Pt(0, 640), C(0.97, 0.98, 1.0), C(0.90, 0.93, 0.98)));
        foreach (var (x, y, w) in new (double, double, double)[] { (120, 560, 180), (760, 600, 220), (880, 420, 160), (300, 420, 140) })
        {
            g.Fill(Gfx.Ell(x, y, w, 22), Op(C(0.84, 0.89, 0.97), 0.7));
        }
        // the frozen pond
        g.Inked(Gfx.Ell(392, 456, 340, 112), C(0.78, 0.89, 0.97));
        g.Fill(Gfx.Ell(420, 470, 284, 82), C(0.86, 0.94, 0.99));
        foreach (var (x, y) in new (double, double)[] { (470, 486), (600, 520), (650, 480) })
        {
            g.Line(new[] { Pt(x, y + 12), Pt(x + 18, y) }, White, 3);
            g.Line(new[] { Pt(x + 10, y + 14), Pt(x + 20, y + 7) }, White, 2);
        }
        // footprints across the snow
        for (int i = 0; i < 9; i++) { g.Fill(Gfx.Ell(180 + i * 26.0, 452 + (i % 2) * 8.0, 7, 4), C(0.78, 0.84, 0.94)); }
        foreach (var hs in FindScene.Cabins) { cabin(g, hs); }
        lampPost(g, 342, 452); lampPost(g, 790, 560);
        // falling snow
        ulong h = 5;
        for (int n = 0; n < 90; n++)
        {
            h = unchecked(h * 6364136223846793005UL + 1442695040888963407UL);
            double x = (double)((h >> 33) % 1000) / 1000 * W, y = (double)((h >> 13) % 1000) / 1000 * 640, r = 1.4 + (double)(h % 3) * 0.7;
            g.Fill(Gfx.Ell(x, y, r * 2, r * 2), Op(White, 0.9));
            g.Stroke(Gfx.Ell(x, y, r * 2, r * 2), Op(C(0.70, 0.78, 0.90), 0.5), 0.6);
        }
    }

    static void smallPine(Gfx g, double x, double y, double s)
    {
        foreach (var (k, w) in new (int, double)[] { (0, 30), (1, 24), (2, 16) })
        {
            double ty = y - k * 14 * s;
            var t = new FPath().M(x - w * s, ty).L(x, ty - 26 * s).L(x + w * s, ty).Z();
            g.Inked(t, C(0.36, 0.58, 0.48), 1.4, 0.5);
        }
    }

    static void cabin(Gfx g, FindHouse h)
    {
        var w = h.Wall;
        g.Inked(Gfx.Box(w.MaxX - 34, w.MinY - h.Roof + 2, 14, h.Roof), C(0.62, 0.60, 0.62), 2);
        g.Blobs(new[] { R(w.MaxX - 37, w.MinY - h.Roof - 4, 20, 9) }, White, 1);
        g.Blobs(new[] { R(w.MaxX - 30, w.MinY - h.Roof - 22, 14, 11), R(w.MaxX - 22, w.MinY - h.Roof - 36, 18, 13) },
                C(0.88, 0.90, 0.94), 1, 0.4);
        g.Inked(Gfx.Box(w), h.WallC);
        foreach (double y in Stride(w.MinY + 10, w.MaxY, 10, false)) { g.Line(new[] { Pt(w.MinX + 2, y), Pt(w.MaxX - 2, y) }, Op(Ink, 0.3), 1.4); }
        foreach (double y in Stride(w.MinY + 5, w.MaxY, 10, false))
        {
            foreach (double x in new[] { w.MinX - 3, w.MaxX - 4 }) { g.Inked(Gfx.Ell(x, y - 3, 7, 7), C(0.86, 0.70, 0.52), 1.2); }
        }
        var roof = new FPath().M(w.MinX - 12, w.MinY + 2)
            .L(w.MidX - w.W * 0.2, w.MinY - h.Roof).L(w.MidX + w.W * 0.2, w.MinY - h.Roof)
            .L(w.MaxX + 12, w.MinY + 2).Z();
        g.Inked(roof, C(0.50, 0.34, 0.28));
        var snowCap = new FPath().M(w.MinX - 14, w.MinY - 2)
            .L(w.MidX - w.W * 0.22, w.MinY - h.Roof - 6)
            .Q(w.MidX + w.W * 0.22, w.MinY - h.Roof - 6, w.MidX, w.MinY - h.Roof - 12)
            .L(w.MaxX + 14, w.MinY - 2)
            .Q(w.MinX - 14, w.MinY - 2, w.MidX, w.MinY - h.Roof * 0.35);
        g.Inked(snowCap, White);
        for (int k = 0; k < (int)(w.W / 14); k++)                                         // icicles
        {
            double x = w.MinX - 6 + k * 14 + 6;
            var ic = new FPath().M(x - 3, w.MinY + 1).L(x, w.MinY + 8 + (k % 3) * 3).L(x + 3, w.MinY + 1);
            g.Fill(ic, C(0.88, 0.95, 1.0));
        }
        var door = new FPath().M(w.MidX - 11, w.MaxY).L(w.MidX - 11, w.MaxY - 28)
            .Q(w.MidX + 11, w.MaxY - 28, w.MidX, w.MaxY - 40)
            .L(w.MidX + 11, w.MaxY).Z();
        g.Inked(door, C(0.44, 0.30, 0.24), 2);
        foreach (double x in new[] { w.MinX + 14, w.MaxX - 34 })
        {
            g.Inked(Gfx.RBox(x, w.MinY + 14, 20, 18, 2), C(1, 0.86, 0.52), 1.8);
            g.Line(new[] { Pt(x + 10, w.MinY + 14), Pt(x + 10, w.MinY + 32) }, Op(Ink, 0.7), 1.4);
            g.Blobs(new[] { R(x - 3, w.MinY + 10, 26, 6) }, White, 0.8);
        }
    }

    static void stall(Gfx g, double x, double top, Color a, Color b)
    {
        foreach (double px in new[] { x + 4, x + 90 }) { g.Inked(Gfx.Box(px, top + 10, 6, 48), woodDark, 1.6); }
        g.Inked(Gfx.Box(x + 8, top + 16, 84, 34), C(0.98, 0.94, 0.86), 1.6);
        for (int i = 0; i < 6; i++)
        {
            double sx = x - 4 + i * 18;
            var s = new FPath().M(sx + 4, top).L(sx + 22, top).L(sx + 18, top + 16)
                .Q(sx, top + 16, sx + 9, top + 26).Z();
            g.Fill(s, i % 2 == 0 ? a : b);
        }
        var edge = new FPath().M(x, top).L(x + 104, top).L(x + 104, top + 16);
        for (int i = 5; i >= 0; i--) { double sx = x - 4 + i * 18; edge.Q(sx, top + 16, sx + 9, top + 26); }
        edge.Z();
        g.Stroke(edge, Ink, 2, PenLineCap.Flat, PenLineJoin.Round);
    }

    static void lampPost(Gfx g, double x, double y)
    {
        g.Inked(Gfx.RBox(x - 2.5, y - 50, 5, 50, 2), C(0.30, 0.30, 0.36), 1.4);
        g.Inked(Gfx.RBox(x - 6, y - 3, 12, 4, 2), C(0.30, 0.30, 0.36), 1.4);
        g.Inked(Gfx.RBox(x - 7, y - 64, 14, 16, 3), C(1, 0.90, 0.56), 1.6);
        var cap = new FPath().M(x - 9, y - 63).L(x, y - 71).L(x + 9, y - 63).Z();
        g.Inked(cap, C(0.30, 0.30, 0.36), 1.4);
    }

    static void castle(Gfx g)
    {
        void tower(double x, double y, double w, double bottom, double apex, Color flag)
        {
            g.Line(new[] { Pt(x + w / 2, apex), Pt(x + w / 2, apex - 26) }, Ink, 2);
            var f = new FPath().M(x + w / 2, apex - 26)
                .Q(x + w / 2 + 22, apex - 19, x + w / 2 + 12, apex - 30)
                .Q(x + w / 2, apex - 13, x + w / 2 + 10, apex - 10)
                .Z();
            g.Inked(f, flag, 1.6);
            g.Inked(Gfx.Box(x, y, w, bottom - y), stone);
            for (int k = 0; k < (int)((bottom - y) / 16); k++)                                   // a few stones
            {
                double yy = y + 12 + k * 16, xx = x + (k % 2 == 0 ? 8 : w - 22);
                g.Stroke(Gfx.RBox(xx, yy, 14, 7, 2), stoneShade, 1.4);
            }
            var roof = new FPath().M(x - 8, y + 2).L(x + w / 2, apex)
                .L(x + w + 8, y + 2).Z();
            g.Inked(roof, roofBlue);
            g.Line(new[] { Pt(x + w / 2, apex + 10), Pt(x + w / 2 - 6, y) }, Op(White, 0.35), 3);
            window(x + w / 2 - 7, y + 26);
        }
        void window(double x, double y)
        {
            var p = new FPath().M(x, y + 22).L(x, y + 7)
                .Q(x + 14, y + 7, x + 7, y - 4).L(x + 14, y + 22).Z();
            g.Inked(p, C(0.36, 0.34, 0.46), 1.8);
            g.Fill(Gfx.Ell(x + 3, y + 6, 4, 4), Op(flagGold, 0.8));
        }
        // the keep, behind the wall
        tower(500, 104, 100, 200, 36, flagGold);
        window(526, 150); window(560, 150);
        // the curtain wall (its battlements are a prop — cats sit on the walkway behind them)
        g.Inked(Gfx.Box(428, 190, 244, 124), stone);
        foreach (var (x, y) in new (double, double)[] { (446, 214), (604, 222), (470, 270), (630, 276), (452, 296), (612, 300) })
        {
            g.Stroke(Gfx.RBox(x, y, 16, 8, 2), stoneShade, 1.4);
        }
        // banners
        foreach (var (x, c) in new (double, Color)[] { (472, flagRed), (606, flagRed) })
        {
            var b = new FPath().M(x, 204).L(x + 22, 204).L(x + 22, 244)
                .L(x + 11, 236).L(x, 244).Z();
            g.Inked(b, c, 1.8);
            g.Fill(Gfx.Ell(x + 6, 214, 10, 10), flagGold);
        }
        // the gate
        var gate = new FPath().M(522, 314).L(522, 262)
            .Q(578, 262, 550, 230).L(578, 314).Z();
        g.Inked(gate, C(0.42, 0.32, 0.30));
        foreach (double x in Stride(532, 570, 10, true)) { g.Line(new[] { Pt(x, 254), Pt(x, 312) }, C(0.28, 0.22, 0.22), 2); }
        foreach (double y in new double[] { 272, 292 }) { g.Line(new[] { Pt(524, y), Pt(576, y) }, C(0.28, 0.22, 0.22), 2); }
        // the side towers
        tower(380, 126, 62, 316, 62, flagRed);
        tower(658, 126, 62, 316, 62, flagRed);
    }

    static void house(Gfx g, FindHouse h)
    {
        var w = h.Wall;
        // chimney first (the roof overlaps its foot)
        g.Inked(Gfx.Box(w.MaxX - 34, w.MinY - h.Roof + 4, 14, h.Roof - 4), C(0.80, 0.52, 0.44), 2);
        g.Inked(Gfx.Box(w.MaxX - 37, w.MinY - h.Roof, 20, 6), C(0.70, 0.44, 0.38), 2);
        g.Inked(Gfx.Box(w), h.WallC);
        var roof = new FPath().M(w.MinX - 10, w.MinY + 2)
            .L(w.MidX - w.W * 0.2, w.MinY - h.Roof).L(w.MidX + w.W * 0.2, w.MinY - h.Roof)
            .L(w.MaxX + 10, w.MinY + 2).Z();
        g.Inked(roof, h.RoofC);
        for (int k = 1; k < 3; k++)                                                     // shingle rows
        {
            double y = w.MinY - h.Roof * k / 3, t = k / 3.0;
            g.Line(new[] { Pt(w.MinX - 10 + (w.W * 0.3 + 10) * t + 4, y), Pt(w.MaxX + 10 - (w.W * 0.3 + 10) * t - 4, y) },
                   Op(Ink, 0.25), 1.4);
        }
        // door + windows with flower boxes
        var door = new FPath().M(w.MidX - 11, w.MaxY).L(w.MidX - 11, w.MaxY - 26)
            .Q(w.MidX + 11, w.MaxY - 26, w.MidX, w.MaxY - 40)
            .L(w.MidX + 11, w.MaxY).Z();
        g.Inked(door, woodDark, 2);
        g.Fill(Gfx.Ell(w.MidX + 4, w.MaxY - 16, 3, 3), flagGold);
        foreach (double x in new[] { w.MinX + 12, w.MaxX - 32 })
        {
            g.Inked(Gfx.RBox(x, w.MinY + 12, 20, 18, 2), C(0.70, 0.84, 0.94), 1.8);
            g.Line(new[] { Pt(x + 10, w.MinY + 12), Pt(x + 10, w.MinY + 30) }, Op(Ink, 0.7), 1.4);
            g.Line(new[] { Pt(x, w.MinY + 21), Pt(x + 20, w.MinY + 21) }, Op(Ink, 0.7), 1.4);
            g.Inked(Gfx.RBox(x - 2, w.MinY + 30, 24, 5, 1.5), woodDark, 1.4);
            for (int k = 0; k < 4; k++) { g.Fill(Gfx.Ell(x + k * 6, w.MinY + 27, 5, 5), k % 2 == 0 ? flagRed : flagGold); }
        }
    }

    static void market(Gfx g)
    {
        // bunting strung across the square
        var str = new FPath().M(760, 372).Q(1090, 372, 925, 396);
        g.Stroke(str, Op(Ink, 0.6), 1.4);
        for (int k = 0; k < 11; k++)
        {
            double t = k / 10.0 * 0.92 + 0.04, x = 760 + 330 * t, y = 372 + 4 * 24 * t * (1 - t);
            var f = new FPath().M(x - 7, y).L(x + 7, y).L(x, y + 14).Z();
            g.Inked(f, new[] { flagRed, flagGold, C(0.50, 0.70, 0.92), leafLight }[k % 4], 1.2);
        }
        // three stalls: posts, a back cloth and a striped, scalloped awning (their counters are props in front)
        var stripes = new (Color, Color)[] { (flagRed, White), (C(0.42, 0.62, 0.90), White), (flagGold, White) };
        var xs = new double[] { 766, 878, 990 };
        for (int k = 0; k < xs.Length; k++)
        {
            double x = xs[k];
            foreach (double px in new[] { x + 4, x + 84 }) { g.Inked(Gfx.Box(px, 398, 6, 44), woodDark, 1.6); }
            g.Inked(Gfx.Box(x + 8, 404, 78, 36), C(0.98, 0.94, 0.86), 1.6);
            var (a, b) = stripes[k];
            for (int i = 0; i < 6; i++)
            {
                double sx = x - 4 + i * 17;
                var s = new FPath().M(sx + 4, 388).L(sx + 21, 388).L(sx + 17, 404)
                    .Q(sx, 404, sx + 8.5, 414).Z();
                g.Fill(s, i % 2 == 0 ? a : b);
            }
            var edge = new FPath().M(x, 388).L(x + 98, 388)
                .L(x + 98, 404);
            for (int i = 5; i >= 0; i--)
            {
                double sx = x - 4 + i * 17;
                edge.Q(sx, 404, sx + 8.5, 414);
            }
            edge.Z();
            g.Stroke(edge, Ink, 2, PenLineCap.Flat, PenLineJoin.Round);
        }
    }

    // ── the props ──

    /// One prop (FindPropView.draw). The Swift draws it in its own small canvas, translated to (8,8) past the rect's
    /// corner and offset back in the view: net identity, so this draws straight in scene coordinates.
    public static void Prop(Gfx g, FindProp prop)
    {
        var r = prop.Rect;
        switch (prop.Kind)
        {
            case FindProp.Kinds.Parapet:
            {
                g.Inked(Gfx.Box(r.MinX, r.MinY + 18, r.W, r.H - 18), stone);
                foreach (double x in Stride(r.MinX, r.MaxX - 10, 34, false))
                {
                    g.Inked(Gfx.Box(x, r.MinY, 20, 20), stone);
                }
                g.Fill(Gfx.Box(r.MinX + 1.1, r.MinY + 17, r.W - 2.2, 3), stone);
                break;
            }
            case FindProp.Kinds.Bush:
            {
                double w = r.W, h = r.H;
                g.Blobs(new[] { R(r.MinX, r.MinY + h * 0.3, w * 0.45, h * 0.7),
                                R(r.MinX + w * 0.22, r.MinY, w * 0.5, h * 0.9),
                                R(r.MinX + w * 0.52, r.MinY + h * 0.18, w * 0.48, h * 0.82) }, leaf, 1.1);
                foreach (var (fx, fy) in new (double, double)[] { (0.3, 0.45), (0.6, 0.35), (0.8, 0.6), (0.45, 0.7) })
                {
                    g.Fill(Gfx.Ell(r.MinX + w * fx - 2.5, r.MinY + h * fy - 2.5, 5, 5), C(1, 0.62, 0.70));
                }
                g.Line(new[] { Pt(r.MinX + w * 0.32, r.MinY + h * 0.25), Pt(r.MinX + w * 0.42, r.MinY + h * 0.17) }, leafLight, 3);
                break;
            }
            case FindProp.Kinds.Crate:
            {
                g.Inked(Gfx.RBox(r, 3), wood);
                g.Stroke(Gfx.RBox(r.InsetBy(5, 5), 2), Op(woodDark, 0.8), 1.6);
                g.Line(new[] { Pt(r.MinX + 6, r.MinY + 6), Pt(r.MaxX - 6, r.MaxY - 6) }, Op(woodDark, 0.8), 2.4);
                g.Line(new[] { Pt(r.MaxX - 6, r.MinY + 6), Pt(r.MinX + 6, r.MaxY - 6) }, Op(woodDark, 0.8), 2.4);
                break;
            }
            case FindProp.Kinds.Barrels:
            {
                for (int k = 0; k < 2; k++)
                {
                    var b = R(r.MinX + k * (r.W / 2), r.MinY + k * 4, r.W / 2, r.H - k * 4);
                    g.Inked(Gfx.RBox(b, 9), C(0.76, 0.52, 0.34));
                    foreach (double f in new double[] { 0.22, 0.78 })
                    {
                        g.Line(new[] { Pt(b.MinX + 2, b.MinY + b.H * f), Pt(b.MaxX - 2, b.MinY + b.H * f) }, C(0.42, 0.40, 0.40), 2.6);
                    }
                    g.Fill(Gfx.RBox(b.MinX + 7, b.MinY + 6, 3, b.H - 12, 1.5), Op(White, 0.25));
                }
                break;
            }
            case FindProp.Kinds.Hay:
            {
                g.Inked(Gfx.RBox(r, 10), C(0.97, 0.84, 0.47));
                for (int k = 0; k < 7; k++)
                {
                    double x = r.MinX + 8 + k * 9;
                    g.Line(new[] { Pt(x, r.MinY + 8), Pt(x + 3, r.MaxY - 8) }, C(0.85, 0.66, 0.30), 1.3);
                }
                g.Line(new[] { Pt(r.MinX + 2, r.MidY), Pt(r.MaxX - 2, r.MidY) }, C(0.75, 0.45, 0.30), 2.4);
                break;
            }
            case FindProp.Kinds.Counter:
            {
                var c = prop.Cover;
                // goods on top
                switch (prop.Tint)
                {
                    case 0: for (int k = 0; k < 7; k++) { g.Blobs(new[] { R(c.MinX + 6 + k * 11.5, c.MinY - 9, 11, 11) }, flagRed, 0.9); } break;
                    case 1: for (int k = 0; k < 4; k++) { g.Blobs(new[] { R(c.MinX + 6 + k * 21, c.MinY - 10, 22, 12) }, C(0.90, 0.68, 0.40), 0.9); } break;
                    case 3: for (int k = 0; k < 4; k++) { fish(g, c.MinX + 14 + k * 22, c.MinY - 5, k % 2 == 0); } break;
                    case 4:
                        for (int k = 0; k < 5; k++)
                        {
                            double x = c.MinX + 8 + k * 17;
                            g.Blobs(new[] { R(x, c.MinY - 10, 15, 11) }, C(0.94, 0.50, 0.40), 0.9);
                            foreach (double d in new double[] { 3, 9 }) { g.Fill(Gfx.Ell(x + d, c.MinY - 8, 2.5, 2.5), Ink); }
                        }
                        break;
                    default: for (int k = 0; k < 5; k++) { g.Blobs(new[] { R(c.MinX + 6 + k * 17, c.MinY - 11, 16, 13) }, C(0.66, 0.80, 0.40), 0.9); } break;
                }
                g.Inked(Gfx.Box(c.MinX, c.MinY, c.W, c.H), wood);
                for (int k = 1; k < 4; k++) { g.Line(new[] { Pt(c.MinX + c.W * k / 4, c.MinY + 4), Pt(c.MinX + c.W * k / 4, c.MaxY - 2) }, Op(woodDark, 0.6), 1.4); }
                g.Inked(Gfx.Box(c.MinX - 3, c.MinY - 1, c.W + 6, 6), woodDark, 1.8);
                break;
            }
            case FindProp.Kinds.Well:
            {
                var c = prop.Cover;
                foreach (double px in new[] { c.MinX + 6, c.MaxX - 12 }) { g.Inked(Gfx.Box(px, r.MinY + 14, 6, c.MinY - r.MinY - 10), woodDark, 1.6); }
                var roof = new FPath().M(c.MinX - 4, r.MinY + 20).L(c.MidX, r.MinY)
                    .L(c.MaxX + 4, r.MinY + 20).Z();
                g.Inked(roof, C(0.84, 0.45, 0.36));
                g.Line(new[] { Pt(c.MidX, r.MinY + 20), Pt(c.MidX, c.MinY - 10) }, Op(Ink, 0.7), 1.4);
                g.Inked(Gfx.RBox(c.MidX - 6, c.MinY - 14, 12, 10, 2), C(0.62, 0.62, 0.68), 1.6);
                g.Inked(Gfx.RBox(c, 6), stone);
                g.Inked(Gfx.RBox(c.MinX - 3, c.MinY - 2, c.W + 6, 9, 4), stoneShade);
                foreach (var (x, y) in new (double, double)[] { (10, 16), (36, 14), (22, 26), (48, 28) })
                {
                    g.Stroke(Gfx.RBox(c.MinX + x, c.MinY + y, 14, 7, 2), stoneShade, 1.4);
                }
                break;
            }
            case FindProp.Kinds.Wall:
            {
                g.Inked(Gfx.RBox(prop.Cover, 4), stone);
                int k = 0;
                foreach (double y in Stride(prop.Cover.MinY + 4, prop.Cover.MaxY - 6, 10, false))
                {
                    foreach (double x in Stride(prop.Cover.MinX + (k % 2 == 0 ? 4 : 14), prop.Cover.MaxX - 18, 22, false))
                    {
                        g.Stroke(Gfx.RBox(x, y, 18, 8, 2.5), stoneShade, 1.3);
                    }
                    k += 1;
                }
                foreach (double x in Stride(prop.Cover.MinX + 6, prop.Cover.MaxX, 30, false))     // a little moss
                {
                    g.Blobs(new[] { R(x, prop.Cover.MinY - 3, 12, 7) }, leaf, 0.8);
                }
                break;
            }
            case FindProp.Kinds.Tree:
            {
                var c = prop.Cover;
                g.Inked(Gfx.RBox(c.InsetBy(0, -30).OffsetBy(0, -10), 4), woodDark);
                double w = r.W, h = r.H - c.H - 10;
                g.Blobs(new[] { R(r.MinX, r.MinY + h * 0.3, w * 0.55, h * 0.6),
                                R(r.MinX + w * 0.2, r.MinY, w * 0.6, h * 0.65),
                                R(r.MinX + w * 0.45, r.MinY + h * 0.25, w * 0.55, h * 0.65),
                                R(r.MinX + w * 0.15, r.MinY + h * 0.45, w * 0.7, h * 0.55) }, leaf, 1.1);
                foreach (var (fx, fy) in new (double, double)[] { (0.3, 0.3), (0.65, 0.42), (0.45, 0.65) })
                {
                    g.Fill(Gfx.Ell(r.MinX + w * fx - 4, r.MinY + h * fy - 4, 8, 8), flagRed);
                }
                break;
            }
            case FindProp.Kinds.Boat:
            {
                var c = prop.Cover;
                var hull = new[] { C(0.90, 0.48, 0.38), C(0.42, 0.60, 0.88), C(0.98, 0.80, 0.36) };
                if (prop.Tint != 1)                                                    // a mast with a furled sail and a pennant
                {
                    g.Line(new[] { Pt(c.MidX, c.MinY + 4), Pt(c.MidX, r.MinY + 4) }, woodDark, 3);
                    g.Inked(Gfx.RBox(c.MidX - 4, r.MinY + 18, 8, c.MinY - r.MinY - 26, 4), C(0.98, 0.95, 0.88), 1.4);
                    var pen = new FPath().M(c.MidX + 1, r.MinY + 4).L(c.MidX + 18, r.MinY + 9).L(c.MidX + 1, r.MinY + 14).Z();
                    g.Inked(pen, flagGold, 1.2);
                }
                var b = new FPath().M(c.MinX, c.MinY)
                    .L(c.MaxX, c.MinY - 4)
                    .Q(c.MaxX - c.W * 0.18, c.MaxY, c.MaxX - 2, c.MaxY - 6)
                    .L(c.MinX + c.W * 0.14, c.MaxY)
                    .Q(c.MinX, c.MinY, c.MinX + 2, c.MaxY - 8)
                    .Z();
                Geometry bG = b;
                g.Inked(bG, hull[prop.Tint % 3]);
                g.Clip(bG, () =>
                {
                    g.Fill(Gfx.Box(c.MinX, c.MinY + 6, c.W, 6), Op(White, 0.9));
                });
                g.Stroke(bG, Ink, 2.2, PenLineCap.Flat, PenLineJoin.Round);
                g.Line(new[] { Pt(c.MinX - 2, c.MinY), Pt(c.MaxX + 2, c.MinY - 4) }, woodDark, 3.4);
                foreach (double x in new[] { c.MinX + c.W * 0.3, c.MinX + c.W * 0.62 }) { g.Fill(Gfx.Ell(x, c.MinY + 20, 7, 7), Op(Ink, 0.55)); }
                foreach (double dx in new double[] { 10, 40, 80 })
                {
                    double x = c.MinX + dx, y = c.MaxY + 3;
                    var w = new FPath().M(x, y).Q(x + 16, y, x + 8, y + 4);
                    g.Stroke(w, Op(White, 0.8), 2, PenLineCap.Round);
                }
                break;
            }
            case FindProp.Kinds.Net:
            {
                var m = new FPath().M(r.MinX, r.MaxY)
                    .Q(r.MidX, r.MinY, r.MinX + 10, r.MinY + 4)
                    .Q(r.MaxX, r.MaxY, r.MaxX - 8, r.MinY + 6)
                    .Z();
                Geometry mG = m;
                g.Inked(mG, C(0.56, 0.64, 0.50));
                g.Clip(mG, () =>
                {
                    for (int k = 0; k < 14; k++)
                    {
                        double x = r.MinX - 20 + k * 12;
                        g.Stroke(new FPath().M(x, r.MaxY).L(x + 40, r.MinY), Op(Ink, 0.35), 1.1);
                        g.Stroke(new FPath().M(x + 40, r.MaxY).L(x, r.MinY), Op(Ink, 0.35), 1.1);
                    }
                });
                foreach (var (fx, fy) in new (double, double)[] { (0.3, 0.55), (0.62, 0.4), (0.8, 0.75) })
                {
                    g.Inked(Gfx.Ell(r.MinX + r.W * fx - 5, r.MinY + r.H * fy - 4, 10, 8), C(0.98, 0.62, 0.30), 1.4);
                }
                break;
            }
            case FindProp.Kinds.Pots:
            {
                for (int k = 0; k < 2; k++)
                {
                    var b = R(r.MinX + k * r.W / 2 + 2, r.MinY + (1 - k) * 6, r.W / 2 - 4, r.H - (1 - k) * 6);
                    g.Inked(Gfx.RBox(b, 10), C(0.86, 0.72, 0.52));
                    foreach (double f in new double[] { 0.3, 0.5, 0.7 }) { g.Line(new[] { Pt(b.MinX + b.W * f, b.MinY + 3), Pt(b.MinX + b.W * f, b.MaxY - 3) }, Op(woodDark, 0.7), 1.6); }
                    g.Line(new[] { Pt(b.MinX + 3, b.MidY), Pt(b.MaxX - 3, b.MidY) }, Op(woodDark, 0.7), 1.6);
                }
                break;
            }
            case FindProp.Kinds.FishCrate:
            {
                for (int k = 0; k < 3; k++) { fish(g, r.MinX + 14 + k * (r.W - 28) / 2, r.MinY + 4, k % 2 == 1); }
                var b = R(r.MinX, r.MinY + 6, r.W, r.H - 6);
                g.Inked(Gfx.RBox(b, 3), C(0.62, 0.76, 0.86));
                foreach (double f in new double[] { 0.33, 0.66 }) { g.Line(new[] { Pt(b.MinX + 3, b.MinY + b.H * f), Pt(b.MaxX - 3, b.MinY + b.H * f) }, Op(Ink, 0.35), 1.5); }
                break;
            }
            case FindProp.Kinds.Snowman:
            {
                double w = r.W, h = r.H;
                var snowW = C(0.99, 0.99, 1.0);
                g.Inked(Gfx.Ell(r.MinX, r.MinY + h * 0.5, w, h * 0.5), snowW);
                g.Inked(Gfx.Ell(r.MinX + w * 0.16, r.MinY + h * 0.3, w * 0.68, h * 0.3), snowW);
                g.Line(new[] { Pt(r.MinX + w * 0.18, r.MinY + h * 0.42), Pt(r.MinX - w * 0.12, r.MinY + h * 0.3) }, woodDark, 2.4);
                g.Line(new[] { Pt(r.MaxX - w * 0.18, r.MinY + h * 0.42), Pt(r.MaxX + w * 0.12, r.MinY + h * 0.28) }, woodDark, 2.4);
                g.Inked(Gfx.Ell(r.MinX + w * 0.25, r.MinY + h * 0.12, w * 0.5, h * 0.24), snowW);
                g.Inked(Gfx.RBox(r.MinX + w * 0.2, r.MinY + h * 0.32, w * 0.6, 6, 3), flagRed, 1.6);
                g.Inked(Gfx.RBox(r.MinX + w * 0.56, r.MinY + h * 0.33, 7, 16, 3), flagRed, 1.6);
                g.Inked(Gfx.RBox(r.MinX + w * 0.3, r.MinY + h * 0.1, w * 0.4, 5, 2), C(0.26, 0.26, 0.32), 1.4);
                g.Inked(Gfx.RBox(r.MinX + w * 0.36, r.MinY, w * 0.28, h * 0.12, 2), C(0.26, 0.26, 0.32), 1.4);
                foreach (double dx in new double[] { 0.42, 0.58 }) { g.Fill(Gfx.Ell(r.MinX + w * dx - 2, r.MinY + h * 0.2, 4, 4), Ink); }
                var nose = new FPath().M(r.MidX, r.MinY + h * 0.24).L(r.MidX + 12, r.MinY + h * 0.26).L(r.MidX, r.MinY + h * 0.28).Z();
                g.Inked(nose, C(0.98, 0.58, 0.24), 1.2);
                for (int k = 0; k < 3; k++) { g.Fill(Gfx.Ell(r.MidX - 2, r.MinY + h * (0.38 + k * 0.07), 4, 4), Ink); }
                break;
            }
            case FindProp.Kinds.Sled:
            {
                var c = prop.Cover;
                var run = new FPath().M(c.MinX + 2, c.MaxY - 3).L(c.MaxX - 10, c.MaxY - 3)
                    .Q(c.MaxX, c.MaxY - 16, c.MaxX + 2, c.MaxY - 4);
                g.Stroke(run, Ink, 3, PenLineCap.Round);
                foreach (double x in new[] { c.MinX + 18, c.MaxX - 30 }) { g.Line(new[] { Pt(x, c.MaxY - 3), Pt(x, c.MinY + 16) }, Ink, 2.4); }
                g.Inked(Gfx.RBox(c.MinX, c.MinY + 4, c.W - 6, c.H - 14, 5), prop.Tint == 0 ? flagRed : C(0.42, 0.60, 0.88));
                foreach (double f in new double[] { 0.33, 0.66 }) { g.Line(new[] { Pt(c.MinX + (c.W - 6) * f, c.MinY + 7), Pt(c.MinX + (c.W - 6) * f, c.MaxY - 13) }, Op(White, 0.5), 2); }
                var rope = new FPath().M(c.MaxX - 6, c.MinY + 8).Q(c.MaxX + 4, c.MaxY - 2, c.MaxX + 12, c.MinY + 4);
                g.Stroke(rope, woodDark, 1.6);
                break;
            }
            case FindProp.Kinds.Drift:
            {
                double w = r.W, h = r.H;
                g.Blobs(new[] { R(r.MinX, r.MinY + h * 0.35, w * 0.5, h * 0.65),
                                R(r.MinX + w * 0.22, r.MinY, w * 0.52, h),
                                R(r.MinX + w * 0.5, r.MinY + h * 0.22, w * 0.5, h * 0.78) }, C(0.98, 0.99, 1.0), 1.1, 0.55);
                g.Fill(Gfx.Ell(r.MinX + w * 0.1, r.MaxY - h * 0.3, w * 0.8, h * 0.28), Op(C(0.86, 0.91, 0.98), 0.8));
                foreach (var (fx, fy) in new (double, double)[] { (0.34, 0.3), (0.66, 0.42) }) { g.Blobs(new[] { R(r.MinX + w * fx - 3, r.MinY + h * fy - 3, 6, 6) }, flagRed, 0.6); }
                break;
            }
            case FindProp.Kinds.Woodpile:
            {
                const double rad = 9;
                for (int row = 0; row < 3; row++)
                {
                    int n = 4 - row;
                    for (int k = 0; k < n; k++)
                    {
                        double cx = r.MinX + rad + 4 + row * rad + k * rad * 2.1, cy = r.MaxY - rad - row * rad * 1.7;
                        g.Inked(Gfx.Ell(cx - rad, cy - rad, rad * 2, rad * 2), C(0.86, 0.68, 0.48), 1.6);
                        g.Stroke(Gfx.Ell(cx - 4, cy - 4, 8, 8), Op(woodDark, 0.7), 1.2);
                    }
                }
                g.Blobs(new[] { R(r.MinX + 26, r.MinY - 2, 40, 10) }, White, 0.8);
                break;
            }
            case FindProp.Kinds.Pine:
            {
                var c = prop.Cover;
                g.Inked(Gfx.Box(c.MinX, c.MinY - 8, c.W, c.H + 8), woodDark);
                foreach (var (k, f) in new (int, double)[] { (0, 1.0), (1, 0.8), (2, 0.58) })
                {
                    double bas = c.MinY - 4 - k * (r.H - c.H) * 0.27, half = r.W / 2 * f;
                    var t = new FPath().M(r.MidX - half, bas).L(r.MidX, bas - (r.H - c.H) * 0.44)
                        .L(r.MidX + half, bas).Z();
                    g.Inked(t, C(0.32, 0.56, 0.44));
                    var sn = new FPath().M(r.MidX - half * 0.8, bas - 3)
                        .Q(r.MidX + half * 0.8, bas - 3, r.MidX, bas - 12);
                    g.Stroke(sn, White, 4, PenLineCap.Round);
                }
                break;
            }
            case FindProp.Kinds.Cart:
            {
                var c = prop.Cover;
                g.Inked(Gfx.RBox(c, 4), C(0.70, 0.48, 0.70));
                for (int k = 1; k < 4; k++) { g.Line(new[] { Pt(c.MinX + 3, c.MinY + k * 10), Pt(c.MaxX - 3, c.MinY + k * 10) }, Op(Ink, 0.3), 1.3); }
                g.Line(new[] { Pt(c.MaxX - 2, c.MinY + 14), Pt(c.MaxX + 4, c.MinY + 4) }, woodDark, 3);
                foreach (double x in new[] { c.MinX + 20, c.MaxX - 20 })
                {
                    g.Inked(Gfx.Ell(x - 12, c.MaxY - 10, 24, 24), wood);
                    g.Fill(Gfx.Ell(x - 3, c.MaxY - 1, 6, 6), woodDark);
                }
                break;
            }
        }
    }

    /// A little silver fish lying on its side.
    static void fish(Gfx g, double atX, double atY, bool flip)
    {
        Transform t;
        if (flip) { var tg = new TransformGroup(); tg.Children.Add(new ScaleTransform(-1, 1)); tg.Children.Add(new TranslateTransform(atX, atY)); t = tg; }
        else t = new TranslateTransform(atX, atY);
        t.Freeze();
        g.With(t, () =>
        {
            var tail = new FPath().M(7, 0).L(13, -4).L(13, 4).Z();
            g.Inked(tail, C(0.62, 0.72, 0.84), 1.2);
            g.Inked(Gfx.Ell(-10, -4.5, 19, 9), C(0.74, 0.82, 0.90), 1.2);
            g.Fill(Gfx.Ell(-7, -2, 2.6, 2.6), Ink);
        });
    }

    // ── the kingdom at night ──

    /// The kingdom at night: what still glows above the darkness — stars, the moon, lit windows, lanterns.
    public static void NightLights(Gfx g)
    {
        ulong h = 3;
        for (int n = 0; n < 60; n++)
        {
            h = unchecked(h * 6364136223846793005UL + 1442695040888963407UL);
            double x = (double)((h >> 33) % 1000) / 1000 * 1100, y = (double)((h >> 13) % 1000) / 1000 * 230 + 6, r = 1 + (double)(h % 3) * 0.8;
            var st = new FPath().M(x, y - r * 2)
                .Q(x + r * 2, y, x, y)
                .Q(x, y + r * 2, x, y).Q(x - r * 2, y, x, y)
                .Q(x, y - r * 2, x, y);
            g.Fill(st, Op(C(1, 0.97, 0.85), 0.9));
        }
        glow(g, 968, 84, 70, 0.25);
        g.Inked(Gfx.Ell(940, 56, 56, 56), C(1, 0.97, 0.84), 2, 0.4);
        foreach (var (x, y, r) in new (double, double, double)[] { (956, 72, 7), (976, 92, 5), (962, 96, 3.5) })
        {
            g.Fill(Gfx.Ell(x, y, r * 2, r * 2), C(0.92, 0.88, 0.74));
        }
        var warm = C(1, 0.84, 0.42);
        foreach (var hs in FindScene.Houses)
        {
            foreach (double x in new[] { hs.Wall.MinX + 12, hs.Wall.MaxX - 32 })
            {
                var w = R(x, hs.Wall.MinY + 12, 20, 18);
                glow(g, w.MidX, w.MidY, 26, 0.35);
                g.Inked(Gfx.RBox(w, 2), warm, 1.8);
                g.Line(new[] { Pt(x + 10, w.MinY), Pt(x + 10, w.MaxY) }, Op(Ink, 0.7), 1.4);
                g.Line(new[] { Pt(x, w.MidY), Pt(x + 20, w.MidY) }, Op(Ink, 0.7), 1.4);
            }
        }
        foreach (var (x, y) in new (double, double)[] { (404, 152), (682, 152), (543, 130), (526, 150), (560, 150) })
        {
            glow(g, x + 7, y + 12, 22, 0.35);
            var p = new FPath().M(x, y + 22).L(x, y + 7)
                .Q(x + 14, y + 7, x + 7, y - 4).L(x + 14, y + 22).Z();
            g.Inked(p, warm, 1.8);
        }
        foreach (var (x, y) in new (double, double)[] { (452, 418), (640, 418) })          // lamp posts
        {
            glow(g, x, y - 56, 46, 0.4);
            g.Inked(Gfx.RBox(x - 7, y - 64, 14, 16, 3), warm, 1.6);
        }
        foreach (double x in new double[] { 812, 924, 1036 })                                     // stall lanterns
        {
            glow(g, x, 416, 30, 0.4);
            g.Line(new[] { Pt(x, 404), Pt(x, 410) }, Ink, 1.4);
            g.Inked(Gfx.Ell(x - 6, 409, 12, 14), C(1, 0.62, 0.40), 1.4);
        }
    }

    static readonly Color glowC = C(1, 0.85, 0.5);
    static void glow(Gfx g, double cx, double cy, double r, double a)
    {
        g.Fill(Gfx.Ell(cx - r, cy - r, r * 2, r * 2),
               Gfx.Radial(Pt(cx, cy), r, (0, Op(glowC, a)), (1, Gfx.Clear(glowC))));
    }
}
