using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// PLACEHOLDER (skeleton): a transparent, always-on-top window with a circle, so the cloud test has something to see.
public static class Shell
{
    public static void Start()
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            Topmost = true, ShowInTaskbar = false, Width = 120, Height = 120, Left = 200, Top = 200,
            Content = new System.Windows.Shapes.Ellipse { Fill = Brushes.Orange, Width = 100, Height = 100 },
        };
        w.Show();
    }
}

public static class Render
{
    public static void Coats(string path)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(33, 33, 38)), null, new Rect(0, 0, 400, 200));
            dc.DrawEllipse(Brushes.Orange, null, new Point(200, 100), 60, 60);
        }
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(400, 200, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var f = System.IO.File.Create(path);
        enc.Save(f);
    }
}
