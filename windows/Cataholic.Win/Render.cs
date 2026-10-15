using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cataholic;

// `--render-coats <file.png>`: every coat sitting (top row) then napping (bottom row), like CataholicRender.coats
// in Sources/Cataholic/Cataholic.swift (docs/cats.png is the Mac's output — compare side by side).
public static class Render
{
    public static void Coats(string path)
    {
        const double zoom = 2, scale = 2, w = 46 * zoom, h = 62 * zoom, gap = 14, pad = 24;
        int n = CatCoat.All.Count;
        double W = pad * 2 + n * w + (n - 1) * gap, H = pad * 2 + h * 2 - 40;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(CatCoat.C(0.13, 0.13, 0.15)), null, new Rect(0, 0, W, H));
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < n; i++)
                {
                    dc.PushTransform(new TranslateTransform(pad + i * (w + gap), pad + row * (h - 40)));
                    dc.PushTransform(new ScaleTransform(zoom, zoom));
                    CatDrawing.Draw(dc, new CatPose
                    {
                        State = row == 0 ? CatState.Look : CatState.Sleeping, Pin = false, CoatOverride = CatCoat.All[i],
                    });
                    dc.Pop(); dc.Pop();
                }
        }
        var bmp = new RenderTargetBitmap((int)(W * scale), (int)(H * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = System.IO.File.Create(path);
        enc.Save(f);
    }
}
