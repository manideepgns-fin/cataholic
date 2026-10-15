using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cataholic;

// The lookalike visibility self-check (`Cataholic.exe --check-odds`, run by the workflow on every push; the same check as
// Sources/Cataholic/FindOddsCheck.swift). Find my cat's lookalikes are "your cat with one detail wrong" — but a detail can be
// wrong and still invisible (13 Oct 2026: Patches' ink spot landed inside her dark ear patch). So, for every coat × every
// FindOdd, draw the real cat and the lookalike the way the game does, at the smallest size a cat ever appears, and count
// the pixels that differ noticeably. Rule: a pixel differs when any channel is off by more than 40/255; a cell passes with at
// least 3 such pixels. For an odd that ShowsFromAbove only the head (character box y < 26) is counted — all a peeking cat shows.
// Why 3: at that size the faintest detail the game has always shipped is the tail tip, 6 px on both the Mac and Windows
// renderers; half of it is the floor. Measured: the invisible one read 2 (Patches / spot); the weakest accepted are tux / glasses
// (Mac 8, Windows 4) and the tail tips (6).

static class FindOddsCheck
{
    /// The smallest cat in the game: level 5 and endless `Mine` (0.46) × the smallest lookalike jitter (0.98) × a rooftop /
    /// pier cat's perspective (0.92) — at 1× zoom, one DIP per pixel (the camera never zooms out below 1×).
    const double Scale = 0.46 * 0.98 * 0.92;
    const int ChannelStep = 40, MinPixels = 3, W = 48, H = 64;
    static string F(string fmt, params object[] a) => string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt, a);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    /// Like `--check-find`: the table goes to stdout when it is redirected (CI), else to the console that started it.
    public static int Cli()
    {
        if (!Console.IsOutputRedirected) AttachConsole(-1);
        using var o = new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        return Run(o.WriteLine);
    }

    /// BGRA pixels of one cat on a flat grey ground, drawn as the game draws it (FindDraw.Cat, not cropped to its box).
    static byte[] Pixels(FindCat cat)
    {
        var dv = new DrawingVisual();
        bool crop = CatDrawing.CropToBox; CatDrawing.CropToBox = false;
        try
        {
            using var dc = dv.RenderOpen();
            dc.DrawRectangle(new SolidColorBrush(Gfx.C(0.5, 0.5, 0.5)), null, new Rect(0, 0, W, H));
            FindDraw.Cat(new Gfx(dc), cat);
        }
        finally { CatDrawing.CropToBox = crop; }
        var bmp = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var px = new byte[W * H * 4];
        bmp.CopyPixels(px, W * 4, 0);
        return px;
    }

    /// How many pixels of the lookalike differ noticeably from the real cat (head only for an odd seen from above).
    static int Differing(CatCoat coat, FindOdd odd)
    {
        double ox = (W - 46 * Scale) / 2, oy = (H - 62 * Scale) / 2;
        var feet = new Point(ox + 23 * Scale, oy + 49 * Scale);
        var real = new FindCat { Id = 0, Coat = coat, State = CatState.Look, Feet = feet, Scale = Scale };
        var twin = new FindCat { Id = 1, Coat = FindGame.Variant(coat, odd), State = CatState.Look, Feet = feet, Scale = Scale, Odd = odd };
        var was = CatCoat.Current; CatCoat.Current = coat;               // the tail tip's contrast follows the coat on show
        byte[] a, b;
        try { a = Pixels(real); b = Pixels(twin); } finally { CatCoat.Current = was; }
        int n = 0;
        for (int y = 0; y < H; y++)
        {
            if (odd.ShowsFromAbove() && (y + 0.5 - oy) / Scale >= 26) continue;
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                if (Math.Abs(a[i] - b[i]) > ChannelStep || Math.Abs(a[i + 1] - b[i + 1]) > ChannelStep || Math.Abs(a[i + 2] - b[i + 2]) > ChannelStep) n++;
            }
        }
        return n;
    }

    public static int Run(Action<string> print)
    {
        print(F("Find my cat lookalike check — every coat × every odd at the smallest size (scale {0:0.000} ≈ {1:0} × {2:0} px); pass = ≥ {3} pixels differ by > {4}/255",
                Scale, 46 * Scale, 62 * Scale, MinPixels, ChannelStep));
        print("coat      " + string.Concat(FindOddInfo.All.Select(o => o.Name().PadRight(8))));
        var bad = new List<string>();
        foreach (var coat in CatCoat.All)
        {
            string row = coat.Id.PadRight(10);
            foreach (var odd in FindOddInfo.All)
            {
                int n = Differing(coat, odd);
                row += n.ToString().PadRight(8);
                if (n < MinPixels) bad.Add($"{coat.Id}/{odd.Name()} ({n})");
            }
            print(row);
        }
        if (bad.Count > 0) { print("FAIL — too faint to spot: " + string.Join(", ", bad)); return 1; }
        print("PASS — every lookalike's wrong detail shows");
        return 0;
    }
}
