using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Cataholic;

// Cataholic for Windows — the app shell. Flags (used by CI and the README image):
//   --render-coats <file.png>   draw every coat, sitting then napping, into one image, then quit
//   --quit-after <seconds>      run normally, then quit (the cloud test takes screenshots meanwhile)
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (Arg("--render-coats") is string png) { Render.Coats(png); return 0; }
        if (double.TryParse(Arg("--quit-after"), out var secs))
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(secs) };
            t.Tick += (_, _) => app.Shutdown();
            t.Start();
        }
        app.Startup += (_, _) => Shell.Start();
        return app.Run();
    }
}
