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

